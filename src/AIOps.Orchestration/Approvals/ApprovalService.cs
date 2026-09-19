using System.Text.Json;
using AIOps.Abstractions.Audit;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Persistence;
using AIOps.Abstractions.Time;
using AIOps.Contracts.Api;
using AIOps.Domain;
using AIOps.Domain.Entities;
using Microsoft.Extensions.Options;

namespace AIOps.Orchestration.Approvals;

public sealed class ApprovalService
{
    private readonly IActionExecutionStore _actionStore;
    private readonly IApprovalStore _approvalStore;
    private readonly IAuditStore _auditStore;
    private readonly IClock _clock;
    private readonly ApprovalOptions _options;

    public ApprovalService(
        IActionExecutionStore actionStore,
        IApprovalStore approvalStore,
        IAuditStore auditStore,
        IClock clock,
        IOptions<ApprovalOptions> options)
    {
        _actionStore = actionStore;
        _approvalStore = approvalStore;
        _auditStore = auditStore;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<ApprovalResponse> CreateAsync(
        Guid actionExecutionId,
        string requestedBy,
        string justification,
        CancellationToken ct = default)
    {
        var action = await _actionStore.GetAsync(
            actionExecutionId,
            ct);

        if (action is null)
        {
            throw new DomainInvariantViolationException(
                $"Action execution {actionExecutionId} does not exist.");
        }

        if (!action.RequiresApproval)
        {
            throw new DomainInvariantViolationException(
                "This action does not require human approval.");
        }

        if (action.Status != ActionStatus.Proposed)
        {
            throw new DomainInvariantViolationException(
                $"Approval can only be requested for a Proposed action. " +
                $"Current status: {action.Status}.");
        }

        var existing = await _approvalStore
            .GetForActionExecutionAsync(
                actionExecutionId,
                ct);

        if (existing is not null)
        {
            throw new DomainInvariantViolationException(
                $"Action execution {actionExecutionId} already has an approval request.");
        }

        var now = _clock.UtcNow;

        var approval = ApprovalRequest.Create(
            actionExecutionId,
            action.TicketId,
            requestedBy,
            justification,
            now,
            _options.DefaultTtl);

        action.MarkAwaitingApproval();

        await _actionStore.UpdateAsync(
            action,
            ct);

        await _approvalStore.AddAsync(
            approval,
            ct);

        await WriteAuditAsync(
            "ApprovalRequested",
            approval,
            requestedBy,
            ct);

        return Map(approval);
    }

    public async Task<ApprovalResponse> DecideAsync(
        Guid approvalId,
        bool approve,
        string decidedBy,
        string? comment,
        CancellationToken ct = default)
    {
        var approval = await _approvalStore.GetAsync(
            approvalId,
            ct);

        if (approval is null)
        {
            throw new DomainInvariantViolationException(
                $"Approval request {approvalId} does not exist.");
        }

        var action = await _actionStore.GetAsync(
            approval.ActionExecutionId,
            ct);

        if (action is null)
        {
            throw new DomainInvariantViolationException(
                $"Action execution {approval.ActionExecutionId} does not exist.");
        }

        if (action.TicketId != approval.TicketId)
        {
            throw new DomainInvariantViolationException(
                "Approval ticket does not match the action execution ticket.");
        }

        var now = _clock.UtcNow;

        if (approval.IsExpired(now))
        {
            approval.Expire(now);

            await _approvalStore.UpdateAsync(
                approval,
                ct);

            await WriteAuditAsync(
                "ApprovalExpired",
                approval,
                "system",
                ct);

            throw new DomainInvariantViolationException(
                "Approval request has expired.");
        }

        approval.Decide(
            approve,
            decidedBy,
            comment,
            now);

        if (approve)
        {
            action.MarkApproved();
        }
        else
        {
            action.MarkRejected(comment);
        }

        await _approvalStore.UpdateAsync(
            approval,
            ct);

        await _actionStore.UpdateAsync(
            action,
            ct);

        await WriteAuditAsync(
            approve
                ? "ApprovalApproved"
                : "ApprovalRejected",
            approval,
            decidedBy,
            ct);

        return Map(approval);
    }

    public async Task<ApprovalResponse> GetAsync(
        Guid approvalId,
        CancellationToken ct = default)
    {
        var approval = await _approvalStore.GetAsync(
            approvalId,
            ct);

        if (approval is null)
        {
            throw new DomainInvariantViolationException(
                $"Approval request {approvalId} does not exist.");
        }

        return Map(approval);
    }

    public async Task<IReadOnlyList<ApprovalResponse>> ListPendingAsync(
        CancellationToken ct = default)
    {
        var approvals = await _approvalStore.ListPendingAsync(ct);
        var now = _clock.UtcNow;

        var result = new List<ApprovalResponse>();

        foreach (var approval in approvals)
        {
            if (approval.IsExpired(now))
            {
                approval.Expire(now);

                await _approvalStore.UpdateAsync(
                    approval,
                    ct);

                await WriteAuditAsync(
                    "ApprovalExpired",
                    approval,
                    "system",
                    ct);
            }
            else
            {
                result.Add(Map(approval));
            }
        }

        return result;
    }

    private async Task WriteAuditAsync(
        string eventType,
        ApprovalRequest approval,
        string actorId,
        CancellationToken ct)
    {
        await _auditStore.AppendAsync(
            new AuditRecordInput(
                CorrelationId: approval.ActionExecutionId,
                ActorType: actorId == "system"
                    ? ActorKind.System
                    : ActorKind.Human,
                ActorId: actorId,
                EventType: eventType,
                EntityType: "approval_request",
                EntityId: approval.Id.ToString("N"),
                PayloadJson: JsonSerializer.Serialize(new
                {
                    approval.Id,
                    approval.ActionExecutionId,
                    approval.TicketId,
                    approval.Status,
                    approval.RequestedBy,
                    approval.DecidedBy,
                    approval.DecidedAt,
                    approval.ExpiresAt
                })),
            ct);
    }

    private static ApprovalResponse Map(
        ApprovalRequest approval)
    {
        return new ApprovalResponse(
            approval.Id,
            approval.ActionExecutionId,
            approval.TicketId,
            approval.RequestedBy,
            approval.Justification,
            approval.Status,
            approval.CreatedAt,
            approval.ExpiresAt,
            approval.DecidedBy,
            approval.DecidedAt,
            approval.DecisionComment);
    }
}