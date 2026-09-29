using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Persistence;
using Microsoft.Extensions.Options;

namespace AIOps.Infrastructure.Integrations;

public static class ItsmProviderNames
{
    public const string ConfigurationError =
        "Integrations:Itsm:Provider must be 'Simulated', 'ServiceNow', 'JiraServiceManagement', or 'Zendesk'.";

    public static string Normalize(string? provider) => provider?.Trim().ToUpperInvariant() switch
    {
        "SIMULATED" => "Simulated",
        "SERVICENOW" => "ServiceNow",
        "JIRASERVICEMANAGEMENT" => "JiraServiceManagement",
        "ZENDESK" => "Zendesk",
        _ => throw new InvalidOperationException(ConfigurationError)
    };
}

public sealed class ItsmProviderSelectionService : IItsmProviderStatusService
{
    private readonly IItsmProviderSelectionStore _store;
    private readonly IReadOnlyDictionary<string, IItsmProviderConnector> _connectors;
    private readonly string _defaultProvider;

    public ItsmProviderSelectionService(
        IItsmProviderSelectionStore store,
        IEnumerable<IItsmProviderConnector> connectors,
        IOptions<ItsmIntegrationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(connectors);
        ArgumentNullException.ThrowIfNull(options);

        _store = store;
        _connectors = connectors.ToDictionary(
            connector => connector.ProviderName,
            StringComparer.OrdinalIgnoreCase);
        _defaultProvider = ItsmProviderNames.Normalize(options.Value.Provider);
    }

    public async Task<ItsmProviderStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var providerName = await GetSelectedProviderAsync(ct);
        var connector = Resolve(providerName);
        string connectionStatus;
        try
        {
            connectionStatus = await connector.GetConnectionStatusAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            connectionStatus = "Unavailable";
        }

        return new ItsmProviderStatus(
            providerName,
            connector.ProviderName,
            connector.DisplayName,
            connector.Mode,
            connector.IsProduction,
            connectionStatus);
    }

    public async Task<ItsmProviderStatus> SelectProviderAsync(
        string provider,
        string changedBy,
        CancellationToken ct = default)
    {
        var normalized = ItsmProviderNames.Normalize(provider);
        if (string.IsNullOrWhiteSpace(changedBy))
        {
            throw new ArgumentException("A provider selection actor is required.", nameof(changedBy));
        }

        await _store.SetProviderAsync(normalized, changedBy, ct);
        return await GetStatusAsync(ct);
    }

    public async Task<string> GetSelectedProviderAsync(CancellationToken ct = default)
    {
        var storedProvider = await _store.GetProviderAsync(ct);
        var providerName = storedProvider is null
            ? _defaultProvider
            : ItsmProviderNames.Normalize(storedProvider);

        _ = Resolve(providerName);
        return providerName;
    }

    private IItsmProviderConnector Resolve(string providerName)
    {
        if (_connectors.TryGetValue(providerName, out var connector))
        {
            return connector;
        }

        throw new InvalidOperationException(
            $"No ITSM connector is registered for provider '{providerName}'.");
    }
}

/// <summary>Routes updates to the connector selected in persistent server-side settings.</summary>
public sealed class ConfiguredItsmConnector : IItsmConnector
{
    private readonly IItsmProviderStatusService _selection;
    private readonly IReadOnlyDictionary<string, IItsmProviderConnector> _connectors;

    public ConfiguredItsmConnector(
        IItsmProviderStatusService selection,
        IEnumerable<IItsmProviderConnector> connectors)
    {
        _selection = selection;
        _connectors = connectors.ToDictionary(
            connector => connector.ProviderName,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string> UpdateTicketAsync(
        string externalRef,
        string note,
        string? state = null,
        CancellationToken ct = default)
    {
        var selected = await _selection.GetSelectedProviderAsync(ct);
        if (!_connectors.TryGetValue(selected, out var connector))
        {
            throw new InvalidOperationException(
                $"No ITSM connector is registered for provider '{selected}'.");
        }

        return await connector.UpdateTicketAsync(externalRef, note, state, ct);
    }
}
