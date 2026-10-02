using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Diagnostics;
using AIOps.Abstractions.Integrations;
using Microsoft.Extensions.Options;

namespace AIOps.Infrastructure.Integrations;

/// <summary>A sanitized, stable Zendesk provider failure.</summary>
public sealed class ZendeskConnectorException(string category)
    : InvalidOperationException(ZendeskConnectorException.GetMessage(category)), IAIOpsSafeTelemetryFailure
{
    public string Category { get; } = category;
    public string TelemetryErrorType => Category;

    private static string GetMessage(string category) => category switch
    {
        "ZendeskNotConfigured" => "ZendeskNotConfigured: Zendesk is not configured.",
        "ZendeskAuthenticationFailed" => "ZendeskAuthenticationFailed: Zendesk authentication failed.",
        "ZendeskPermissionDenied" => "ZendeskPermissionDenied: Zendesk permission was denied.",
        "ZendeskTicketNotFound" => "ZendeskTicketNotFound: The Zendesk ticket was not found.",
        "ZendeskInvalidState" => "ZendeskInvalidState: The requested ticket status is not supported.",
        "ZendeskThrottled" => "ZendeskThrottled: Zendesk is throttling requests.",
        "ZendeskTimeout" => "ZendeskTimeout: The Zendesk request timed out.",
        _ => "ZendeskProviderFailure: The Zendesk request failed."
    };
}

/// <summary>
/// Zendesk Support API adapter. Notes are deliberately added as private comments;
/// API credentials remain server-side and are never returned or logged.
/// </summary>
public sealed class ZendeskItsmConnector : IItsmProviderConnector
{
    private const string NotConfigured = "ZendeskNotConfigured";
    private const string AuthenticationFailed = "ZendeskAuthenticationFailed";
    private const string PermissionDenied = "ZendeskPermissionDenied";
    private const string TicketNotFound = "ZendeskTicketNotFound";
    private const string InvalidState = "ZendeskInvalidState";
    private const string Throttled = "ZendeskThrottled";
    private const string TimedOut = "ZendeskTimeout";
    private const string ProviderFailure = "ZendeskProviderFailure";

    private static readonly IReadOnlyDictionary<string, string> StatusMappings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Open"] = "open",
            ["InProgress"] = "open",
            ["In Progress"] = "open",
            ["Resolved"] = "solved",
            ["Closed"] = "closed"
        };

    private readonly HttpClient _httpClient;
    private readonly ZendeskItsmOptions _options;

    public ZendeskItsmConnector(
        HttpClient httpClient,
        IOptions<ItsmIntegrationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;
        _options = options.Value.Zendesk ?? new ZendeskItsmOptions();
    }

    public string ProviderName => "Zendesk";
    public string DisplayName => "Zendesk";
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

            using var request = CreateRequest(
                HttpMethod.Get,
                configuration,
                "api/v2/users/me.json");
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
            ?? throw new ZendeskConnectorException(NotConfigured);
        var ticketId = NormalizeTicketId(externalRef);
        string? mappedStatus = null;
        if (!string.IsNullOrWhiteSpace(state) &&
            !StatusMappings.TryGetValue(state.Trim(), out mappedStatus))
        {
            throw new ZendeskConnectorException(InvalidState);
        }

        await LookupTicketAsync(configuration, ticketId, ct);

        using var request = CreateRequest(
            HttpMethod.Put,
            configuration,
            $"api/v2/tickets/{ticketId}.json");
        var ticketPayload = new Dictionary<string, object>
        {
            ["comment"] = new
            {
                body = note,
                // ITSM notes are internal by default; never rely on Zendesk's default visibility.
                @public = false
            }
        };
        if (mappedStatus is not null)
        {
            ticketPayload["status"] = mappedStatus;
        }
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { ticket = ticketPayload }),
            Encoding.UTF8,
            "application/json");

        using var response = await SendAsync(request, ct);
        EnsureSuccess(response, ticketLookup: true);

        return JsonSerializer.Serialize(new
        {
            provider = ProviderName,
            ticketId,
            updated = true,
            commentVisibility = "private",
            status = mappedStatus
        });
    }

    private async Task LookupTicketAsync(
        ZendeskConfiguration configuration,
        string ticketId,
        CancellationToken ct)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            configuration,
            $"api/v2/tickets/{ticketId}.json");
        using var response = await SendAsync(request, ct);
        EnsureSuccess(response, ticketLookup: true);

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!document.RootElement.TryGetProperty("ticket", out var ticket) ||
                !ticket.TryGetProperty("id", out var idElement))
            {
                throw new ZendeskConnectorException(ProviderFailure);
            }

            var returnedId = idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString()
                : idElement.GetRawText();
            if (returnedId != ticketId)
            {
                throw new ZendeskConnectorException(TicketNotFound);
            }
        }
        catch (ZendeskConnectorException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new ZendeskConnectorException(TimedOut);
        }
        catch
        {
            throw new ZendeskConnectorException(ProviderFailure);
        }
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        ZendeskConfiguration configuration,
        string relativePath)
    {
        var request = new HttpRequestMessage(
            method,
            new Uri(configuration.BaseUri, relativePath));
        var username = $"{configuration.Email}/token";
        var basicCredentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{username}:{configuration.ApiToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicCredentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
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
            throw new ZendeskConnectorException(TimedOut);
        }
        catch (HttpRequestException exception) when (HasTimeoutInnerException(exception))
        {
            throw new ZendeskConnectorException(TimedOut);
        }
        catch
        {
            throw new ZendeskConnectorException(ProviderFailure);
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
            HttpStatusCode.Forbidden => PermissionDenied,
            HttpStatusCode.NotFound when ticketLookup => TicketNotFound,
            HttpStatusCode.TooManyRequests => Throttled,
            HttpStatusCode.RequestTimeout => TimedOut,
            _ => ProviderFailure
        };
        throw new ZendeskConnectorException(category);
    }

    private ZendeskConfiguration? GetConfiguration()
    {
        var baseUrl = _options.BaseUrl?.Trim();
        var email = _options.Email?.Trim();
        var apiToken = _options.ApiToken;

        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(apiToken))
        {
            return null;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedBaseUrl) ||
            parsedBaseUrl.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(parsedBaseUrl.Host) ||
            !string.IsNullOrEmpty(parsedBaseUrl.UserInfo) ||
            !string.IsNullOrEmpty(parsedBaseUrl.Query) ||
            !string.IsNullOrEmpty(parsedBaseUrl.Fragment))
        {
            throw new ZendeskConnectorException(NotConfigured);
        }

        var normalizedBaseUrl = new Uri(
            parsedBaseUrl.AbsoluteUri.TrimEnd('/') + "/",
            UriKind.Absolute);
        return new ZendeskConfiguration(normalizedBaseUrl, email, apiToken);
    }

    private static string NormalizeTicketId(string? externalRef)
    {
        var value = externalRef?.Trim();
        if (string.IsNullOrEmpty(value) ||
            value.Any(character => character is < '0' or > '9') ||
            !ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ticketId) ||
            ticketId == 0)
        {
            throw new ZendeskConnectorException(TicketNotFound);
        }

        return ticketId.ToString(CultureInfo.InvariantCulture);
    }

    private sealed record ZendeskConfiguration(Uri BaseUri, string Email, string ApiToken);
}
