namespace AIOps.Abstractions.Integrations;

// Integration seams. Phase 1-15: ONLY mock/simulated implementations exist.
// These interfaces exist so ServiceNow/Entra ID/AWS adapters can be added later
// behind configuration — never during this portfolio build-out.

/// <summary>Directory/identity operations. SIMULATION ONLY.</summary>
public interface IDirectoryService
{
    Task<string> ResetPasswordAsync(string userPrincipalName, CancellationToken ct = default);
    Task<string> GrantGroupAccessAsync(string userPrincipalName, string groupId, CancellationToken ct = default);
}

/// <summary>Cloud compute operations. SIMULATION ONLY.</summary>
public interface ICloudProvider
{
    Task<string> RestartInstanceAsync(string instanceId, CancellationToken ct = default);
    Task<string> GetInstanceStatusAsync(string instanceId, CancellationToken ct = default);
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
