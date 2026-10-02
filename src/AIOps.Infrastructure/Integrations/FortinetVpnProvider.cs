using System.Net;
using System.Text.Json;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Diagnostics;
using AIOps.Abstractions.Integrations;
using Microsoft.Extensions.Options;

namespace AIOps.Infrastructure.Integrations;

public sealed class FortinetVpnProvider(
    HttpClient httpClient,
    IOptions<FortinetVpnOptions> options,
    TimeProvider timeProvider) : IVpnDiagnosticsProvider
{
    private const string Provider = "Fortinet";
    private const string DisplayNameValue = "Fortinet FortiGate";

    private readonly FortinetVpnOptions _options = options.Value;

    public string Category => NetworkDiagnosticCategories.Vpn;

    public string ProviderName => Provider;

    public string DisplayName => DisplayNameValue;

    public string Mode => "Real";

    public bool IsProduction => true;

    public async Task<NetworkDiagnosticProviderHealth> GetConnectionStatusAsync(
        CancellationToken ct = default)
    {
        try
        {
            if (!IsConfigured())
            {
                return NotConfigured();
            }

            ValidateBaseUrl();

            using var response = await SendRequestAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return MapHealthFailure(response.StatusCode);
            }

            var body = await response.Content.ReadAsStringAsync(ct);

            if (!TryParseSuccessfulResponse(body, out _))
            {
                return new NetworkDiagnosticProviderHealth(
                    NetworkDiagnosticConnectionStates.ProviderError,
                    "Fortinet FortiGate provider response was invalid.");
            }

            return new NetworkDiagnosticProviderHealth(
                NetworkDiagnosticConnectionStates.Connected,
                "Fortinet FortiGate SSL-VPN monitor API is reachable.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new NetworkDiagnosticProviderHealth(
                NetworkDiagnosticConnectionStates.Timeout,
                "Fortinet FortiGate provider request timed out.");
        }
        catch (FortinetVpnException ex)
        {
            return new NetworkDiagnosticProviderHealth(
                ex.HealthState,
                ex.Message);
        }
        catch (HttpRequestException)
        {
            return new NetworkDiagnosticProviderHealth(
                NetworkDiagnosticConnectionStates.Unavailable,
                "Fortinet FortiGate provider is unavailable.");
        }
        catch
        {
            return new NetworkDiagnosticProviderHealth(
                NetworkDiagnosticConnectionStates.ProviderError,
                "Fortinet FortiGate provider request failed.");
        }
    }

    public async Task<VpnDiagnosticResult> RunVpnDiagnosticsAsync(
        VpnDiagnosticRequest request,
        CancellationToken ct = default)
    {
        if (request.TargetType != NetworkDiagnosticTargetType.User)
        {
            throw new FortinetVpnException(
                FortinetVpnError.UnsupportedTarget,
                "Fortinet FortiGate SSL-VPN diagnostics support User targets only.");
        }

        if (string.IsNullOrWhiteSpace(request.Target))
        {
            throw new FortinetVpnException(
                FortinetVpnError.TargetNotFound,
                "Fortinet FortiGate VPN user target is required.");
        }

        if (!IsConfigured())
        {
            throw new FortinetVpnException(
                FortinetVpnError.NotConfigured,
                "Fortinet FortiGate credentials are not configured.");
        }

        ValidateBaseUrl();

        try
        {
            using var response = await SendRequestAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                throw MapException(response.StatusCode);
            }

            var body = await response.Content.ReadAsStringAsync(ct);

            var sessions = ParseSessions(body);

            var matching = sessions
                .Where(session =>
                    string.Equals(
                        session.User,
                        request.Target,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matching.Count == 0)
            {
                throw new FortinetVpnException(
                    FortinetVpnError.TargetNotFound,
                    "Fortinet FortiGate SSL-VPN user session was not found.");
            }

            if (matching.Count > 1)
            {
                throw new FortinetVpnException(
                    FortinetVpnError.ProviderFailure,
                    "Fortinet FortiGate returned multiple matching SSL-VPN sessions without an unambiguous session identity.");
            }

            var selected = matching[0];

            var providerFields = new Dictionary<string, string?>(
                StringComparer.OrdinalIgnoreCase);

            AddField(
                providerFields,
                "sessionId",
                selected.SessionId);

            AddField(
                providerFields,
                "vpnType",
                "SSL-VPN");

            AddField(
                providerFields,
                "connectionType",
                selected.ConnectionType);

            AddField(
                providerFields,
                "source",
                selected.SourceIp);

            AddField(
                providerFields,
                "state",
                "Connected");

            AddField(
                providerFields,
                "tunnelProtocol",
                selected.TunnelProtocol);

            AddField(
                providerFields,
                "vpn",
                "FortiGate SSL-VPN");

            AddField(
                providerFields,
                "networkName",
                selected.Group);

            return new VpnDiagnosticResult(
                Status: "Success",
                Provider: DisplayNameValue,
                ObservationType: NetworkDiagnosticObservationType.Session,
                ObservedAt: timeProvider.GetUtcNow(),
                User: selected.User,
                Device: null,
                Ip: selected.TunnelIp ?? selected.SourceIp,
                ConnectionState: NetworkDiagnosticConnectionStates.Connected,
                Latency: null,
                PacketLoss: null,
                ProviderDetails: providerFields.Count == 0
                    ? null
                    : new NetworkDiagnosticProviderDetails(
                        providerFields));
        }
        catch (FortinetVpnException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new FortinetVpnException(
                FortinetVpnError.Timeout,
                "Fortinet FortiGate SSL-VPN request timed out.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw new FortinetVpnException(
                FortinetVpnError.Unavailable,
                "Fortinet FortiGate provider is unavailable.");
        }
        catch
        {
            throw new FortinetVpnException(
                FortinetVpnError.ProviderFailure,
                "Fortinet FortiGate provider request failed.");
        }
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        CancellationToken ct)
    {
        var requestUri = BuildRequestUri();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            requestUri);

        request.Headers.TryAddWithoutValidation(
            "Authorization",
            $"Bearer {_options.ApiToken}");

        request.Headers.Accept.ParseAdd(
            "application/json");

        return await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
    }

    private Uri BuildRequestUri()
    {
        var baseUri = GetBaseUri();

        var builder = new UriBuilder(
            new Uri(
                baseUri,
                "api/v2/monitor/vpn/ssl"));

        builder.Query =
            $"vdom={Uri.EscapeDataString(_options.Vdom)}";

        return builder.Uri;
    }

    private Uri GetBaseUri()
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new FortinetVpnException(
                FortinetVpnError.NotConfigured,
                "Fortinet FortiGate BaseUrl is not configured.");
        }

        if (!Uri.TryCreate(
                _options.BaseUrl,
                UriKind.Absolute,
                out var uri) ||
            !string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new FortinetVpnException(
                FortinetVpnError.ProviderFailure,
                "Fortinet FortiGate BaseUrl must use HTTPS.");
        }

        return uri.AbsoluteUri.EndsWith(
            "/",
            StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/");
    }

    private void ValidateBaseUrl()
    {
        _ = GetBaseUri();
    }

    private bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(_options.BaseUrl) &&
        !string.IsNullOrWhiteSpace(_options.ApiToken) &&
        !string.IsNullOrWhiteSpace(_options.Vdom);

    private static NetworkDiagnosticProviderHealth NotConfigured() =>
        new(
            NetworkDiagnosticConnectionStates.NotConfigured,
            "Fortinet FortiGate credentials are not configured.");

    private static NetworkDiagnosticProviderHealth MapHealthFailure(
        HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized =>
                new(
                    NetworkDiagnosticConnectionStates.AuthenticationFailed,
                    "Fortinet FortiGate authentication failed."),

            HttpStatusCode.Forbidden =>
                new(
                    NetworkDiagnosticConnectionStates.PermissionDenied,
                    "Fortinet FortiGate permission was denied."),

            HttpStatusCode.RequestTimeout =>
                new(
                    NetworkDiagnosticConnectionStates.Timeout,
                    "Fortinet FortiGate provider request timed out."),

            HttpStatusCode.TooManyRequests =>
                new(
                    NetworkDiagnosticConnectionStates.ProviderError,
                    "Fortinet FortiGate provider request was throttled."),

            >= HttpStatusCode.InternalServerError =>
                new(
                    NetworkDiagnosticConnectionStates.Unavailable,
                    "Fortinet FortiGate provider is unavailable."),

            _ =>
                new(
                    NetworkDiagnosticConnectionStates.ProviderError,
                    "Fortinet FortiGate provider request failed.")
        };

    private static FortinetVpnException MapException(
        HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized =>
                new(
                    FortinetVpnError.AuthenticationFailed,
                    "Fortinet FortiGate authentication failed."),

            HttpStatusCode.Forbidden =>
                new(
                    FortinetVpnError.PermissionDenied,
                    "Fortinet FortiGate permission was denied."),

            HttpStatusCode.NotFound =>
                new(
                    FortinetVpnError.TargetNotFound,
                    "Fortinet FortiGate VPN resource was not found."),

            HttpStatusCode.RequestTimeout =>
                new(
                    FortinetVpnError.Timeout,
                    "Fortinet FortiGate request timed out."),

            HttpStatusCode.TooManyRequests =>
                new(
                    FortinetVpnError.Throttled,
                    "Fortinet FortiGate request was throttled."),

            >= HttpStatusCode.InternalServerError =>
                new(
                    FortinetVpnError.Unavailable,
                    "Fortinet FortiGate provider is unavailable."),

            _ =>
                new(
                    FortinetVpnError.ProviderFailure,
                    "Fortinet FortiGate provider request failed.")
        };

    private static bool TryParseSuccessfulResponse(
        string body,
        out List<FortinetVpnSession> sessions)
    {
        try
        {
            sessions = ParseSessions(body);
            return true;
        }
        catch
        {
            sessions = [];
            return false;
        }
    }

    private static List<FortinetVpnSession> ParseSessions(
        string body)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(body);
        }
        catch
        {
            throw new FortinetVpnException(
                FortinetVpnError.ProviderFailure,
                "Fortinet FortiGate provider response was malformed.");
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FortinetVpnException(
                    FortinetVpnError.ProviderFailure,
                    "Fortinet FortiGate provider response was malformed.");
            }

            if (root.TryGetProperty(
                    "status",
                    out var status) &&
                status.ValueKind == JsonValueKind.String &&
                string.Equals(
                    status.GetString(),
                    "error",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new FortinetVpnException(
                    FortinetVpnError.ProviderFailure,
                    "Fortinet FortiGate provider returned an error.");
            }

            var sessionArray = FindSessionArray(root);

            if (sessionArray is null)
            {
                throw new FortinetVpnException(
                    FortinetVpnError.ProviderFailure,
                    "Fortinet FortiGate provider response did not contain SSL-VPN session data.");
            }

            var sessions = new List<FortinetVpnSession>();

            foreach (var item in sessionArray.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var session = ParseSession(item);

                if (session.User is not null)
                {
                    sessions.Add(session);
                }
            }

            return sessions;
        }
    }

    private static JsonElement? FindSessionArray(
        JsonElement root)
    {
        foreach (var propertyName in new[]
        {
            "results",
            "data",
            "sessions",
            "session"
        })
        {
            if (TryGetPropertyIgnoreCase(
                    root,
                    propertyName,
                    out var value))
            {
                if (value.ValueKind == JsonValueKind.Array)
                {
                    return value;
                }

                if (value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var nestedName in new[]
                    {
                        "results",
                        "data",
                        "sessions",
                        "session"
                    })
                    {
                        if (TryGetPropertyIgnoreCase(
                                value,
                                nestedName,
                                out var nested) &&
                            nested.ValueKind == JsonValueKind.Array)
                        {
                            return nested;
                        }
                    }
                }
            }
        }

        return null;
    }

    private static FortinetVpnSession ParseSession(
        JsonElement element)
    {
        return new FortinetVpnSession(
            User: GetString(
                element,
                "username",
                "user",
                "name"),

            SessionId: GetString(
                element,
                "index",
                "id",
                "session_id",
                "sessionId"),

            SourceIp: GetString(
                element,
                "source_ip",
                "sourceIp",
                "from",
                "src_ip"),

            TunnelIp: GetString(
                element,
                "tunnel_ip",
                "tunnelIp",
                "tunnel_dest_ip",
                "tunnelDestIp"),

            ConnectionType: GetString(
                element,
                "type",
                "connection_type",
                "connectionType"),

            TunnelProtocol: GetString(
                element,
                "protocol",
                "tunnel_protocol",
                "tunnelProtocol"),

            Group: GetString(
                element,
                "group",
                "user_group",
                "userGroup"));
    }

    private static string? GetString(
        JsonElement element,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetPropertyIgnoreCase(
                    element,
                    name,
                    out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString()?.Trim();

                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            if (value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt64(out var number))
            {
                return number.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static void AddField(
        Dictionary<string, string?> fields,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields[key] = value;
        }
    }

    private sealed record FortinetVpnSession(
        string? User,
        string? SessionId,
        string? SourceIp,
        string? TunnelIp,
        string? ConnectionType,
        string? TunnelProtocol,
        string? Group);
}

public enum FortinetVpnError
{
    NotConfigured,
    AuthenticationFailed,
    PermissionDenied,
    TargetNotFound,
    UnsupportedTarget,
    Throttled,
    Timeout,
    Unavailable,
    ProviderFailure
}

public sealed class FortinetVpnException(
    FortinetVpnError category,
    string message) : Exception(message), IAIOpsSafeTelemetryFailure
{
    public FortinetVpnError Category { get; } = category;

    public string TelemetryErrorType => Category.ToString();

    public string HealthState =>
        Category switch
        {
            FortinetVpnError.NotConfigured =>
                NetworkDiagnosticConnectionStates.NotConfigured,

            FortinetVpnError.AuthenticationFailed =>
                NetworkDiagnosticConnectionStates.AuthenticationFailed,

            FortinetVpnError.PermissionDenied =>
                NetworkDiagnosticConnectionStates.PermissionDenied,

            FortinetVpnError.Timeout =>
                NetworkDiagnosticConnectionStates.Timeout,

            FortinetVpnError.Unavailable =>
                NetworkDiagnosticConnectionStates.Unavailable,

            _ =>
                NetworkDiagnosticConnectionStates.ProviderError
        };
}
