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

        return Task.FromResult(
            $$"""{"instanceId":"{{instanceId}}","status":"restarted","provider":"simulated"}""");
    }

    public Task<string> GetInstanceStatusAsync(
        string instanceId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        return Task.FromResult(
            $$"""{"instanceId":"{{instanceId}}","status":"running","provider":"simulated"}""");
    }
}