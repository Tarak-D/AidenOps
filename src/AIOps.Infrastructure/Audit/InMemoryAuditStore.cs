using System.Collections.Concurrent;
using AIOps.Abstractions.Audit;

namespace AIOps.Infrastructure.Audit;

/// <summary>
/// Append-only in-memory audit store for dev/tests (Phase 1-2). Replaced by the EF Core
/// PostgreSQL implementation in Phase 3. Append-only is an interface-level guarantee.
/// </summary>
public sealed class InMemoryAuditStore : IAuditStore
{
    private readonly ConcurrentQueue<AuditRecordView> _records = new();
    private long _sequence;

    public Task AppendAsync(AuditRecordInput record, CancellationToken ct = default)
    {
        var seq = Interlocked.Increment(ref _sequence);
        _records.Enqueue(new AuditRecordView(seq, DateTimeOffset.UtcNow, record));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditRecordView>> QueryAsync(
        string? entityType = null, string? entityId = null, string? actorId = null,
        int take = 100, CancellationToken ct = default)
    {
        IEnumerable<AuditRecordView> q = _records;
        if (entityType is not null) q = q.Where(r => r.Record.EntityType == entityType);
        if (entityId is not null) q = q.Where(r => r.Record.EntityId == entityId);
        if (actorId is not null) q = q.Where(r => r.Record.ActorId == actorId);
        return Task.FromResult<IReadOnlyList<AuditRecordView>>(
            q.OrderByDescending(r => r.Sequence).Take(take).ToList());
    }
}
