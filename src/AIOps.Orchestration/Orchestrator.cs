using AIOps.Abstractions.Agents;
using AIOps.Abstractions.Audit;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Grains;
using AIOps.Abstractions.Diagnostics;
using AIOps.Abstractions.Persistence;
using AIOps.Abstractions.Time;
using AIOps.Abstractions.Tools;
using AIOps.Domain;
using AIOps.Contracts.Api;
using AIOps.Contracts.AgentGateway;
using AIOps.Domain.Entities;
using AIOps.Orchestration.Approvals;
using AIOps.Tools;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIOps.Orchestration;

/// <summary>
/// Orchestrates the ticket lifecycle and coordinates agent runs.
/// Manages the flow from ticket creation through triage, investigation,
/// and resolution/approval.
/// </summary>
public sealed class Orchestrator
{
    private readonly IClusterClient _cluster;
    private readonly IAuditStore _auditStore;
    private readonly IAgentGateway _agentGateway;
    private readonly IToolRegistry _toolRegistry;
    private readonly Agents.ToolProposalValidationService _proposalValidation;
    private readonly IActionExecutionStore _actionExecutionStore;
    private readonly IApprovalStore _approvalStore;
    private readonly ApprovalService _approvalService;
    private readonly ToolExecutor _toolExecutor;
    private readonly IClock _clock;
    private readonly AgentPolicyOptions _agentPolicyOptions;
    private readonly ILogger<Orchestrator> _logger;

    public Orchestrator(
        IClusterClient cluster,
        IAuditStore auditStore,
        IAgentGateway agentGateway,
        IToolRegistry toolRegistry,
        Agents.ToolProposalValidationService proposalValidation,
        IActionExecutionStore actionExecutionStore,
        IApprovalStore approvalStore,
        ApprovalService approvalService,
        ToolExecutor toolExecutor,
        IClock clock,
        IOptions<AgentPolicyOptions> agentPolicyOptions,
        ILogger<Orchestrator> logger)
    {
        _cluster = cluster;
        _auditStore = auditStore;
        _agentGateway = agentGateway;
        _toolRegistry = toolRegistry;
        _proposalValidation = proposalValidation;
        _actionExecutionStore = actionExecutionStore;
        _approvalStore = approvalStore;
        _approvalService = approvalService;
        _toolExecutor = toolExecutor;
        _clock = clock;
        _agentPolicyOptions = agentPolicyOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new ticket and starts the initial agent run.
    /// </summary>
    public async Task<TicketDetailDto> CreateAndStartTicketAsync(
        CreateTicketRequest request,
        string actorId,
        CancellationToken ct = default)
    {
        var id = Guid.NewGuid();
        var grain = _cluster.GetGrain<ITicketGrain>(id);
        var cmd = new CreateTicketCommand(
            request.Title,
            request.Description,
            request.ReporterEmail,
            request.ReporterName,
            request.Source,
            request.ExternalRef);
        var state = await grain.Create(cmd);
        await _auditStore.AppendAsync(new AuditRecordInput(
            CorrelationId: Guid.NewGuid(),
            ActorType: ActorKind.Human,
            ActorId: actorId,
            EventType: "TicketCreated",
            EntityType: "ticket",
            EntityId: id.ToString("N"),
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new { title = state.Title, description = state.Description })));
        return Map(id, state);
    }

    /// <summary>
    /// Starts an agent run for an existing ticket.
    /// </summary>
    public Task<AgentRunResult> StartAgentRunAsync(
        Guid ticketId,
        AgentRunRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return AIOpsDiagnostics.TrackAsync(
            "agent",
            "Orchestrator",
            "start_run",
            () => StartAgentRunCoreAsync(ticketId, request, ct),
            ct,
            request.CorrelationId,
            result => result.Outcome == AgentRunOutcome.Failed ? "agent_run_failed" : null);
    }

    private async Task<AgentRunResult> StartAgentRunCoreAsync(
        Guid ticketId,
        AgentRunRequest request,
        CancellationToken ct)
    {
        var grain = _cluster.GetGrain<ITicketGrain>(ticketId);
        var state = await grain.GetState();
        if (!state.Exists)
            throw new DomainInvariantViolationException($"Ticket {ticketId} does not exist.");

        if (request.Ticket.TicketId != ticketId)
        {
            throw new DomainInvariantViolationException(
                "Agent run ticket context does not match the requested ticket.");
        }

        // The manifest is always rebuilt from registered server-side tools.
        // Caller-supplied or model-supplied risk/schema data is never authoritative.
        var serverRequest = request with
        {
            AllowedTools = _toolRegistry.Describe()
                .Select(tool => new ToolManifestEntry(
                    tool.Name,
                    tool.Description,
                    tool.Risk,
                    tool.RequiresApproval,
                    tool.InputSchemaJson))
                .ToArray(),
            TriageConfidenceThreshold = _agentPolicyOptions.TriageConfidenceThreshold,
            MaxAttempts = _agentPolicyOptions.MaxAttempts
        };

        var result = await _agentGateway.StartRunAsync(serverRequest, ct);
        result = await _proposalValidation.ValidateAndPersistAsync(
            ticketId,
            request.CorrelationId,
            result,
            ct);
        result = await ProcessValidatedProposalAsync(
            ticketId,
            request.CorrelationId,
            result,
            ct);
        await _auditStore.AppendAsync(new AuditRecordInput(
            CorrelationId: request.CorrelationId,
            ActorType: ActorKind.Agent,
            ActorId: "orchestrator",
            EventType: "AgentRunStarted",
            EntityType: "ticket",
            EntityId: ticketId.ToString("N"),
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(result)));
        return result;
    }

    /// <summary>
    /// Records an approval decision and executes the action only after the
    /// persisted approval and action have both reached their approved states.
    /// </summary>
    public async Task<ApprovalResponse> DecideApprovalAsync(
        Guid approvalId,
        bool approve,
        string decidedBy,
        string? comment,
        CancellationToken ct = default)
    {
        ApprovalResponse approval;
        try
        {
            approval = await _approvalService.DecideAsync(
                approvalId,
                approve,
                decidedBy,
                comment,
                ct);
        }
        catch (DomainInvariantViolationException)
        {
            var expired = await _approvalStore.GetAsync(approvalId, ct);
            if (expired?.Status == ApprovalStatus.Expired)
            {
                await ResumeAgentRunAsync(
                    CreateResumeSelector(expired.TicketId, expired.ActionExecutionId),
                    ct);
            }

            throw;
        }

        if (approval.Status == ApprovalStatus.Approved)
        {
            await ExecuteApprovedActionAsync(
                approval.ActionExecutionId,
                ct);
        }
        else if (approval.Status == ApprovalStatus.Rejected)
        {
            await ResumeAgentRunAsync(
                CreateResumeSelector(approval.TicketId, approval.ActionExecutionId),
                ct);
        }

        return approval;
    }

    /// <summary>
    /// Expires pending approvals and resumes any affected agent run from the
    /// persisted approval/action state.
    /// </summary>
    public async Task<IReadOnlyList<ApprovalResponse>> ListPendingApprovalsAsync(
        CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var expired = (await _approvalStore.ListPendingAsync(ct))
            .Where(approval => approval.IsExpired(now))
            .ToArray();

        var pending = await _approvalService.ListPendingAsync(ct);
        foreach (var approval in expired)
        {
            try
            {
                await ResumeAgentRunAsync(
                    CreateResumeSelector(approval.TicketId, approval.ActionExecutionId),
                    ct);
            }
            catch (DomainInvariantViolationException ex)
            {
                _logger.LogError(
                    ex,
                    "Could not resume expired action {ActionExecutionId}.",
                    approval.ActionExecutionId);
            }
        }

        return pending;
    }

    /// <summary>
    /// Executes a persisted approved action through ToolExecutor's safety checks.
    /// </summary>
    public async Task<ToolResult> ExecuteApprovedActionAsync(
        Guid actionExecutionId,
        CancellationToken ct = default)
    {
        var action = await _actionExecutionStore.GetAsync(
            actionExecutionId,
            ct);

        if (action is null)
        {
            throw new DomainInvariantViolationException(
                $"Action execution {actionExecutionId} does not exist.");
        }

        if (!action.RequiresApproval || action.Status != ActionStatus.Approved)
        {
            throw new DomainInvariantViolationException(
                "Only a persisted approval-required action in Approved status can execute.");
        }

        var approval = await _approvalStore.GetForActionExecutionAsync(
            actionExecutionId,
            ct);
        if (approval is null ||
            approval.ActionExecutionId != action.Id ||
            approval.TicketId != action.TicketId ||
            approval.Status != ApprovalStatus.Approved ||
            approval.ExpiresAt <= _clock.UtcNow)
        {
            throw new DomainInvariantViolationException(
                "Action execution requires a persisted, approved, unexpired request bound to the same action and ticket.");
        }

        var result = await ExecuteActionAsync(
            action,
            action.Id,
            ct);
        await ResumeAgentRunAsync(
            CreateResumeSelector(action.TicketId, action.Id),
            ct);
        return result;
    }

    private async Task<AgentRunResult> ProcessValidatedProposalAsync(
        Guid ticketId,
        Guid correlationId,
        AgentRunResult result,
        CancellationToken ct)
    {
        if (result.Outcome != AgentRunOutcome.ProposalCreated ||
            result.Proposal is not { } proposal ||
            proposal.ActionExecutionId is not { } actionExecutionId)
        {
            return result;
        }

        var action = await _actionExecutionStore.GetAsync(
            actionExecutionId,
            ct);
        if (action is null || action.TicketId != ticketId)
        {
            throw new DomainInvariantViolationException(
                "Validated proposal does not reference a persisted action for this ticket.");
        }

        if (action.Status != ActionStatus.Proposed)
        {
            throw new DomainInvariantViolationException(
                $"Newly validated action must be Proposed (current: {action.Status}).");
        }

        if (action.RequiresApproval)
        {
            await _approvalService.CreateAsync(
                action.Id,
                action.ProposedBy,
                proposal.Justification,
                ct);

            return result with
            {
                Outcome = AgentRunOutcome.AwaitingApproval,
                EscalationSummary = null
            };
        }

        await ExecuteActionAsync(
            action,
            correlationId,
            ct);

        var resume = await ResumeAgentRunAsync(
            CreateResumeSelector(ticketId, action.Id),
            ct);

        return result with
        {
            Outcome = resume.Outcome,
            EscalationSummary = resume.EscalationSummary,
            ResolutionSummary = resume.ResolutionSummary,
            Error = resume.Error,
            Trace = result.Trace.Concat(resume.Trace).ToArray()
        };
    }

    private async Task<ToolResult> ExecuteActionAsync(
        ActionExecution action,
        Guid correlationId,
        CancellationToken ct)
    {
        action.BeginExecution();
        await _actionExecutionStore.UpdateAsync(action, ct);
        await AppendActionExecutionAuditAsync(
            correlationId,
            action,
            "ActionExecutionStarted",
            null,
            ct);

        ToolResult result;
        try
        {
            result = await _toolExecutor.ExecuteAsync(action, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Tool execution failed for action {ActionExecutionId}.",
                action.Id);
            result = new ToolResult(
                false,
                "Tool execution failed.",
                Error: ex.Message);
        }

        var now = _clock.UtcNow;
        if (result.Success)
        {
            action.MarkSucceeded(result.Summary, now);
        }
        else
        {
            var failureSummary = string.IsNullOrWhiteSpace(result.Error)
                ? result.Summary
                : $"{result.Summary} Error: {result.Error}";
            action.MarkFailed(failureSummary, now);
        }

        await _actionExecutionStore.UpdateAsync(action, ct);
        await AppendActionExecutionAuditAsync(
            correlationId,
            action,
            result.Success
                ? "ActionExecutionSucceeded"
                : "ActionExecutionFailed",
            result,
            ct);

        return result;
    }

    private Task AppendActionExecutionAuditAsync(
        Guid correlationId,
        ActionExecution action,
        string eventType,
        ToolResult? result,
        CancellationToken ct)
    {
        return _auditStore.AppendAsync(
            new AuditRecordInput(
                correlationId,
                ActorKind.System,
                "orchestrator",
                eventType,
                "action_execution",
                action.Id.ToString("N"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    action.Id,
                    action.TicketId,
                    action.ToolName,
                    action.Risk,
                    action.RequiresApproval,
                    action.Status,
                    result?.Success,
                    result?.Summary,
                    result?.DetailsJson,
                    result?.Error
                })),
            ct);
    }

    /// <summary>
    /// Resumes an agent run (e.g., after human approval).
    /// </summary>
    public async Task<AgentRunResult> ResumeAgentRunAsync(
        ResumeAgentRunRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ResumeAgentRunAsync(
            request.TicketId,
            request.ActionExecutionId,
            ct);
    }

    /// <summary>
    /// Resumes from a narrow ticket/action selector. All approval and execution
    /// values are loaded from persisted server state.
    /// </summary>
    public Task<AgentRunResult> ResumeAgentRunAsync(
        Guid ticketId,
        Guid actionExecutionId,
        CancellationToken ct = default) =>
        AIOpsDiagnostics.TrackAsync(
            "agent",
            "Orchestrator",
            "resume_run",
            () => ResumeAgentRunCoreAsync(ticketId, actionExecutionId, ct),
            ct,
            actionExecutionId,
            result => result.Outcome == AgentRunOutcome.Failed ? "agent_run_failed" : null);

    private async Task<AgentRunResult> ResumeAgentRunCoreAsync(
        Guid ticketId,
        Guid actionExecutionId,
        CancellationToken ct)
    {
        var actionId = actionExecutionId;
        var correlationId = actionId == Guid.Empty ? Guid.Empty : actionId;
        await AppendResumeAuditAsync(
            correlationId,
            actionId,
            "AgentResumeRequested",
            new
            {
                ActionExecutionId = actionId,
                TicketId = ticketId
            },
            ct);

        ActionExecution action;
        ApprovalRequest? approval;
        try
        {
            if (actionId == Guid.Empty)
            {
                throw new DomainInvariantViolationException(
                    "An action execution ID is required to resume an agent run.");
            }

            action = await _actionExecutionStore.GetAsync(actionId, ct)
                ?? throw new DomainInvariantViolationException(
                    $"Action execution {actionId} does not exist.");

            if (ticketId != action.TicketId)
            {
                throw new DomainInvariantViolationException(
                    "Resume ticket does not match the persisted action ticket.");
            }

            var ticketState = await _cluster
                .GetGrain<ITicketGrain>(action.TicketId)
                .GetState();
            if (!ticketState.Exists)
            {
                throw new DomainInvariantViolationException(
                    $"Ticket {action.TicketId} does not exist.");
            }

            approval = await _approvalStore.GetForActionExecutionAsync(action.Id, ct);
            var invalidState = ValidateResumeState(action, approval);
            if (invalidState is not null)
            {
                throw new DomainInvariantViolationException(invalidState);
            }
        }
        catch (Exception ex)
        {
            await AppendResumeAuditAsync(
                correlationId,
                actionId,
                "AgentResumeRejected",
                new { reason = ex.Message },
                ct);
            throw;
        }

        var serverRequest = new ResumeAgentRunRequest(
            CorrelationId: action.Id,
            TicketId: action.TicketId,
            ApprovalGranted: approval?.Status == ApprovalStatus.Approved,
            ApprovalDecidedBy: approval?.DecidedBy,
            ToolResultJson: JsonSerializer.Serialize(new
            {
                actionExecutionId = action.Id,
                actionStatus = action.Status.ToString(),
                resultSummary = action.ResultSummary,
                executedAt = action.ExecutedAt
            }),
            ToolExecutionSucceeded: action.Status == ActionStatus.Succeeded,
            ActionExecutionId: action.Id,
            ApprovalStatus: approval?.Status.ToString(),
            ActionStatus: action.Status.ToString(),
            ToolExecutionError: action.Status is ActionStatus.Failed or ActionStatus.Rejected
                ? action.ResultSummary
                : null);

        await AppendResumeAuditAsync(
            action.Id,
            action.Id,
            "AgentResumeAccepted",
            new
            {
                action.Id,
                action.TicketId,
                action.Status,
                approvalStatus = approval?.Status
            },
            ct);

        AgentRunResult result;
        try
        {
            result = await _agentGateway.ResumeRunAsync(serverRequest, ct);
        }
        catch (Exception ex)
        {
            await AppendResumeAuditAsync(
                action.Id,
                action.Id,
                "AgentResumeFailed",
                new { error = ex.Message },
                ct);
            throw;
        }

        await AppendResumeAuditAsync(
            action.Id,
            action.Id,
            "AgentRunResumed",
            result,
            ct);
        if (result.Outcome == AgentRunOutcome.Failed)
        {
            await AppendResumeAuditAsync(
                action.Id,
                action.Id,
                "AgentResumeFailed",
                new { result.Error },
                ct);
        }

        return result;
    }

    private static string? ValidateResumeState(
        ActionExecution action,
        ApprovalRequest? approval)
    {
        if (action.Status is not (ActionStatus.Succeeded or ActionStatus.Failed or ActionStatus.Rejected))
        {
            return $"Action status {action.Status} is not ready for agent resume.";
        }

        if (string.IsNullOrWhiteSpace(action.ResultSummary))
        {
            return "Action result must be persisted before agent resume.";
        }

        if ((action.Status is ActionStatus.Succeeded or ActionStatus.Failed) &&
            action.ExecutedAt is null)
        {
            return "Action execution timestamp must be persisted before agent resume.";
        }

        if (action.RequiresApproval)
        {
            if (approval is null ||
                approval.ActionExecutionId != action.Id ||
                approval.TicketId != action.TicketId)
            {
                return "Approval record is missing or is not bound to the persisted action and ticket.";
            }

            if (action.Status == ActionStatus.Rejected &&
                approval.Status is not (ApprovalStatus.Rejected or ApprovalStatus.Expired))
            {
                return "Rejected action does not have a persisted rejected or expired approval.";
            }

            if ((action.Status is ActionStatus.Succeeded or ActionStatus.Failed) &&
                approval.Status != ApprovalStatus.Approved)
            {
                return "Executed action does not have a persisted approved approval.";
            }
        }
        else if (approval is not null || action.Status == ActionStatus.Rejected)
        {
            return "Non-approval action has an inconsistent approval or rejection state.";
        }

        return null;
    }

    private Task AppendResumeAuditAsync(
        Guid correlationId,
        Guid actionExecutionId,
        string eventType,
        object payload,
        CancellationToken ct)
    {
        return _auditStore.AppendAsync(
            new AuditRecordInput(
                correlationId,
                ActorKind.System,
                "orchestrator",
                eventType,
                "action_execution",
                actionExecutionId == Guid.Empty
                    ? "unknown"
                    : actionExecutionId.ToString("N"),
                JsonSerializer.Serialize(payload)),
            ct);
    }

    private static ResumeAgentRunRequest CreateResumeSelector(
        Guid ticketId,
        Guid actionExecutionId) => new(
            CorrelationId: Guid.Empty,
            TicketId: ticketId,
            ApprovalGranted: false,
            ApprovalDecidedBy: null,
            ToolResultJson: null,
            ToolExecutionSucceeded: false,
            ActionExecutionId: actionExecutionId);

    /// <summary>
    /// Applies a triage decision to a ticket.
    /// </summary>
    public async Task<TicketDetailDto> ApplyTriageAsync(
        Guid ticketId,
        TicketDomain domain,
        Severity severity,
        string actorId,
        CancellationToken ct = default)
    {
        var grain = _cluster.GetGrain<ITicketGrain>(ticketId);
        var before = (await grain.GetState()).Status;
        await grain.ApplyTriage(domain, severity, actorId);
        var result = await grain.GetState();
        await _auditStore.AppendAsync(new AuditRecordInput(
            CorrelationId: Guid.NewGuid(),
            ActorType: ActorKind.Agent,
            ActorId: actorId,
            EventType: "TriageApplied",
            EntityType: "ticket",
            EntityId: ticketId.ToString("N"),
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new { before, domain, severity })));
        return Map(ticketId, result);
    }

    /// <summary>
    /// Resolves a ticket after investigation.
    /// </summary>
    public async Task<TicketDetailDto> ResolveTicketAsync(
        Guid ticketId,
        string actorId,
        CancellationToken ct = default)
    {
        var grain = _cluster.GetGrain<ITicketGrain>(ticketId);
        var state = await grain.GetState();
        if (!state.Exists)
            throw new DomainInvariantViolationException($"Ticket {ticketId} does not exist.");

        await grain.TransitionTo(
            TicketStatus.Resolved,
            ActorKind.Human,
            actorId,
            "Resolved after investigation");
        var result = await grain.GetState();
        await _auditStore.AppendAsync(new AuditRecordInput(
            CorrelationId: Guid.NewGuid(),
            ActorType: ActorKind.Human,
            ActorId: actorId,
            EventType: "TicketResolved",
            EntityType: "ticket",
            EntityId: ticketId.ToString("N"),
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(result)));
        return Map(ticketId, result);
    }

    /// <summary>
    /// Approves a ticket for resolution.
    /// </summary>
    public async Task<TicketDetailDto> ApproveTicketAsync(
        Guid ticketId,
        Guid approvalId,
        string actorId,
        CancellationToken ct = default)
    {
        var grain = _cluster.GetGrain<ITicketGrain>(ticketId);
        var state = await grain.GetState();
        if (!state.Exists)
            throw new DomainInvariantViolationException($"Ticket {ticketId} does not exist.");

        await grain.ApprovalResolved(approvalId, ApprovalStatus.Approved, actorId);
        var result = await grain.GetState();
        await _auditStore.AppendAsync(new AuditRecordInput(
            CorrelationId: Guid.NewGuid(),
            ActorType: ActorKind.Human,
            ActorId: actorId,
            EventType: "TicketApproved",
            EntityType: "ticket",
            EntityId: ticketId.ToString("N"),
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(result)));
        return Map(ticketId, result);
    }

    /// <summary>
    /// Gets the current state of a ticket for reporting.
    /// </summary>
    public async Task<TicketDetailDto?> GetTicketAsync(Guid id, CancellationToken ct = default)
    {
        var state = await _cluster.GetGrain<ITicketGrain>(id).GetState();
        return state.Exists ? Map(id, state) : null;
    }

    private static TicketDetailDto Map(Guid id, TicketState state) => new(
        id,
        state.ExternalRef,
        state.Title,
        state.Description,
        state.ReporterEmail,
        state.ReporterName,
        state.Domain,
        state.Severity,
        state.Status,
        state.Source,
        state.AwaitingApprovalId,
        state.CreatedAt,
        state.UpdatedAt,
        state.ResolvedAt,
        state.RecentActivity.Select(a => new TimelineEntryDto(a.OccurredAt, a.ActorKind, a.ActorId, a.Kind, a.Summary)).ToList()
    );
}
