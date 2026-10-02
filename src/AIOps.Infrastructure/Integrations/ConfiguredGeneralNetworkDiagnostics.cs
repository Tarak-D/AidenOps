using AIOps.Abstractions.Integrations;

namespace AIOps.Infrastructure.Integrations;

/// <summary>
/// Routes general network diagnostics through the provider selected for
/// the GeneralNetwork category.
/// </summary>
public sealed class ConfiguredGeneralNetworkDiagnostics(
    INetworkDiagnosticProviderSelectionService selectionService,
    IEnumerable<IGeneralNetworkDiagnosticsProvider> providers)
    : IGeneralNetworkDiagnostics
{
    private readonly INetworkDiagnosticProviderSelectionService _selectionService =
        selectionService ?? throw new ArgumentNullException(nameof(selectionService));

    private readonly IReadOnlyCollection<IGeneralNetworkDiagnosticsProvider> _providers =
        providers?.ToArray()
        ?? throw new ArgumentNullException(nameof(providers));

    public async Task<GeneralNetworkDiagnosticResult> RunDiagnosticsAsync(
        GeneralNetworkDiagnosticRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var providerName =
            await _selectionService.GetSelectedProviderAsync(
                NetworkDiagnosticCategories.GeneralNetwork,
                ct);

        var provider =
            _providers.SingleOrDefault(candidate =>
                string.Equals(
                    candidate.ProviderName,
                    providerName,
                    StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            throw new InvalidOperationException(
                $"No general network diagnostic provider is registered for '{providerName}'.");
        }

        return await provider.RunDiagnosticsAsync(
            request,
            ct);
    }
}
