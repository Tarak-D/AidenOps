using AIOps.Abstractions.Integrations;

namespace AIOps.Infrastructure.Integrations;

public sealed class SimulatedNetworkDiagnostics : INetworkDiagnostics
{
    public Task<VpnDiagnosticResult> RunVpnDiagnosticsAsync(
        VpnDiagnosticRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(new VpnDiagnosticResult(
            Status: "Success",
            Provider: "Simulated",
            ObservationType: NetworkDiagnosticObservationType.Session,
            ObservedAt: DateTimeOffset.UtcNow,
            User: request.TargetType == NetworkDiagnosticTargetType.User ? request.Target : null,
            Device: request.TargetType == NetworkDiagnosticTargetType.Device ? request.Target : null,
            Ip: request.TargetType == NetworkDiagnosticTargetType.Ip ? request.Target : null,
            ConnectionState: "Connected",
            Latency: 24,
            PacketLoss: 0,
            ProviderDetails: new NetworkDiagnosticProviderDetails(
                new Dictionary<string, string?> { ["vpn"] = "healthy" })));
    }
}
