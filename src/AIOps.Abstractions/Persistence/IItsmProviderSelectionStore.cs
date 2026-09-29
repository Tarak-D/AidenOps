namespace AIOps.Abstractions.Persistence;

/// <summary>Stores only the active ITSM provider selection; never provider credentials.</summary>
public interface IItsmProviderSelectionStore
{
    Task<string?> GetProviderAsync(CancellationToken ct = default);

    Task SetProviderAsync(
        string provider,
        string changedBy,
        CancellationToken ct = default);
}
