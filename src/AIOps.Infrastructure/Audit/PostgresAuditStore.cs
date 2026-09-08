using AIOps.Abstractions.Audit;
using AIOps.Infrastructure.EfCore;
using Microsoft.EntityFrameworkCore;

namespace AIOps.Infrastructure.Audit;

/// <summary>PostgreSQL implementation of the append-only audit store.</summary>
public sealed class PostgresAuditStore : IAuditStore
{
    private readonly AIOpsDbContext _context;

    public PostgresAuditStore(AIOpsDbContext context) => _context = context;

    public async Task AppendAsync(AuditRecordInput record, CancellationToken ct = default)
    {
        var entity = new AuditRecordEntity
        {
            Id = Guid.NewGuid(),
            CorrelationId = record.CorrelationId,
            ActorType = record.ActorType,
            ActorId = record.ActorId,
            EventType = record.EventType,
            EntityType = record.EntityType,
            EntityId = record.EntityId,
            PayloadJson = record.PayloadJson,
            OccurredAt = DateTimeOffset.UtcNow
        };

        _context.AuditRecords.Add(entity);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditRecordView>> QueryAsync(
        string? entityType = null, string? entityId = null, string? actorId = null,
        int take = 100, CancellationToken ct = default)
    {
        var query = _context.AuditRecords.AsQueryable();
        if (entityType is not null) query = query.Where(e => e.EntityType == entityType);
        if (entityId is not null) query = query.Where(e => e.EntityId == entityId);
        if (actorId is not null) query = query.Where(e => e.ActorId == actorId);

        return await query
            .OrderByDescending(e => e.OccurredAt)
            .Take(take)
            .Select(e => new AuditRecordView(
                e.Sequence,
                e.OccurredAt,
                new AuditRecordInput(
                    e.CorrelationId,
                    e.ActorType,
                    e.ActorId,
                    e.EventType,
                    e.EntityType,
                    e.EntityId,
                    e.PayloadJson)))
            .ToListAsync(ct);
    }
}