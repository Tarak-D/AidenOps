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

/// <summary>Vendor-independent ticket update operation implemented by the selected ITSM provider.</summary>
public interface IItsmConnector
{
    Task<string> UpdateTicketAsync(string externalRef, string note, string? state = null, CancellationToken ct = default);
}

/// <summary>A provider boundary with safe identity and connectivity status for operator visibility.</summary>
public interface IItsmProviderConnector : IItsmConnector
{
    string ProviderName { get; }
    string DisplayName { get; }
    string Mode { get; }
    bool IsProduction { get; }
    Task<string> GetConnectionStatusAsync(CancellationToken ct = default);
}

public sealed record ItsmProviderStatus(
    string ConfiguredProvider,
    string ActiveProvider,
    string DisplayName,
    string Mode,
    bool IsProduction,
    string ConnectionStatus);

public interface IItsmProviderStatusService
{
    Task<ItsmProviderStatus> GetStatusAsync(CancellationToken ct = default);
    Task<ItsmProviderStatus> SelectProviderAsync(
        string provider,
        string changedBy,
        CancellationToken ct = default);
    Task<string> GetSelectedProviderAsync(CancellationToken ct = default);
}

/// <summary>Network diagnostics. SIMULATION ONLY.</summary>
public interface INetworkDiagnostics
{
    Task<string> RunVpnDiagnosticsAsync(string userOrDeviceId, CancellationToken ct = default);
}

public static class NetworkDiagnosticCategories
{
    public const string Vpn = "Vpn";
    public const string GeneralNetwork = "GeneralNetwork";
}

public static class NetworkDiagnosticConnectionStates
{
    public const string Simulated = "Simulated";
    public const string Connected = "Connected";
    public const string NotConfigured = "NotConfigured";
    public const string Unavailable = "Unavailable";
    public const string AuthenticationFailed = "AuthenticationFailed";
    public const string PermissionDenied = "PermissionDenied";
    public const string Timeout = "Timeout";
    public const string ProviderError = "ProviderError";
}

/// <summary>Safe, provider-independent connectivity state and display reason.</summary>
public sealed record NetworkDiagnosticProviderHealth(string State, string Reason);

/// <summary>Provider identity and bounded connectivity status for a network diagnostic category.</summary>
public interface INetworkDiagnosticProvider
{
    string Category { get; }
    string ProviderName { get; }
    string DisplayName { get; }
    string Mode { get; }
    bool IsProduction { get; }
    Task<NetworkDiagnosticProviderHealth> GetConnectionStatusAsync(CancellationToken ct = default);
}

/// <summary>Existing VPN diagnostic operation exposed by a selected VPN provider.</summary>
public interface IVpnDiagnosticsProvider : INetworkDiagnosticProvider
{
    Task<string> RunVpnDiagnosticsAsync(string userOrDeviceId, CancellationToken ct = default);
}

public sealed record NetworkDiagnosticProviderSelection(string Category, string Provider);

public sealed record NetworkDiagnosticProviderStatus(
    string Category,
    string ConfiguredProvider,
    string ActiveProvider,
    string DisplayName,
    string Mode,
    bool IsProduction,
    string ConnectionStatus,
    string StatusReason);

public interface INetworkDiagnosticProviderSelectionService
{
    Task<NetworkDiagnosticProviderStatus> GetStatusAsync(CancellationToken ct = default);
    Task<NetworkDiagnosticProviderStatus> SelectProviderAsync(
        string category,
        string provider,
        string changedBy,
        CancellationToken ct = default);
    Task<NetworkDiagnosticProviderSelection> GetSelectionAsync(CancellationToken ct = default);
    Task<string> GetSelectedProviderAsync(string category, CancellationToken ct = default);
}
