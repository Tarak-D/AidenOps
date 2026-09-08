using System.Collections.Concurrent;
using AIOps.Abstractions.Persistence;
using AIOps.Domain;

namespace AIOps.Infrastructure.Persistence;

/// <summary>In-memory ticket read store for dev/tests. Replaced by EF Core + PostgreSQL in Phase 3.</summary>
public sealed class InMemoryTicketRepository : ITicketRepository
{
    private readonly ConcurrentDictionary<Guid, TicketQueueRow> _rows = new();

    public Task UpsertSnapshotAsync(Guid ticketId, TicketStatus status, TicketDomain domain,
        Severity severity, string title, string reporterEmail, DateTimeOffset occurredAt,
        CancellationToken ct = default)
    {
        _rows[ticketId] = new TicketQueueRow(ticketId, title, domain, severity, status, occurredAt);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TicketQueueRow>> ListAsync(
        TicketStatus? status = null, TicketDomain? domain = null, Severity? severity = null,
        int skip = 0, int take = 50, CancellationToken ct = default)
    {
        IEnumerable<TicketQueueRow> q = _rows.Values;
        if (status is not null) q = q.Where(r => r.Status == status);
        if (domain is not null) q = q.Where(r => r.Domain == domain);
        if (severity is not null) q = q.Where(r => r.Severity == severity);
        return Task.FromResult<IReadOnlyList<TicketQueueRow>>(
            q.OrderByDescending(r => r.UpdatedAt).Skip(skip).Take(take).ToList());
    }
}
