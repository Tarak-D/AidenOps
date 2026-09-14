using AIOps.Abstractions.Agents;
using AIOps.Abstractions.Audit;
using AIOps.Abstractions.Grains;
using AIOps.Domain;
using AIOps.Contracts.Api;
using AIOps.Contracts.AgentGateway;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<Orchestrator> _logger;

    public Orchestrator(IClusterClient cluster, IAuditStore auditStore, IAgentGateway agentGateway, ILogger<Orchestrator> logger)
    {
        _cluster = cluster;
        _auditStore = auditStore;
        _agentGateway = agentGateway;
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
    public async Task<AgentRunResult> StartAgentRunAsync(
        Guid ticketId,
        AgentRunRequest request,
        CancellationToken ct = default)
    {
        var grain = _cluster.GetGrain<ITicketGrain>(ticketId);
        var state = await grain.GetState();
        if (!state.Exists)
            throw new DomainInvariantViolationException($"Ticket {ticketId} does not exist.");

        var result = await _agentGateway.StartRunAsync(request, ct);
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
    /// Resumes an agent run (e.g., after human approval).
    /// </summary>
    public async Task<AgentRunResult> ResumeAgentRunAsync(
        ResumeAgentRunRequest request,
        CancellationToken ct = default)
    {
        var grain = _cluster.GetGrain<ITicketGrain>(request.TicketId);
        var state = await grain.GetState();
        if (!state.Exists)
            throw new DomainInvariantViolationException($"Ticket {request.TicketId} does not exist.");

        var result = await _agentGateway.ResumeRunAsync(request, ct);
        await _auditStore.AppendAsync(new AuditRecordInput(
            CorrelationId: request.CorrelationId,
            ActorType: ActorKind.Agent,
            ActorId: "orchestrator",
            EventType: "AgentRunResumed",
            EntityType: "ticket",
            EntityId: request.TicketId.ToString("N"),
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(result)));
        return result;
    }

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