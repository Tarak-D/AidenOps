using AIOps.Abstractions.Integrations;

namespace AIOps.Infrastructure.Integrations;

/// <summary>
/// Deterministic in-process directory service used for development and demos.
/// It never contacts a real identity provider.
/// </summary>
public sealed class SimulatedDirectoryService : IDirectoryService
{
    public Task<string> ResetPasswordAsync(
        string userPrincipalName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrincipalName);

        return Task.FromResult(
            $$"""{"userPrincipalName":"{{userPrincipalName}}","status":"password-reset","provider":"simulated"}""");
    }

    public Task<string> GrantGroupAccessAsync(
        string userPrincipalName,
        string groupId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrincipalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        return Task.FromResult(
            $$"""{"userPrincipalName":"{{userPrincipalName}}","groupId":"{{groupId}}","status":"access-granted","provider":"simulated"}""");
    }
}