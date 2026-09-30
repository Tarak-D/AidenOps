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
    Task<VpnDiagnosticResult> RunVpnDiagnosticsAsync(VpnDiagnosticRequest request, CancellationToken ct = default);
}

public enum NetworkDiagnosticTargetType
{
    User,
    Device,
    Ip,
    Host,
    Url,
    Service,
    Monitor
}

public enum NetworkDiagnosticObservationType
{
    Session,
    ActiveTest,
    Monitoring
}

/// <summary>Explicitly typed target for a VPN diagnostic request.</summary>
public sealed record VpnDiagnosticRequest
{
    public VpnDiagnosticRequest(string target, NetworkDiagnosticTargetType targetType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (targetType is not (NetworkDiagnosticTargetType.User or NetworkDiagnosticTargetType.Device or NetworkDiagnosticTargetType.Ip))
        {
            throw new ArgumentOutOfRangeException(nameof(targetType), "VPN target type must be User, Device, or Ip.");
        }

        Target = target.Trim();
        TargetType = targetType;
    }

    public string Target { get; }
    public NetworkDiagnosticTargetType TargetType { get; }
}

/// <summary>
/// Provider-normalized details. Sensitive credential-bearing field names are rejected;
/// adapters should add only explicitly normalized, non-secret fields.
/// </summary>
public sealed record NetworkDiagnosticProviderDetails
{
    private static readonly HashSet<string> AllowedFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "agentName", "clientVersion", "connectionType", "devicePlatform", "gateway", "hostName",
        "ipVersion", "location", "metricName", "metricUnit", "monitorId", "networkName",
        "assignedIpv6", "loginTime", "observationId", "profileName", "providerStatus", "publicIp", "region", "sessionId", "source",
        "state", "status", "testId", "tunnelId", "tunnelProtocol", "vpn", "vpnType"
    };
    private static readonly string[] SensitiveFieldParts =
    [
        "password", "secret", "token", "credential", "authorization", "apikey", "api_key",
        "privatekey", "private_key", "certificate", "cookie", "header"
    ];

    public NetworkDiagnosticProviderDetails(IReadOnlyDictionary<string, string?> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Any(field => string.IsNullOrWhiteSpace(field.Key) ||
                                SensitiveFieldParts.Any(part => field.Key.Contains(part, StringComparison.OrdinalIgnoreCase)) ||
                                !AllowedFieldNames.Contains(field.Key.Split('.').Last()) ||
                                LooksLikeAuthorizationMaterial(field.Value)))
        {
            throw new ArgumentException("Provider details may contain only allow-listed normalized fields.", nameof(fields));
        }

        Fields = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string?>(
            fields.ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal));
    }

    public IReadOnlyDictionary<string, string?> Fields { get; }

    private static bool LooksLikeAuthorizationMaterial(string? value) =>
        value is not null &&
        (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
         value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("-----BEGIN ", StringComparison.OrdinalIgnoreCase));
}

public sealed record VpnDiagnosticResult(
    string Status,
    string Provider,
    NetworkDiagnosticObservationType ObservationType,
    DateTimeOffset ObservedAt,
    string? User,
    string? Device,
    string? Ip,
    string? ConnectionState,
    double? Latency,
    double? PacketLoss,
    NetworkDiagnosticProviderDetails? ProviderDetails);

/// <summary>Explicitly typed target for a general network diagnostic request.</summary>
public sealed record GeneralNetworkDiagnosticRequest
{
    public GeneralNetworkDiagnosticRequest(string target, NetworkDiagnosticTargetType targetType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (targetType is not (NetworkDiagnosticTargetType.Ip or NetworkDiagnosticTargetType.Host or
            NetworkDiagnosticTargetType.Url or NetworkDiagnosticTargetType.Service or NetworkDiagnosticTargetType.Monitor))
        {
            throw new ArgumentOutOfRangeException(nameof(targetType), "General network target type must be Ip, Host, Url, Service, or Monitor.");
        }

        Target = target.Trim();
        TargetType = targetType;
    }

    public string Target { get; }
    public NetworkDiagnosticTargetType TargetType { get; }
}

public sealed record GeneralNetworkDiagnosticResult(
    string Status,
    string Provider,
    NetworkDiagnosticObservationType ObservationType,
    DateTimeOffset ObservedAt,
    string Target,
    string State,
    double? Latency,
    double? PacketLoss,
    string? ExecutionId,
    NetworkDiagnosticProviderDetails? ProviderDetails);

/// <summary>General network diagnostic operation; no providers execute through this contract yet.</summary>
public interface IGeneralNetworkDiagnostics
{
    Task<GeneralNetworkDiagnosticResult> RunDiagnosticsAsync(
        GeneralNetworkDiagnosticRequest request,
        CancellationToken ct = default);
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
    Task<VpnDiagnosticResult> RunVpnDiagnosticsAsync(VpnDiagnosticRequest request, CancellationToken ct = default);
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
