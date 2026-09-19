using AIOps.Abstractions.Integrations;

namespace AIOps.Infrastructure.Integrations;

public sealed class SimulatedNetworkDiagnostics : INetworkDiagnostics
{
    public Task<string> RunVpnDiagnosticsAsync(
        string userOrDeviceId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userOrDeviceId);

        return Task.FromResult(
            $$"""{"userOrDeviceId":"{{userOrDeviceId}}","vpn":"healthy","latencyMs":24,"packetLossPercent":0,"provider":"simulated"}""");
    }
}