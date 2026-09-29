using AIOps.Abstractions.Persistence;
using AIOps.Infrastructure.EfCore;
using Microsoft.EntityFrameworkCore;

namespace AIOps.Infrastructure.Integrations;

public sealed class PostgresItsmProviderSelectionStore(
    IDbContextFactory<AIOpsDbContext> contextFactory)
    : IItsmProviderSelectionStore
{
    private const string SelectionId = "active-itsm-provider";

    public async Task<string?> GetProviderAsync(CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.ItsmProviderSelections
            .AsNoTracking()
            .Where(selection => selection.Id == SelectionId)
            .Select(selection => selection.Provider)
            .SingleOrDefaultAsync(ct);
    }

    public async Task SetProviderAsync(
        string provider,
        string changedBy,
        CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO integration_provider_selections (id, provider, updated_at, updated_by)
            VALUES ({SelectionId}, {provider}, {DateTimeOffset.UtcNow}, {changedBy})
            ON CONFLICT (id) DO UPDATE SET
                provider = EXCLUDED.provider,
                updated_at = EXCLUDED.updated_at,
                updated_by = EXCLUDED.updated_by
            """, ct);
    }
}

/// <summary>Fails explicitly when persistent provider selection cannot be stored.</summary>
public sealed class UnavailableItsmProviderSelectionStore : IItsmProviderSelectionStore
{
    private static InvalidOperationException Error() => new(
        "Persistent ITSM provider selection requires a configured PostgreSQL connection.");

    public Task<string?> GetProviderAsync(CancellationToken ct = default) =>
        Task.FromException<string?>(Error());

    public Task SetProviderAsync(
        string provider,
        string changedBy,
        CancellationToken ct = default) =>
        Task.FromException(Error());
}
