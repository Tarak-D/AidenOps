using AIOps.Abstractions.Integrations;

namespace AIOps.Infrastructure.Integrations;

/// <summary>Provider slot with no external integration implemented in this batch.</summary>
public sealed class UnconfiguredNetworkDiagnosticProvider(
    string category,
    string providerName,
    string displayName,
    bool isSimulated = false) : INetworkDiagnosticProvider
{
    public string Category { get; } = category;
    public string ProviderName { get; } = providerName;
    public string DisplayName { get; } = displayName;
    public string Mode { get; } = isSimulated ? "Simulated" : "Real";
    public bool IsProduction { get; } = !isSimulated;

    public Task<NetworkDiagnosticProviderHealth> GetConnectionStatusAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(isSimulated
            ? new NetworkDiagnosticProviderHealth(
                NetworkDiagnosticConnectionStates.Simulated,
                "Diagnostics are handled by the local simulated provider.")
            : new NetworkDiagnosticProviderHealth(
                NetworkDiagnosticConnectionStates.NotConfigured,
                "Provider credentials/configuration are not available."));
    }
}

/// <summary>Simulated VPN adapter preserving the existing simulation implementation.</summary>
public sealed class SimulatedVpnDiagnosticsProvider(SimulatedNetworkDiagnostics diagnostics)
    : IVpnDiagnosticsProvider
{
    public string Category => NetworkDiagnosticCategories.Vpn;
    public string ProviderName => "Simulated";
    public string DisplayName => "Simulated";
    public string Mode => "Simulated";
    public bool IsProduction => false;

    public Task<NetworkDiagnosticProviderHealth> GetConnectionStatusAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new NetworkDiagnosticProviderHealth(
            NetworkDiagnosticConnectionStates.Simulated,
            "Diagnostics are handled by the local simulated provider."));
    }

    public Task<string> RunVpnDiagnosticsAsync(string userOrDeviceId, CancellationToken ct = default) =>
        diagnostics.RunVpnDiagnosticsAsync(userOrDeviceId, ct);
}

/// <summary>Rejects execution through a VPN provider slot until a real adapter is implemented.</summary>
public sealed class UnconfiguredVpnDiagnosticsProvider(
    string providerName,
    string displayName) : IVpnDiagnosticsProvider
{
    public string Category => NetworkDiagnosticCategories.Vpn;
    public string ProviderName { get; } = providerName;
    public string DisplayName { get; } = displayName;
    public string Mode => "Real";
    public bool IsProduction => true;

    public Task<NetworkDiagnosticProviderHealth> GetConnectionStatusAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new NetworkDiagnosticProviderHealth(
            NetworkDiagnosticConnectionStates.NotConfigured,
            "Provider credentials/configuration are not available."));
    }

    public Task<string> RunVpnDiagnosticsAsync(string userOrDeviceId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException("NetworkProviderNotConfigured: Provider credentials/configuration are not available.");
    }
}
