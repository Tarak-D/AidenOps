using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Persistence;
using AIOps.Infrastructure.Integrations;

namespace AIOps.Infrastructure.Tests;

public sealed class NetworkDiagnosticProviderSelectionTests
{
    private sealed class MemorySelectionStore : INetworkDiagnosticProviderSelectionStore
    {
        public NetworkDiagnosticProviderSelection? Selection { get; private set; }
        public string? UpdatedBy { get; private set; }
        public DateTimeOffset? UpdatedAt { get; private set; }

        public Task<NetworkDiagnosticProviderSelection?> GetSelectionAsync(CancellationToken ct = default) =>
            Task.FromResult(Selection);

        public Task SetSelectionAsync(NetworkDiagnosticProviderSelection selection, string changedBy, CancellationToken ct = default)
        {
            Selection = selection;
            UpdatedBy = changedBy;
            UpdatedAt = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }
    }

    private sealed class Provider(string category, string name, string display, bool simulated = false)
        : INetworkDiagnosticProvider
    {
        public string Category { get; } = category;
        public string ProviderName { get; } = name;
        public string DisplayName { get; } = display;
        public string Mode => simulated ? "Simulated" : "Real";
        public bool IsProduction => !simulated;
        public Task<NetworkDiagnosticProviderHealth> GetConnectionStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(simulated
                ? new NetworkDiagnosticProviderHealth(NetworkDiagnosticConnectionStates.Simulated, "Simulated provider.")
                : new NetworkDiagnosticProviderHealth(NetworkDiagnosticConnectionStates.NotConfigured, "Provider credentials/configuration are not available."));
    }

    [Theory]
    [InlineData("Vpn", "Cisco", "Cisco")]
    [InlineData("GeneralNetwork", "CiscoThousandEyes", "Cisco ThousandEyes")]
    public async Task SelectionPersistsValidProviderForCategory(string category, string provider, string displayName)
    {
        var store = new MemorySelectionStore();
        var service = new NetworkDiagnosticProviderSelectionService(store, CreateProviders());

        var status = await service.SelectProviderAsync(category, provider, "admin-user");

        Assert.Equal(category, status.Category);
        Assert.Equal(provider, status.ActiveProvider);
        Assert.Equal(displayName, status.DisplayName);
        Assert.Equal(NetworkDiagnosticConnectionStates.NotConfigured, status.ConnectionStatus);
        Assert.Equal("Provider credentials/configuration are not available.", status.StatusReason);
        Assert.Equal(new NetworkDiagnosticProviderSelection(category, provider), store.Selection);
        Assert.Equal("admin-user", store.UpdatedBy);
        Assert.True(store.UpdatedAt.HasValue);
    }

    [Theory]
    [InlineData("Vpn", "Datadog")]
    [InlineData("GeneralNetwork", "Cisco")]
    [InlineData("Other", "Simulated")]
    [InlineData("Vpn", "Unknown")]
    public async Task UnknownOrIncompatibleSelectionIsRejected(string category, string provider)
    {
        var store = new MemorySelectionStore();
        var service = new NetworkDiagnosticProviderSelectionService(store, CreateProviders());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SelectProviderAsync(category, provider, "admin-user"));
        Assert.Null(store.Selection);
    }

    [Fact]
    public async Task SelectionIsLoadedByNewServiceInstanceWithoutFallback()
    {
        var store = new MemorySelectionStore();
        var writer = new NetworkDiagnosticProviderSelectionService(store, CreateProviders());
        await writer.SelectProviderAsync("Vpn", "Fortinet", "admin-user");

        var reader = new NetworkDiagnosticProviderSelectionService(store, CreateProviders());
        var status = await reader.GetStatusAsync();

        Assert.Equal("Fortinet", status.ActiveProvider);
        Assert.Equal(NetworkDiagnosticConnectionStates.NotConfigured, status.ConnectionStatus);
    }

    [Fact]
    public async Task SimulatedStatusIsTruthfulForBothCategories()
    {
        var store = new MemorySelectionStore();
        var service = new NetworkDiagnosticProviderSelectionService(store, CreateProviders());
        var vpn = await service.GetStatusAsync();
        var general = await service.SelectProviderAsync("GeneralNetwork", "Simulated", "admin-user");

        Assert.Equal(NetworkDiagnosticConnectionStates.Simulated, vpn.ConnectionStatus);
        Assert.Equal(NetworkDiagnosticConnectionStates.Simulated, general.ConnectionStatus);
        Assert.False(vpn.IsProduction);
        Assert.False(general.IsProduction);
    }

    [Fact]
    public async Task ExistingVpnOperationUsesSelectedSimulatedProvider()
    {
        var store = new MemorySelectionStore();
        var selection = new NetworkDiagnosticProviderSelectionService(store, CreateProviders());
        var router = new ConfiguredVpnDiagnostics(selection, CreateVpnProviders());

        var result = await router.RunVpnDiagnosticsAsync("device-42");

        Assert.Contains("\"userOrDeviceId\":\"device-42\"", result);
        Assert.Contains("\"provider\":\"simulated\"", result);
    }

    [Fact]
    public async Task RealVpnSelectionDoesNotFallBackToSimulatedExecution()
    {
        var store = new MemorySelectionStore();
        var selection = new NetworkDiagnosticProviderSelectionService(store, CreateProviders());
        await selection.SelectProviderAsync("Vpn", "Cisco", "admin-user");
        var router = new ConfiguredVpnDiagnostics(selection, CreateVpnProviders());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => router.RunVpnDiagnosticsAsync("device-1"));

        Assert.Contains("NetworkProviderNotConfigured", error.Message);
    }

    private static INetworkDiagnosticProvider[] CreateProviders() =>
    [
        new SimulatedVpnDiagnosticsProvider(new SimulatedNetworkDiagnostics()),
        new Provider("Vpn", "Cisco", "Cisco"),
        new Provider("Vpn", "PaloAltoNetworks", "Palo Alto Networks"),
        new Provider("Vpn", "Fortinet", "Fortinet"),
        new Provider("Vpn", "Cloudflare", "Cloudflare"),
        new Provider("Vpn", "Citrix", "Citrix"),
        new Provider("Vpn", "OpenVPN", "OpenVPN"),
        new Provider("GeneralNetwork", "CiscoThousandEyes", "Cisco ThousandEyes"),
        new Provider("GeneralNetwork", "Datadog", "Datadog"),
        new Provider("GeneralNetwork", "Dynatrace", "Dynatrace"),
        new Provider("GeneralNetwork", "Zabbix", "Zabbix"),
        new Provider("GeneralNetwork", "PRTG", "PRTG"),
        new Provider("GeneralNetwork", "Simulated", "Simulated", simulated: true)
    ];

    private static IVpnDiagnosticsProvider[] CreateVpnProviders() =>
    [
        new SimulatedVpnDiagnosticsProvider(new SimulatedNetworkDiagnostics()),
        new UnconfiguredVpnDiagnosticsProvider("Cisco", "Cisco"),
        new UnconfiguredVpnDiagnosticsProvider("PaloAltoNetworks", "Palo Alto Networks"),
        new UnconfiguredVpnDiagnosticsProvider("Fortinet", "Fortinet"),
        new UnconfiguredVpnDiagnosticsProvider("Cloudflare", "Cloudflare"),
        new UnconfiguredVpnDiagnosticsProvider("Citrix", "Citrix"),
        new UnconfiguredVpnDiagnosticsProvider("OpenVPN", "OpenVPN")
    ];
}
