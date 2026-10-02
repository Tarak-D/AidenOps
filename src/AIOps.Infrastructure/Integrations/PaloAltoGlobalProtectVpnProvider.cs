using System.Net;
using System.Xml.Linq;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Diagnostics;
using AIOps.Abstractions.Integrations;
using Microsoft.Extensions.Options;

namespace AIOps.Infrastructure.Integrations;

public sealed class PaloAltoGlobalProtectVpnProvider(
    HttpClient httpClient,
    IOptions<PaloAltoGlobalProtectOptions> options,
    TimeProvider timeProvider) : IVpnDiagnosticsProvider
{
    private const string Provider = "PaloAltoNetworks";
    private const string DisplayNameValue = "Palo Alto Networks";

    private readonly PaloAltoGlobalProtectOptions _options = options.Value;

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
                    "Palo Alto Networks provider request failed.");
            }

            return new NetworkDiagnosticProviderHealth(
                NetworkDiagnosticConnectionStates.Connected,
                "Palo Alto Networks GlobalProtect API is reachable.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new NetworkDiagnosticProviderHealth(
                NetworkDiagnosticConnectionStates.Timeout,
                "Palo Alto Networks provider request timed out.");
        }
        catch (PaloAltoGlobalProtectException ex)
        {
            return new NetworkDiagnosticProviderHealth(
                ex.HealthState,
                ex.Message);
        }
        catch (HttpRequestException)
        {
            return new NetworkDiagnosticProviderHealth(
                NetworkDiagnosticConnectionStates.Unavailable,
                "Palo Alto Networks provider is unavailable.");
        }
        catch
        {
            return new NetworkDiagnosticProviderHealth(
                NetworkDiagnosticConnectionStates.ProviderError,
                "Palo Alto Networks provider request failed.");
        }
    }

    public async Task<VpnDiagnosticResult> RunVpnDiagnosticsAsync(
        VpnDiagnosticRequest request,
        CancellationToken ct = default)
    {
        if (request.TargetType != NetworkDiagnosticTargetType.User)
        {
            throw new PaloAltoGlobalProtectException(
                PaloAltoGlobalProtectError.UnsupportedTarget,
                "Palo Alto Networks GlobalProtect diagnostics support User targets only.");
        }

        if (string.IsNullOrWhiteSpace(request.Target))
        {
            throw new PaloAltoGlobalProtectException(
                PaloAltoGlobalProtectError.TargetNotFound,
                "Palo Alto Networks GlobalProtect user target is required.");
        }

        if (!IsConfigured())
        {
            throw new PaloAltoGlobalProtectException(
                PaloAltoGlobalProtectError.NotConfigured,
                "Palo Alto Networks credentials are not configured.");
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

            var entries = ParseEntries(body);

            var matching = entries
                .Where(entry =>
                    string.Equals(
                        entry.User,
                        request.Target,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matching.Count == 0)
            {
                throw new PaloAltoGlobalProtectException(
                    PaloAltoGlobalProtectError.TargetNotFound,
                    "Palo Alto Networks GlobalProtect user session was not found.");
            }

            var selected = SelectNewest(matching);

            var providerFields = new Dictionary<string, string?>(
                StringComparer.OrdinalIgnoreCase);

            AddField(
                providerFields,
                "vpnType",
                selected.VpnType);

            AddField(
                providerFields,
                "tunnelType",
                selected.TunnelType);

            AddField(
                providerFields,
                "virtualIp",
                selected.VirtualIp);

            AddField(
                providerFields,
                "publicIp",
                selected.PublicIp);

            AddField(
                providerFields,
                "loginTimeUtc",
                selected.LoginTimeUtc?.ToString("O"));

            AddField(
                providerFields,
                "clientVersion",
                selected.Client);

            AddField(
                providerFields,
                "computer",
                selected.Computer);

            AddField(
                providerFields,
                "domain",
                selected.Domain);

            AddField(
                providerFields,
                "gateway",
                selected.Gateway);

            return new VpnDiagnosticResult(
                Status: "Success",
                Provider: DisplayNameValue,
                ObservationType: NetworkDiagnosticObservationType.Session,
                ObservedAt: timeProvider.GetUtcNow(),
                User: selected.User,
                Device: selected.Computer,
                Ip: selected.VirtualIp ?? selected.PublicIp,
                ConnectionState: null,
                Latency: null,
                PacketLoss: null,
                ProviderDetails: providerFields.Count == 0
                    ? null
                    : new NetworkDiagnosticProviderDetails(
                        providerFields));
        }
        catch (PaloAltoGlobalProtectException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new PaloAltoGlobalProtectException(
                PaloAltoGlobalProtectError.Timeout,
                "Palo Alto Networks GlobalProtect request timed out.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw new PaloAltoGlobalProtectException(
                PaloAltoGlobalProtectError.Unavailable,
                "Palo Alto Networks GlobalProtect provider is unavailable.");
        }
        catch
        {
            throw new PaloAltoGlobalProtectException(
                PaloAltoGlobalProtectError.ProviderFailure,
                "Palo Alto Networks GlobalProtect provider request failed.");
        }
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        CancellationToken ct)
    {
        var requestUri = BuildRequestUri();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            requestUri);

        request.Headers.TryAddWithoutValidation(
            "X-PAN-KEY",
            _options.ApiKey);

        request.Headers.Accept.ParseAdd("application/xml");

        return await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
    }

    private Uri BuildRequestUri()
    {
        var baseUri = GetBaseUri();

        const string command =
            "<show><global-protect-gateway><current-user/></global-protect-gateway></show>";

        var builder = new UriBuilder(
            new Uri(baseUri, "api"));

        builder.Query =
            $"type=op&cmd={Uri.EscapeDataString(command)}";

        return builder.Uri;
    }

    private Uri GetBaseUri()
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new PaloAltoGlobalProtectException(
                PaloAltoGlobalProtectError.NotConfigured,
                "Palo Alto Networks GlobalProtect BaseUrl is not configured.");
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
            throw new PaloAltoGlobalProtectException(
                PaloAltoGlobalProtectError.ProviderFailure,
                "Palo Alto Networks GlobalProtect BaseUrl must use HTTPS.");
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
        !string.IsNullOrWhiteSpace(_options.ApiKey);

    private static NetworkDiagnosticProviderHealth NotConfigured() =>
        new(
            NetworkDiagnosticConnectionStates.NotConfigured,
            "Palo Alto Networks credentials are not configured.");

    private static NetworkDiagnosticProviderHealth MapHealthFailure(
        HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized =>
                new(
                    NetworkDiagnosticConnectionStates.AuthenticationFailed,
                    "Palo Alto Networks authentication failed."),

            HttpStatusCode.Forbidden =>
                new(
                    NetworkDiagnosticConnectionStates.PermissionDenied,
                    "Palo Alto Networks permission was denied."),

            HttpStatusCode.RequestTimeout =>
                new(
                    NetworkDiagnosticConnectionStates.Timeout,
                    "Palo Alto Networks provider request timed out."),

            HttpStatusCode.TooManyRequests =>
                new(
                    NetworkDiagnosticConnectionStates.ProviderError,
                    "Palo Alto Networks provider request was throttled."),

            >= HttpStatusCode.InternalServerError =>
                new(
                    NetworkDiagnosticConnectionStates.Unavailable,
                    "Palo Alto Networks provider is unavailable."),

            _ =>
                new(
                    NetworkDiagnosticConnectionStates.ProviderError,
                    "Palo Alto Networks provider request failed.")
        };

    private static PaloAltoGlobalProtectException MapException(
        HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized =>
                new(
                    PaloAltoGlobalProtectError.AuthenticationFailed,
                    "Palo Alto Networks authentication failed."),

            HttpStatusCode.Forbidden =>
                new(
                    PaloAltoGlobalProtectError.PermissionDenied,
                    "Palo Alto Networks permission was denied."),

            HttpStatusCode.NotFound =>
                new(
                    PaloAltoGlobalProtectError.TargetNotFound,
                    "Palo Alto Networks GlobalProtect resource was not found."),

            HttpStatusCode.RequestTimeout =>
                new(
                    PaloAltoGlobalProtectError.Timeout,
                    "Palo Alto Networks GlobalProtect request timed out."),

            HttpStatusCode.TooManyRequests =>
                new(
                    PaloAltoGlobalProtectError.Throttled,
                    "Palo Alto Networks GlobalProtect request was throttled."),

            >= HttpStatusCode.InternalServerError =>
                new(
                    PaloAltoGlobalProtectError.Unavailable,
                    "Palo Alto Networks GlobalProtect provider is unavailable."),

            _ =>
                new(
                    PaloAltoGlobalProtectError.ProviderFailure,
                    "Palo Alto Networks GlobalProtect provider request failed.")
        };

    private static bool TryParseSuccessfulResponse(
        string body,
        out List<GlobalProtectEntry> entries)
    {
        try
        {
            entries = ParseEntries(body);
            return true;
        }
        catch
        {
            entries = [];
            return false;
        }
    }

    private static List<GlobalProtectEntry> ParseEntries(
        string body)
    {
        XDocument document;

        try
        {
            document = XDocument.Parse(body);
        }
        catch
        {
            throw new PaloAltoGlobalProtectException(
                PaloAltoGlobalProtectError.ProviderFailure,
                "Palo Alto Networks GlobalProtect provider request failed.");
        }

        var root = document.Root;

        if (root is null ||
            !string.Equals(
                root.Attribute("status")?.Value,
                "success",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new PaloAltoGlobalProtectException(
                PaloAltoGlobalProtectError.ProviderFailure,
                "Palo Alto Networks GlobalProtect provider request failed.");
        }

        var result = root.Element("result");

        if (result is null)
        {
            throw new PaloAltoGlobalProtectException(
                PaloAltoGlobalProtectError.ProviderFailure,
                "Palo Alto Networks GlobalProtect provider response was malformed.");
        }

        return result
            .Elements("entry")
            .Select(ParseEntry)
            .ToList();
    }

    private static GlobalProtectEntry ParseEntry(
        XElement entry)
    {
        return new GlobalProtectEntry(
            User: Value(entry, "username"),
            Computer: Value(entry, "computer"),
            Client: Value(entry, "client"),
            VpnType: Value(entry, "vpn-type"),
            VirtualIp: Value(entry, "virtual-ip"),
            PublicIp: Value(entry, "public-ip"),
            TunnelType: Value(entry, "tunnel-type"),
            LoginTimeUtc: ParseUnixTimestamp(
                Value(entry, "login-time-utc")),
            Domain: Value(entry, "domain"),
            Gateway: Value(entry, "gateway"));
    }

    private static string? Value(
        XElement entry,
        string name)
    {
        var value = entry
            .Element(name)?
            .Value?
            .Trim();

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }

    private static DateTimeOffset? ParseUnixTimestamp(
        string? value)
    {
        if (!long.TryParse(
                value,
                out var seconds))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(
                seconds);
        }
        catch
        {
            return null;
        }
    }

    private static GlobalProtectEntry SelectNewest(
        List<GlobalProtectEntry> entries)
    {
        if (entries.Count == 1)
        {
            return entries[0];
        }

        if (entries.Any(
                entry => entry.LoginTimeUtc is null))
        {
            throw new PaloAltoGlobalProtectException(
                PaloAltoGlobalProtectError.ProviderFailure,
                "Palo Alto Networks returned multiple sessions without unambiguous login timestamps.");
        }

        var ordered = entries
            .OrderByDescending(
                entry => entry.LoginTimeUtc)
            .ToList();

        if (ordered.Count > 1 &&
            ordered[0].LoginTimeUtc ==
            ordered[1].LoginTimeUtc)
        {
            throw new PaloAltoGlobalProtectException(
                PaloAltoGlobalProtectError.ProviderFailure,
                "Palo Alto Networks returned multiple sessions with the same login timestamp.");
        }

        return ordered[0];
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

    private sealed record GlobalProtectEntry(
        string? User,
        string? Computer,
        string? Client,
        string? VpnType,
        string? VirtualIp,
        string? PublicIp,
        string? TunnelType,
        DateTimeOffset? LoginTimeUtc,
        string? Domain,
        string? Gateway);
}

public enum PaloAltoGlobalProtectError
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

public sealed class PaloAltoGlobalProtectException(
    PaloAltoGlobalProtectError category,
    string message) : Exception(message), IAIOpsSafeTelemetryFailure
{
    public PaloAltoGlobalProtectError Category { get; } =
        category;

    public string TelemetryErrorType => Category.ToString();

    public string HealthState =>
        Category switch
        {
            PaloAltoGlobalProtectError.NotConfigured =>
                NetworkDiagnosticConnectionStates.NotConfigured,

            PaloAltoGlobalProtectError.AuthenticationFailed =>
                NetworkDiagnosticConnectionStates.AuthenticationFailed,

            PaloAltoGlobalProtectError.PermissionDenied =>
                NetworkDiagnosticConnectionStates.PermissionDenied,

            PaloAltoGlobalProtectError.Timeout =>
                NetworkDiagnosticConnectionStates.Timeout,

            PaloAltoGlobalProtectError.Unavailable =>
                NetworkDiagnosticConnectionStates.Unavailable,

            _ =>
                NetworkDiagnosticConnectionStates.ProviderError
        };
}
