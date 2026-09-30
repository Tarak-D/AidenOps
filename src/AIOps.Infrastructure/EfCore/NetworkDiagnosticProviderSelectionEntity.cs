namespace AIOps.Infrastructure.EfCore;

/// <summary>Non-secret persisted selection for network diagnostic category/provider.</summary>
public sealed class NetworkDiagnosticProviderSelectionEntity
{
    public string Id { get; set; } = "active-network-diagnostics";
    public string Category { get; set; } = "Vpn";
    public string Provider { get; set; } = "Simulated";
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "configuration";
}
