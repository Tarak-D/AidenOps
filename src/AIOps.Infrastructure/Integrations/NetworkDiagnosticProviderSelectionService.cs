using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Persistence;

namespace AIOps.Infrastructure.Integrations;

public static class NetworkDiagnosticProviderNames
{
    public const string ConfigurationError =
        "The network diagnostic category/provider selection is invalid or incompatible.";

    public const string SelectionActorRequired =
        "A provider selection actor is required.";

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Providers =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [NetworkDiagnosticCategories.Vpn] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Cisco"] = "Cisco Secure Access",
                ["PaloAltoNetworks"] = "Palo Alto Networks",
                ["Fortinet"] = "Fortinet",
                ["Cloudflare"] = "Cloudflare",
                ["Citrix"] = "Citrix",
                ["OpenVPN"] = "OpenVPN",
                ["Simulated"] = "Simulated"
            },
            [NetworkDiagnosticCategories.GeneralNetwork] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CiscoThousandEyes"] = "Cisco ThousandEyes",
                ["Datadog"] = "Datadog",
                ["Dynatrace"] = "Dynatrace",
                ["Zabbix"] = "Zabbix",
                ["PRTG"] = "PRTG",
                ["Simulated"] = "Simulated"
            }
        };

    public static string NormalizeCategory(string? category) => category?.Trim() switch
    {
        var value when string.Equals(value, NetworkDiagnosticCategories.Vpn, StringComparison.OrdinalIgnoreCase) => NetworkDiagnosticCategories.Vpn,
        "VPN Diagnostics" => NetworkDiagnosticCategories.Vpn,
        var value when string.Equals(value, NetworkDiagnosticCategories.GeneralNetwork, StringComparison.OrdinalIgnoreCase) => NetworkDiagnosticCategories.GeneralNetwork,
        "General Network Diagnostics" => NetworkDiagnosticCategories.GeneralNetwork,
        _ => throw new InvalidOperationException(ConfigurationError)
    };

    public static NetworkDiagnosticProviderSelection Normalize(string? category, string? provider)
    {
        var normalizedCategory = NormalizeCategory(category);
        if (provider is null || !Providers[normalizedCategory].TryGetValue(provider.Trim(), out var displayName))
        {
            throw new InvalidOperationException(ConfigurationError);
        }

        var normalizedProvider = Providers[normalizedCategory]
            .Single(pair => string.Equals(pair.Value, displayName, StringComparison.Ordinal))
            .Key;
        return new NetworkDiagnosticProviderSelection(normalizedCategory, normalizedProvider);
    }

    public static IReadOnlyList<(string Name, string DisplayName)> GetProviders(string category)
    {
        var normalizedCategory = NormalizeCategory(category);
        return Providers[normalizedCategory]
            .Select(pair => (pair.Key, pair.Value))
            .ToArray();
    }
}

public sealed class NetworkDiagnosticProviderSelectionService : INetworkDiagnosticProviderSelectionService
{
    private const string SelectionId = "active-network-diagnostics";
    private readonly INetworkDiagnosticProviderSelectionStore _store;
    private readonly IReadOnlyDictionary<string, INetworkDiagnosticProvider> _providers;

    public NetworkDiagnosticProviderSelectionService(
        INetworkDiagnosticProviderSelectionStore store,
        IEnumerable<INetworkDiagnosticProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(providers);
        _store = store;
        _providers = providers.ToDictionary(
            provider => Key(provider.Category, provider.ProviderName),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<NetworkDiagnosticProviderStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var selection = await GetSelectionAsync(ct);
        var provider = Resolve(selection.Category, selection.Provider);
        NetworkDiagnosticProviderHealth health;
        try
        {
            health = await provider.GetConnectionStatusAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            health = new NetworkDiagnosticProviderHealth(
                NetworkDiagnosticConnectionStates.ProviderError,
                "The provider status check failed.");
        }

        return new NetworkDiagnosticProviderStatus(
            selection.Category,
            selection.Provider,
            provider.ProviderName,
            provider.DisplayName,
            provider.Mode,
            provider.IsProduction,
            health.State,
            health.Reason);
    }

    public async Task<NetworkDiagnosticProviderStatus> SelectProviderAsync(
        string category,
        string provider,
        string changedBy,
        CancellationToken ct = default)
    {
        var selection = NetworkDiagnosticProviderNames.Normalize(category, provider);
        if (string.IsNullOrWhiteSpace(changedBy))
        {
            throw new ArgumentException(NetworkDiagnosticProviderNames.SelectionActorRequired, nameof(changedBy));
        }

        _ = Resolve(selection.Category, selection.Provider);
        await _store.SetSelectionAsync(selection, changedBy, ct);
        return await GetStatusAsync(ct);
    }

    public async Task<NetworkDiagnosticProviderSelection> GetSelectionAsync(CancellationToken ct = default)
    {
        var stored = await _store.GetSelectionAsync(ct);
        var selection = stored is null
            ? new NetworkDiagnosticProviderSelection(NetworkDiagnosticCategories.Vpn, "Simulated")
            : NetworkDiagnosticProviderNames.Normalize(stored.Category, stored.Provider);
        _ = Resolve(selection.Category, selection.Provider);
        return selection;
    }

    public async Task<string> GetSelectedProviderAsync(string category, CancellationToken ct = default)
    {
        var normalizedCategory = NetworkDiagnosticProviderNames.NormalizeCategory(category);
        var selection = await GetSelectionAsync(ct);
        if (!string.Equals(selection.Category, normalizedCategory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected network diagnostic category does not match the requested operation.");
        }

        return selection.Provider;
    }

    public INetworkDiagnosticProvider Resolve(string category, string provider)
    {
        var selection = NetworkDiagnosticProviderNames.Normalize(category, provider);
        if (_providers.TryGetValue(Key(selection.Category, selection.Provider), out var resolved))
        {
            return resolved;
        }

        throw new InvalidOperationException("The selected network diagnostic provider is not registered.");
    }

    private static string Key(string category, string provider) => $"{category}:{provider}";
}

/// <summary>Routes the existing VPN operation to exactly the persisted server-side provider.</summary>
public sealed class ConfiguredVpnDiagnostics : AIOps.Abstractions.Integrations.INetworkDiagnostics
{
    private readonly INetworkDiagnosticProviderSelectionService _selection;
    private readonly IReadOnlyDictionary<string, IVpnDiagnosticsProvider> _providers;

    public ConfiguredVpnDiagnostics(
        INetworkDiagnosticProviderSelectionService selection,
        IEnumerable<IVpnDiagnosticsProvider> providers)
    {
        _selection = selection;
        _providers = providers.ToDictionary(provider => provider.ProviderName, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<VpnDiagnosticResult> RunVpnDiagnosticsAsync(VpnDiagnosticRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var selected = await _selection.GetSelectedProviderAsync(NetworkDiagnosticCategories.Vpn, ct);
        if (!_providers.TryGetValue(selected, out var provider))
        {
            throw new InvalidOperationException("The selected VPN diagnostics provider is not registered.");
        }

        return await provider.RunVpnDiagnosticsAsync(request, ct);
    }
}
