using AIOps.Abstractions.Integrations;

namespace AIOps.Abstractions.Persistence;

/// <summary>Persists only the selected network diagnostic category/provider and actor metadata.</summary>
public interface INetworkDiagnosticProviderSelectionStore
{
    Task<NetworkDiagnosticProviderSelection?> GetSelectionAsync(CancellationToken ct = default);

    Task SetSelectionAsync(
        NetworkDiagnosticProviderSelection selection,
        string changedBy,
        CancellationToken ct = default);
}
