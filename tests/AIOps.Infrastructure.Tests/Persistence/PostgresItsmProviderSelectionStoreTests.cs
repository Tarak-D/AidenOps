using AIOps.Infrastructure.EfCore;
using AIOps.Infrastructure.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Pgvector.EntityFrameworkCore;

namespace AIOps.Infrastructure.Tests.Persistence;

public sealed class PostgresItsmProviderSelectionStoreTests
{
    private const string ConnectionString =
        "Host=127.0.0.1;Port=5432;Database=aios;Username=aios;Password=aiospw";
    private const string SelectionId = "active-itsm-provider";

    [Fact]
    public async Task ProviderSelectionPersistsAcrossStoreInstances()
    {
        var options = new DbContextOptionsBuilder<AIOpsDbContext>()
            .UseNpgsql(ConnectionString, npgsqlOptions => npgsqlOptions.UseVector())
            .Options;
        var factory = new TestContextFactory(options);
        await using (var setup = await factory.CreateDbContextAsync())
        {
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS integration_provider_selections
                (
                    id character varying(100) PRIMARY KEY,
                    provider character varying(100) NOT NULL
                        CHECK (provider IN ('Simulated', 'ServiceNow', 'JiraServiceManagement', 'Zendesk')),
                    updated_at timestamp with time zone NOT NULL,
                    updated_by character varying(200) NOT NULL
                );
                DELETE FROM integration_provider_selections WHERE id = 'active-itsm-provider';
                """);
        }

        try
        {
            var writer = new PostgresItsmProviderSelectionStore(factory);
            await writer.SetProviderAsync("JiraServiceManagement", "integration-test-admin");

            var reader = new PostgresItsmProviderSelectionStore(factory);
            Assert.Equal(
                "JiraServiceManagement",
                await reader.GetProviderAsync());
        }
        finally
        {
            await using var cleanup = await factory.CreateDbContextAsync();
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM integration_provider_selections WHERE id = {SelectionId}");
        }
    }

    private sealed class TestContextFactory(DbContextOptions<AIOpsDbContext> options)
        : IDbContextFactory<AIOpsDbContext>
    {
        public AIOpsDbContext CreateDbContext() => new(options);

        public Task<AIOpsDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
