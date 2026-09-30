using AIOps.Abstractions.Integrations;
using AIOps.Infrastructure.EfCore;
using AIOps.Infrastructure.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Pgvector.EntityFrameworkCore;

namespace AIOps.Infrastructure.Tests.Persistence;

public sealed class PostgresNetworkDiagnosticProviderSelectionStoreTests
{
    private const string ConnectionString =
        "Host=127.0.0.1;Port=5432;Database=aios;Username=aios;Password=aiospw";
    private const string SelectionId = "active-network-diagnostics";

    [Fact]
    public async Task SelectionPersistsAcrossStoreInstancesWithOnlySelectionMetadata()
    {
        var options = new DbContextOptionsBuilder<AIOpsDbContext>()
            .UseNpgsql(ConnectionString, npgsqlOptions => npgsqlOptions.UseVector())
            .Options;
        var factory = new TestContextFactory(options);
        await using (var setup = await factory.CreateDbContextAsync())
        {
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS network_diagnostic_provider_selections
                (
                    id character varying(100) PRIMARY KEY,
                    category character varying(100) NOT NULL
                        CHECK (category IN ('Vpn', 'GeneralNetwork')),
                    provider character varying(100) NOT NULL,
                    updated_at timestamp with time zone NOT NULL,
                    updated_by character varying(200) NOT NULL,
                    CHECK (
                        (category = 'Vpn' AND provider IN ('Cisco', 'PaloAltoNetworks', 'Fortinet', 'Cloudflare', 'Citrix', 'OpenVPN', 'Simulated'))
                        OR
                        (category = 'GeneralNetwork' AND provider IN ('CiscoThousandEyes', 'Datadog', 'Dynatrace', 'Zabbix', 'PRTG', 'Simulated'))
                    )
                );
                DELETE FROM network_diagnostic_provider_selections WHERE id = 'active-network-diagnostics';
                """);
        }

        try
        {
            var writer = new PostgresNetworkDiagnosticProviderSelectionStore(factory);
            await writer.SetSelectionAsync(
                new NetworkDiagnosticProviderSelection("GeneralNetwork", "Datadog"),
                "network-test-admin");

            var reader = new PostgresNetworkDiagnosticProviderSelectionStore(factory);
            Assert.Equal(
                new NetworkDiagnosticProviderSelection("GeneralNetwork", "Datadog"),
                await reader.GetSelectionAsync());

            await using var verify = await factory.CreateDbContextAsync();
            var entity = await verify.NetworkDiagnosticProviderSelections.SingleAsync(item => item.Id == SelectionId);
            Assert.Equal("network-test-admin", entity.UpdatedBy);
            Assert.True(entity.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
            Assert.Equal(SelectionId, entity.Id);
        }
        finally
        {
            await using var cleanup = await factory.CreateDbContextAsync();
            await cleanup.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM network_diagnostic_provider_selections WHERE id = {SelectionId}");
        }
    }

    private sealed class TestContextFactory(DbContextOptions<AIOpsDbContext> options)
        : IDbContextFactory<AIOpsDbContext>
    {
        public AIOpsDbContext CreateDbContext() => new(options);
        public Task<AIOpsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
