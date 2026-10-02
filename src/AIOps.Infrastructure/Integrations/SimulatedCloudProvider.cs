using AIOps.Abstractions.Diagnostics;
using AIOps.Abstractions.Integrations;

namespace AIOps.Infrastructure.Integrations;

/// <summary>
/// Deterministic in-process cloud provider used for development and demos.
/// It never contacts a real cloud platform.
/// </summary>
public sealed class SimulatedCloudProvider : ICloudProvider
{
    public Task<string> RestartInstanceAsync(
        string instanceId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        return AIOpsDiagnostics.TrackAsync(
            "cloud", "Simulated", "restart_instance",
            () => Task.FromResult(
                $$"""{"instanceId":"{{instanceId}}","status":"restarted","provider":"simulated"}"""),
            ct);
    }

    public Task<string> GetInstanceStatusAsync(
        string instanceId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        return AIOpsDiagnostics.TrackAsync(
            "cloud", "Simulated", "get_instance_status",
            () => Task.FromResult(
                $$"""{"instanceId":"{{instanceId}}","status":"running","provider":"simulated"}"""),
            ct);
    }
}
