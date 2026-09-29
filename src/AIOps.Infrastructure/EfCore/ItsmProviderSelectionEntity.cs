namespace AIOps.Infrastructure.EfCore;

/// <summary>Persisted non-secret selection of the active ITSM provider.</summary>
public sealed class ItsmProviderSelectionEntity
{
    public string Id { get; set; } = "active";
    public string Provider { get; set; } = "Simulated";
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "configuration";
}
