namespace AIOps.Abstractions.Integrations;

// Integration seams keep providers behind configuration and out of orchestration.

/// <summary>Directory/identity operations. SIMULATION ONLY.</summary>
public interface IDirectoryService
{
    Task<string> ResetPasswordAsync(string userPrincipalName, CancellationToken ct = default);
    Task<string> GrantGroupAccessAsync(string userPrincipalName, string groupId, CancellationToken ct = default);
}

/// <summary>Cloud compute operations.</summary>
public interface ICloudProvider
{
    Task<string> RestartInstanceAsync(string instanceId, CancellationToken ct = default);
    Task<string> GetInstanceStatusAsync(string instanceId, CancellationToken ct = default);
}

/// <summary>Resolved cloud provider identity for status and operator visibility.</summary>
public sealed record CloudProviderStatus(
    string ConfiguredProvider,
    string ActiveProvider,
    string DisplayName,
    string Mode,
    bool IsProduction);

public interface ICloudProviderStatusService
{
    CloudProviderStatus GetStatus();
}

/// <summary>ITSM connector (ServiceNow-style). SIMULATION ONLY.</summary>
public interface IItsmConnector
{
    Task<string> UpdateTicketAsync(string externalRef, string note, string? state = null, CancellationToken ct = default);
}

/// <summary>Network diagnostics. SIMULATION ONLY.</summary>
public interface INetworkDiagnostics
{
    Task<string> RunVpnDiagnosticsAsync(string userOrDeviceId, CancellationToken ct = default);
}
