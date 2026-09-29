using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Integrations;
using Microsoft.Extensions.Options;

namespace AIOps.Infrastructure.Integrations;

/// <summary>A sanitized, stable ServiceNow provider failure.</summary>
public sealed class ServiceNowConnectorException(string category)
    : InvalidOperationException(ServiceNowConnectorException.GetMessage(category))
{
    public string Category { get; } = category;

    private static string GetMessage(string category) => category switch
    {
        "ServiceNowAuthenticationFailed" => "ServiceNowAuthenticationFailed: ServiceNow authentication failed.",
        "ServiceNowPermissionDenied" => "ServiceNowPermissionDenied: ServiceNow permission was denied.",
        "ServiceNowTicketNotFound" => "ServiceNowTicketNotFound: The ServiceNow incident was not found.",
        "ServiceNowInvalidState" => "ServiceNowInvalidState: The requested incident state is not supported.",
        "ServiceNowThrottled" => "ServiceNowThrottled: ServiceNow is throttling requests.",
        "ServiceNowTimeout" => "ServiceNowTimeout: The ServiceNow request timed out.",
        "ServiceNowNotConfigured" => "ServiceNowNotConfigured: ServiceNow credentials are not configured.",
        _ => "ServiceNowProviderFailure: The ServiceNow request failed."
    };
}

/// <summary>
/// ServiceNow OAuth client-credentials and incident Table API adapter. Tokens and
/// credentials stay in memory/server configuration and are never included in results.
/// </summary>
public sealed class ServiceNowItsmConnector : IItsmProviderConnector
{
    private const string ProviderFailure = "ServiceNowProviderFailure";
    private const string AuthenticationFailed = "ServiceNowAuthenticationFailed";
    private const string TicketNotFound = "ServiceNowTicketNotFound";
    private const string InvalidState = "ServiceNowInvalidState";
    private const string Throttled = "ServiceNowThrottled";
    private const string TimedOut = "ServiceNowTimeout";
    private const string NotConfigured = "ServiceNowNotConfigured";

    private static readonly IReadOnlyDictionary<string, string> ServiceNowStates =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["New"] = "1",
            ["In Progress"] = "2",
            ["On Hold"] = "3",
            ["Resolved"] = "6",
            ["Closed"] = "7",
            ["Canceled"] = "8"
        };

    private readonly HttpClient _httpClient;
    private readonly ServiceNowItsmOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _refreshTokenAfter;

    public ServiceNowItsmConnector(
        HttpClient httpClient,
        IOptions<ItsmIntegrationOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _httpClient = httpClient;
        _options = options.Value.ServiceNow ?? new ServiceNowItsmOptions();
        _timeProvider = timeProvider;
    }

    public string ProviderName => "ServiceNow";
    public string DisplayName => "ServiceNow";
    public string Mode => "Real";
    public bool IsProduction => true;

    public async Task<string> GetConnectionStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var configuration = GetConfiguration();
            if (configuration is null)
            {
                return "NotConfigured";
            }

            var token = await GetAccessTokenAsync(configuration, ct);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(configuration.BaseUri,
                    "api/now/table/incident?sysparm_limit=1&sysparm_fields=sys_id"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await SendAsync(request, ct);
            EnsureSuccess(response, ticketLookup: false);
            return "Connected";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return "Unavailable";
        }
    }

    public async Task<string> UpdateTicketAsync(
        string externalRef,
        string note,
        string? state = null,
        CancellationToken ct = default)
    {
        var configuration = GetConfiguration()
            ?? throw new ServiceNowConnectorException(NotConfigured);

        string? mappedState = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            if (!ServiceNowStates.TryGetValue(state.Trim(), out mappedState))
            {
                throw new ServiceNowConnectorException(InvalidState);
            }
        }

        if (string.IsNullOrWhiteSpace(externalRef))
        {
            throw new ServiceNowConnectorException(TicketNotFound);
        }

        var token = await GetAccessTokenAsync(configuration, ct);
        var incident = await FindIncidentAsync(configuration, token, externalRef, ct);

        var fields = new Dictionary<string, string>
        {
            ["work_notes"] = note
        };
        if (mappedState is not null)
        {
            fields["state"] = mappedState;
        }

        using var updateRequest = new HttpRequestMessage(
            HttpMethod.Patch,
            new Uri(configuration.BaseUri,
                $"api/now/table/incident/{Uri.EscapeDataString(incident.SysId)}"));
        updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        updateRequest.Content = new StringContent(
            JsonSerializer.Serialize(fields),
            Encoding.UTF8,
            "application/json");

        using var response = await SendAsync(updateRequest, ct);
        EnsureSuccess(response, ticketLookup: true);

        // Return only a small, allow-listed summary. The HTTP status is the source
        // of truth for whether the update succeeded; the response body is never echoed.
        return JsonSerializer.Serialize(new
        {
            provider = ProviderName,
            ticketNumber = incident.Number,
            updated = true,
            httpStatus = (int)response.StatusCode,
            state = mappedState
        });
    }

    private async Task<(string SysId, string Number)> FindIncidentAsync(
        ServiceNowConfiguration configuration,
        string token,
        string externalRef,
        CancellationToken ct)
    {
        var query = Uri.EscapeDataString($"number={externalRef.Trim()}");
        var uri = new Uri(configuration.BaseUri,
            $"api/now/table/incident?sysparm_query={query}&sysparm_limit=1&sysparm_fields=sys_id,number");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(request, ct);
        EnsureSuccess(response, ticketLookup: true);

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!document.RootElement.TryGetProperty("result", out var results) ||
                results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
            {
                throw new ServiceNowConnectorException(TicketNotFound);
            }

            var result = results[0];
            var sysId = result.TryGetProperty("sys_id", out var idElement)
                ? idElement.GetString()
                : null;
            var number = result.TryGetProperty("number", out var numberElement)
                ? numberElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(sysId) || string.IsNullOrWhiteSpace(number))
            {
                throw new ServiceNowConnectorException(ProviderFailure);
            }

            return (sysId, number);
        }
        catch (ServiceNowConnectorException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new ServiceNowConnectorException(TimedOut);
        }
        catch
        {
            throw new ServiceNowConnectorException(ProviderFailure);
        }
    }

    private async Task<string> GetAccessTokenAsync(
        ServiceNowConfiguration configuration,
        CancellationToken ct)
    {
        if (_accessToken is not null && _timeProvider.GetUtcNow() < _refreshTokenAfter)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && _timeProvider.GetUtcNow() < _refreshTokenAfter)
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(configuration.BaseUri, "oauth_token.do"))
            {
                Content = new FormUrlEncodedContent(
                [
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("client_id", configuration.ClientId),
                    new KeyValuePair<string, string>("client_secret", configuration.ClientSecret)
                ])
            };

            using var response = await SendAsync(request, ct);
            EnsureSuccess(response, ticketLookup: false);

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = document.RootElement;
                var accessToken = root.TryGetProperty("access_token", out var tokenElement)
                    ? tokenElement.GetString()
                    : null;
                var expiresIn = root.TryGetProperty("expires_in", out var expiresElement) &&
                    expiresElement.TryGetInt32(out var seconds)
                    ? seconds
                    : 0;

                if (string.IsNullOrWhiteSpace(accessToken) || expiresIn <= 0)
                {
                    throw new ServiceNowConnectorException(AuthenticationFailed);
                }

                var lifetime = TimeSpan.FromSeconds(expiresIn);
                var refreshSkew = TimeSpan.FromSeconds(
                    Math.Max(1, Math.Min(60, expiresIn / 10)));
                _accessToken = accessToken;
                _refreshTokenAfter = _timeProvider.GetUtcNow() + lifetime - refreshSkew;
                return accessToken;
            }
            catch (ServiceNowConnectorException)
            {
                throw;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new ServiceNowConnectorException(TimedOut);
            }
            catch
            {
                throw new ServiceNowConnectorException(AuthenticationFailed);
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        try
        {
            return await _httpClient.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new ServiceNowConnectorException(TimedOut);
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode == HttpStatusCode.RequestTimeout || HasTimeoutInnerException(ex))
        {
            throw new ServiceNowConnectorException(TimedOut);
        }
        catch
        {
            throw new ServiceNowConnectorException(ProviderFailure);
        }
    }

    private static bool HasTimeoutInnerException(Exception exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException or TaskCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureSuccess(HttpResponseMessage response, bool ticketLookup)
    {
        if ((int)response.StatusCode is >= 200 and < 300)
        {
            return;
        }

        var category = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => AuthenticationFailed,
            HttpStatusCode.Forbidden => "ServiceNowPermissionDenied",
            HttpStatusCode.NotFound when ticketLookup => TicketNotFound,
            HttpStatusCode.TooManyRequests => Throttled,
            HttpStatusCode.RequestTimeout => TimedOut,
            _ => ProviderFailure
        };
        throw new ServiceNowConnectorException(category);
    }

    private ServiceNowConfiguration? GetConfiguration()
    {
        var baseUrl = _options.BaseUrl?.Trim();
        var clientId = _options.ClientId?.Trim();
        var clientSecret = _options.ClientSecret;

        if (string.IsNullOrWhiteSpace(baseUrl) &&
            string.IsNullOrWhiteSpace(clientId) &&
            string.IsNullOrWhiteSpace(clientSecret))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "Integrations:Itsm:ServiceNow:BaseUrl is required when ServiceNow is configured.");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedBaseUrl) ||
            parsedBaseUrl.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(parsedBaseUrl.Host) ||
            !string.IsNullOrEmpty(parsedBaseUrl.UserInfo) ||
            !string.IsNullOrEmpty(parsedBaseUrl.Query) ||
            !string.IsNullOrEmpty(parsedBaseUrl.Fragment))
        {
            throw new InvalidOperationException(
                "Integrations:Itsm:ServiceNow:BaseUrl must be an absolute HTTPS URL without credentials, query, or fragment.");
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(
                "Integrations:Itsm:ServiceNow:ClientId is required when ServiceNow is configured.");
        }

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "Integrations:Itsm:ServiceNow:ClientSecret is required when ServiceNow is configured.");
        }

        var normalizedBaseUrl = new Uri(
            parsedBaseUrl.AbsoluteUri.TrimEnd('/') + "/",
            UriKind.Absolute);
        return new ServiceNowConfiguration(normalizedBaseUrl, clientId, clientSecret);
    }

    private sealed record ServiceNowConfiguration(
        Uri BaseUri,
        string ClientId,
        string ClientSecret);
}
