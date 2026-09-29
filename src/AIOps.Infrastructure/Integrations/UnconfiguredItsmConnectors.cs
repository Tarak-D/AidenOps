using AIOps.Abstractions.Integrations;

namespace AIOps.Infrastructure.Integrations;

/// <summary>Stable failure used until a real vendor API connector is implemented and configured.</summary>
public sealed class ItsmProviderNotConfiguredException(string provider)
    : InvalidOperationException(
        $"ItsmProviderNotConfigured: The {provider} connector is not configured.");

public abstract class UnconfiguredItsmProviderConnector : IItsmProviderConnector
{
    protected UnconfiguredItsmProviderConnector(string providerName, string displayName)
    {
        ProviderName = providerName;
        DisplayName = displayName;
    }

    public string ProviderName { get; }
    public string DisplayName { get; }
    public string Mode => "Real";
    public bool IsProduction => true;

    public Task<string> GetConnectionStatusAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult("NotConfigured");
    }

    public Task<string> UpdateTicketAsync(
        string externalRef,
        string note,
        string? state = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        throw new ItsmProviderNotConfiguredException(ProviderName);
    }
}
