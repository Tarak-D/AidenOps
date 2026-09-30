using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Integrations;
using Microsoft.Extensions.Options;

namespace AIOps.Infrastructure.Integrations;

/// <summary>
/// Cisco Secure Access VPN session reader. It uses only the documented user-connections
/// endpoint and never performs an active network test.
/// </summary>
public sealed class CiscoSecureAccessVpnProvider : IVpnDiagnosticsProvider
{
    private const string ProviderNameValue = "Cisco";
    private const string DisplayNameValue = "Cisco Secure Access";
    private const string TokenPath = "/auth/v2/token";
    private const string ConnectionsPath = "/admin/v2/vpn/userConnections";
    private const int PageSize = 1000;
    private const int MaximumPages = 10;
    private static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromSeconds(60);

    private readonly HttpClient _httpClient;
    private readonly CiscoSecureAccessOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public CiscoSecureAccessVpnProvider(
        HttpClient httpClient,
        IOptions<CiscoSecureAccessOptions> options,
        TimeProvider timeProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public string Category => NetworkDiagnosticCategories.Vpn;
    public string ProviderName => ProviderNameValue;
    public string DisplayName => DisplayNameValue;
    public string Mode => "Real";
    public bool IsProduction => true;

    public async Task<NetworkDiagnosticProviderHealth> GetConnectionStatusAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsConfigured())
        {
            return Health(CiscoSecureAccessError.NotConfigured);
        }

        try
        {
            ValidateBaseUrl();
            var token = await GetAccessTokenAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ConnectionsPath}?limit=1");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await SendAsync(request, ct);
            EnsureSuccess(response);
            await ValidateStatusResponseAsync(response, ct);
            return new NetworkDiagnosticProviderHealth(
                NetworkDiagnosticConnectionStates.Connected,
                "Cisco Secure Access VPN API is reachable with the required read permission.");
        }
        catch (CiscoSecureAccessException exception)
        {
            return Health(exception.Category);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Health(CiscoSecureAccessError.Timeout);
        }
        catch (HttpRequestException)
        {
            return Health(CiscoSecureAccessError.Unavailable);
        }
        catch (JsonException)
        {
            return Health(CiscoSecureAccessError.ProviderFailure);
        }
        catch (Exception)
        {
            return Health(CiscoSecureAccessError.ProviderFailure);
        }
    }

    public async Task<VpnDiagnosticResult> RunVpnDiagnosticsAsync(
        VpnDiagnosticRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        if (request.TargetType != NetworkDiagnosticTargetType.User)
        {
            throw Error(CiscoSecureAccessError.UnsupportedTarget);
        }

        if (!IsConfigured())
        {
            throw Error(CiscoSecureAccessError.NotConfigured);
        }

        IReadOnlyList<CiscoVpnSession> sessions;
        try
        {
            ValidateBaseUrl();
            var token = await GetAccessTokenAsync(ct);
            sessions = await ReadSessionsAsync(request.Target, token, ct);
        }
        catch (CiscoSecureAccessException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw Error(CiscoSecureAccessError.Timeout);
        }
        catch (HttpRequestException)
        {
            throw Error(CiscoSecureAccessError.Unavailable);
        }
        catch (JsonException)
        {
            throw Error(CiscoSecureAccessError.ProviderFailure);
        }
        catch (Exception)
        {
            throw Error(CiscoSecureAccessError.ProviderFailure);
        }

        if (sessions.Count == 0)
        {
            throw Error(CiscoSecureAccessError.TargetNotFound);
        }

        var selected = SelectMostRecentSession(sessions);
        var observedAt = selected.ObservedAt;
        var details = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["sessionId"] = selected.SessionId,
            ["profileName"] = selected.ProfileName,
            ["loginTime"] = selected.LoginTimeText,
            ["assignedIpv6"] = selected.AssignedIpv6,
            ["publicIp"] = selected.PublicIp
        };

        return new VpnDiagnosticResult(
            Status: "Success",
            Provider: DisplayName,
            ObservationType: NetworkDiagnosticObservationType.Session,
            ObservedAt: observedAt,
            User: selected.Username,
            Device: selected.DeviceName,
            Ip: selected.AssignedIp,
            ConnectionState: null,
            Latency: null,
            PacketLoss: null,
            ProviderDetails: new NetworkDiagnosticProviderDetails(details));
    }

    private bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(_options.ClientId) &&
        !string.IsNullOrWhiteSpace(_options.ClientSecret);

    private void ValidateBaseUrl()
    {
        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(baseUri.Host, "api.sse.cisco.com", StringComparison.OrdinalIgnoreCase) ||
            !baseUri.IsDefaultPort ||
            baseUri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw Error(CiscoSecureAccessError.ProviderFailure);
        }

        _httpClient.BaseAddress ??= baseUri;

    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken is not null && _tokenExpiresAt - _timeProvider.GetUtcNow() > TokenRefreshSkew)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && _tokenExpiresAt - _timeProvider.GetUtcNow() > TokenRefreshSkew)
            {
                return _accessToken;
            }

            var credentialBytes = System.Text.Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}");
            using var request = new HttpRequestMessage(HttpMethod.Post, TokenPath)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials"
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(credentialBytes));
            using var response = await SendAsync(request, ct);
            EnsureSuccess(response);

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw Error(CiscoSecureAccessError.AuthenticationFailed);
                }

                var accessToken = root.TryGetProperty("access_token", out var tokenElement) &&
                                  tokenElement.ValueKind == JsonValueKind.String
                    ? tokenElement.GetString()
                    : null;
                var expiresIn = root.TryGetProperty("expires_in", out var expiresElement) &&
                                expiresElement.TryGetInt32(out var seconds)
                    ? seconds
                    : 0;
                if (string.IsNullOrWhiteSpace(accessToken) ||
                    accessToken.Any(character => character < 0x21 || character > 0x7e) ||
                    expiresIn <= 0)
                {
                    throw Error(CiscoSecureAccessError.AuthenticationFailed);
                }

                _accessToken = accessToken;
                _tokenExpiresAt = _timeProvider.GetUtcNow().AddSeconds(expiresIn);
                return accessToken;
            }
            catch (JsonException)
            {
                throw Error(CiscoSecureAccessError.AuthenticationFailed);
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<IReadOnlyList<CiscoVpnSession>> ReadSessionsAsync(
        string username,
        string token,
        CancellationToken ct)
    {
        var sessions = new List<CiscoVpnSession>();
        var offset = 0;
        int? total = null;
        var page = 0;
        do
        {
            if (page++ >= MaximumPages)
            {
                throw Error(CiscoSecureAccessError.ProviderFailure);
            }

            var query = $"?usernames={Uri.EscapeDataString(username)}&limit={PageSize}&offset={offset}";
            using var request = new HttpRequestMessage(HttpMethod.Get, ConnectionsPath + query);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await SendAsync(request, ct);
            EnsureSuccess(response);

            JsonDocument document;
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            }
            catch (JsonException)
            {
                throw Error(CiscoSecureAccessError.ProviderFailure);
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array ||
                    !root.TryGetProperty("total", out var totalElement) || !totalElement.TryGetInt32(out var pageTotal) || pageTotal < 0)
                {
                    throw Error(CiscoSecureAccessError.ProviderFailure);
                }

                total = pageTotal;
                var observedAt = response.Headers.Date ?? _timeProvider.GetUtcNow();
                foreach (var item in data.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        throw Error(CiscoSecureAccessError.ProviderFailure);
                    }

                    var session = ParseSession(item, observedAt);
                    if (string.Equals(session.Username, username, StringComparison.OrdinalIgnoreCase))
                    {
                        sessions.Add(session);
                    }
                }

                var returned = data.GetArrayLength();
                if (returned == 0 && offset < total)
                {
                    throw Error(CiscoSecureAccessError.ProviderFailure);
                }

                offset = checked(offset + returned);
                if (returned == 0)
                {
                    break;
                }
            }
        } while (total.HasValue && offset < total.Value);

        return sessions;
    }

    private static async Task ValidateStatusResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array ||
                !root.TryGetProperty("total", out var total) || !total.TryGetInt32(out var count) || count < 0)
            {
                throw Error(CiscoSecureAccessError.ProviderFailure);
            }
        }
        catch (JsonException)
        {
            throw Error(CiscoSecureAccessError.ProviderFailure);
        }
    }

    private static CiscoVpnSession ParseSession(JsonElement item, DateTimeOffset observedAt)
    {
        var username = ReadString(item, "username");
        if (string.IsNullOrWhiteSpace(username))
        {
            throw Error(CiscoSecureAccessError.ProviderFailure);
        }

        var loginTimeText = ReadString(item, "loginTime");
        DateTimeOffset? loginTime = null;
        if (loginTimeText is not null)
        {
            if (!DateTimeOffset.TryParse(
                    loginTimeText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                throw Error(CiscoSecureAccessError.ProviderFailure);
            }

            loginTime = parsed;
        }

        return new CiscoVpnSession(
            username,
            ReadString(item, "deviceName"),
            ReadString(item, "assignedIp"),
            ReadString(item, "assignedIpv6"),
            ReadString(item, "publicIp"),
            ReadString(item, "sessionId"),
            loginTimeText,
            loginTime,
            ReadString(item, "profileName"),
            observedAt);
    }

    private static string? ReadString(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw Error(CiscoSecureAccessError.ProviderFailure);
        }

        return property.GetString();
    }

    private static CiscoVpnSession SelectMostRecentSession(IReadOnlyList<CiscoVpnSession> sessions)
    {
        var withLoginTime = sessions.Where(session => session.LoginTime.HasValue).ToArray();
        if (withLoginTime.Length == 0)
        {
            if (sessions.Count == 1)
            {
                return sessions[0];
            }

            // The API's loginTime is the only documented timestamp. With no timestamps,
            // selecting one of several sessions as "most recent" would be arbitrary.
            throw Error(CiscoSecureAccessError.ProviderFailure);
        }

        if (withLoginTime.Length != sessions.Count)
        {
            // A session without the documented creation timestamp could be newer than
            // the others; fail rather than silently choose a possibly stale session.
            throw Error(CiscoSecureAccessError.ProviderFailure);
        }

        var newestTime = withLoginTime.Max(session => session.LoginTime);
        var newest = withLoginTime.Where(session => session.LoginTime == newestTime).ToArray();
        return newest.Length == 1
            ? newest[0]
            : throw Error(CiscoSecureAccessError.ProviderFailure);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            // Buffering the small, bounded response keeps HttpClient.Timeout active
            // through content download as well as response-header receipt.
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw Error(CiscoSecureAccessError.Timeout);
        }
        catch (HttpRequestException)
        {
            throw Error(CiscoSecureAccessError.Unavailable);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var category = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => CiscoSecureAccessError.AuthenticationFailed,
            HttpStatusCode.Forbidden => CiscoSecureAccessError.PermissionDenied,
            HttpStatusCode.TooManyRequests => CiscoSecureAccessError.Throttled,
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => CiscoSecureAccessError.Timeout,
            HttpStatusCode.NotFound => CiscoSecureAccessError.ProviderFailure,
            _ when (int)response.StatusCode >= 500 => CiscoSecureAccessError.Unavailable,
            _ => CiscoSecureAccessError.ProviderFailure
        };
        throw Error(category);
    }

    private static NetworkDiagnosticProviderHealth Health(string category) => category switch
    {
        CiscoSecureAccessError.NotConfigured => new(NetworkDiagnosticConnectionStates.NotConfigured, "Cisco Secure Access credentials are not configured."),
        CiscoSecureAccessError.AuthenticationFailed => new(NetworkDiagnosticConnectionStates.AuthenticationFailed, "Cisco Secure Access authentication failed."),
        CiscoSecureAccessError.PermissionDenied => new(NetworkDiagnosticConnectionStates.PermissionDenied, "Cisco Secure Access denied the required VPN read permission."),
        CiscoSecureAccessError.Timeout => new(NetworkDiagnosticConnectionStates.Timeout, "Cisco Secure Access request timed out."),
        CiscoSecureAccessError.Unavailable => new(NetworkDiagnosticConnectionStates.Unavailable, "Cisco Secure Access is unavailable."),
        _ => new(NetworkDiagnosticConnectionStates.ProviderError, "Cisco Secure Access provider request failed.")
    };

    private static CiscoSecureAccessException Error(string category) => new(category);

    private sealed record CiscoVpnSession(
        string Username,
        string? DeviceName,
        string? AssignedIp,
        string? AssignedIpv6,
        string? PublicIp,
        string? SessionId,
        string? LoginTimeText,
        DateTimeOffset? LoginTime,
        string? ProfileName,
        DateTimeOffset ObservedAt);
}

public static class CiscoSecureAccessError
{
    public const string NotConfigured = "CiscoSecureAccessNotConfigured";
    public const string AuthenticationFailed = "CiscoSecureAccessAuthenticationFailed";
    public const string PermissionDenied = "CiscoSecureAccessPermissionDenied";
    public const string TargetNotFound = "CiscoSecureAccessTargetNotFound";
    public const string UnsupportedTarget = "CiscoSecureAccessUnsupportedTarget";
    public const string Throttled = "CiscoSecureAccessThrottled";
    public const string Timeout = "CiscoSecureAccessTimeout";
    public const string Unavailable = "CiscoSecureAccessUnavailable";
    public const string ProviderFailure = "CiscoSecureAccessProviderFailure";
}

public sealed class CiscoSecureAccessException : InvalidOperationException
{
    private static readonly IReadOnlyDictionary<string, string> SafeMessages =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CiscoSecureAccessError.NotConfigured] = "Cisco Secure Access credentials are not configured.",
            [CiscoSecureAccessError.AuthenticationFailed] = "Cisco Secure Access authentication failed.",
            [CiscoSecureAccessError.PermissionDenied] = "Cisco Secure Access denied the required VPN read permission.",
            [CiscoSecureAccessError.TargetNotFound] = "No Cisco Secure Access VPN session was found for the requested user.",
            [CiscoSecureAccessError.UnsupportedTarget] = "Cisco Secure Access supports VPN diagnostics only for user targets.",
            [CiscoSecureAccessError.Throttled] = "Cisco Secure Access rate limit was reached.",
            [CiscoSecureAccessError.Timeout] = "Cisco Secure Access request timed out.",
            [CiscoSecureAccessError.Unavailable] = "Cisco Secure Access is unavailable.",
            [CiscoSecureAccessError.ProviderFailure] = "Cisco Secure Access provider request failed."
        };

    public CiscoSecureAccessException(string category)
        : base($"{category}: {SafeMessages.GetValueOrDefault(category, SafeMessages[CiscoSecureAccessError.ProviderFailure])}")
    {
        Category = SafeMessages.ContainsKey(category) ? category : CiscoSecureAccessError.ProviderFailure;
    }

    public string Category { get; }
}
