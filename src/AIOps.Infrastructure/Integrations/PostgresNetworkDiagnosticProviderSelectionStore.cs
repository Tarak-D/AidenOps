using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Persistence;
using AIOps.Infrastructure.EfCore;
using Microsoft.EntityFrameworkCore;

namespace AIOps.Infrastructure.Integrations;

public sealed class PostgresNetworkDiagnosticProviderSelectionStore(
    IDbContextFactory<AIOpsDbContext> contextFactory)
    : INetworkDiagnosticProviderSelectionStore
{
    private const string SelectionId = "active-network-diagnostics";

    public async Task<NetworkDiagnosticProviderSelection?> GetSelectionAsync(CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var selection = await context.NetworkDiagnosticProviderSelections
            .AsNoTracking()
            .Where(item => item.Id == SelectionId)
            .Select(item => new { item.Category, item.Provider })
            .SingleOrDefaultAsync(ct);
        return selection is null
            ? null
            : new NetworkDiagnosticProviderSelection(selection.Category, selection.Provider);
    }

    public async Task SetSelectionAsync(
        NetworkDiagnosticProviderSelection selection,
        string changedBy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var normalized = NetworkDiagnosticProviderNames.Normalize(selection.Category, selection.Provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(changedBy);
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO network_diagnostic_provider_selections (id, category, provider, updated_at, updated_by)
            VALUES ({SelectionId}, {normalized.Category}, {normalized.Provider}, {DateTimeOffset.UtcNow}, {changedBy})
            ON CONFLICT (id) DO UPDATE SET
                category = EXCLUDED.category,
                provider = EXCLUDED.provider,
                updated_at = EXCLUDED.updated_at,
                updated_by = EXCLUDED.updated_by
            """, ct);
    }
}

public sealed class UnavailableNetworkDiagnosticProviderSelectionStore
    : INetworkDiagnosticProviderSelectionStore
{
    private static InvalidOperationException Error() => new(
        "Persistent network diagnostic provider selection requires a configured PostgreSQL connection.");

    public Task<NetworkDiagnosticProviderSelection?> GetSelectionAsync(CancellationToken ct = default) =>
        Task.FromException<NetworkDiagnosticProviderSelection?>(Error());

    public Task SetSelectionAsync(
        NetworkDiagnosticProviderSelection selection,
        string changedBy,
        CancellationToken ct = default) => Task.FromException(Error());
}
