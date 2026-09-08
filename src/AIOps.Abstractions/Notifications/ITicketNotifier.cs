using AIOps.Domain;

namespace AIOps.Abstractions.Notifications;

/// <summary>
/// Real-time push seam (SignalR implementation in AIOps.Host, Phase 9).
/// Keeps orchestration independent of web transport.
/// </summary>
public interface ITicketNotifier
{
    Task TicketStatusChanged(Guid ticketId, TicketStatus from, TicketStatus to,
        ActorKind actor, string actorId, CancellationToken ct = default);
    Task TimelineEntryAdded(Guid ticketId, string kind, string summary,
        ActorKind actor, string actorId, CancellationToken ct = default);
    Task ApprovalRequested(Guid approvalId, Guid ticketId, string toolName,
        RiskLevel risk, DateTimeOffset expiresAt, CancellationToken ct = default);
    Task ApprovalResolved(Guid approvalId, Guid ticketId, ApprovalStatus outcome,
        string decidedBy, CancellationToken ct = default);
}
