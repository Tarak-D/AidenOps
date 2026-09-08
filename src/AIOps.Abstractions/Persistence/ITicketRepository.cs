using AIOps.Domain;

namespace AIOps.Abstractions.Persistence;

/// <summary>Read-side/query persistence for tickets (list pages, queue views).</summary>
public interface ITicketRepository
{
    Task UpsertSnapshotAsync(Guid ticketId, TicketStatus status, TicketDomain domain,
        Severity severity, string title, string reporterEmail, DateTimeOffset occurredAt,
        CancellationToken ct = default);

    Task<IReadOnlyList<TicketQueueRow>> ListAsync(
        TicketStatus? status = null, TicketDomain? domain = null, Severity? severity = null,
        int skip = 0, int take = 50, CancellationToken ct = default);
}

public sealed record TicketQueueRow(
    Guid TicketId, string Title, TicketDomain Domain, Severity Severity,
    TicketStatus Status, DateTimeOffset UpdatedAt);
