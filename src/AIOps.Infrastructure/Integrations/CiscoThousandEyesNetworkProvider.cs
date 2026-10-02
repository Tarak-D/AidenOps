using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Diagnostics;
using AIOps.Abstractions.Integrations;
using Microsoft.Extensions.Options;

namespace AIOps.Infrastructure.Integrations;

/// <summary>
/// Cisco ThousandEyes general-network diagnostics provider.
///
/// Creates an Agent-to-Server Instant Test through the ThousandEyes v7 API
/// and retrieves the resulting network metrics.
/// </summary>
public sealed class CiscoThousandEyesNetworkProvider(
    HttpClient httpClient,
    IOptions<CiscoThousandEyesOptions> options,
    TimeProvider timeProvider) : IGeneralNetworkDiagnosticsProvider
{
    private const string Provider = "CiscoThousandEyes";
    private const string DisplayNameValue = "Cisco ThousandEyes";

    private const string InstantTestPath =
        "tests/agent-to-server/instant";

    private const int MaximumPollAttempts = 20;
    private const int MinimumPollIntervalMilliseconds = 100;
    private const int MaximumPollIntervalMilliseconds = 10_000;

    private readonly CiscoThousandEyesOptions _options = options.Value;

    public string Category => NetworkDiagnosticCategories.GeneralNetwork;

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
                return new NetworkDiagnosticProviderHealth(
                    NetworkDiagnosticConnectionStates.NotConfigured,
                    "Cisco ThousandEyes credentials and agent configuration are not available.");
            }

            ValidateBaseUrl();

            using var request = CreateRequest(
                HttpMethod.Get,
                BuildUri("agents"));

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                return MapHealthFailure(response.StatusCode);
            }

            var body = await response.Content.ReadAsStringAsync(ct);

            if (!ContainsConfiguredAgent(body, _options.AgentId))
            {
                return new NetworkDiagnosticProviderHealth(
                    NetworkDiagnosticConnectionStates.ProviderError,
                    "Cisco ThousandEyes configured agent was not found.");
            }

            return new NetworkDiagnosticProviderHealth(
                NetworkDiagnosticConnectionStates.Connected,
                "Cisco ThousandEyes API is reachable and the configured agent is available.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new NetworkDiagnosticProviderHealth(
                NetworkDiagnosticConnectionStates.Timeout,
                "Cisco ThousandEyes provider request timed out.");
        }
        catch (HttpRequestException)
        {
            return new NetworkDiagnosticProviderHealth(
                NetworkDiagnosticConnectionStates.Unavailable,
                "Cisco ThousandEyes provider is unavailable.");
        }
        catch (ThousandEyesProviderException ex)
        {
            return new NetworkDiagnosticProviderHealth(
                ex.HealthState,
                ex.Message);
        }
        catch
        {
            return new NetworkDiagnosticProviderHealth(
                NetworkDiagnosticConnectionStates.ProviderError,
                "Cisco ThousandEyes provider request failed.");
        }
    }

    public async Task<GeneralNetworkDiagnosticResult> RunDiagnosticsAsync(
        GeneralNetworkDiagnosticRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateTarget(request);
        ValidateConfiguration();

        try
        {
            var payload = new
            {
                server = request.Target,
                agents = new[]
                {
                    new
                    {
                        agentId = _options.AgentId
                    }
                },
                networkMeasurements = true
            };

            using var createRequest = CreateRequest(
                HttpMethod.Post,
                BuildUriWithAccountGroup(InstantTestPath));

            createRequest.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var createResponse = await httpClient.SendAsync(
                createRequest,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            if (!createResponse.IsSuccessStatusCode)
            {
                throw MapExecutionException(createResponse.StatusCode);
            }

            var createBody =
                await createResponse.Content.ReadAsStringAsync(ct);

            using var createDocument =
                ParseDocument(createBody);

            var testId =
                GetString(createDocument.RootElement, "testId");

            if (string.IsNullOrWhiteSpace(testId))
            {
                throw new ThousandEyesProviderException(
                    ThousandEyesError.ProviderFailure,
                    "Cisco ThousandEyes instant test response did not contain a test ID.");
            }

            var resultUri =
                GetNetworkResultUri(
                    createDocument.RootElement,
                    testId);

            return await PollResultAsync(
                request,
                testId,
                resultUri,
                ct);
        }
        catch (ThousandEyesProviderException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ThousandEyesProviderException(
                ThousandEyesError.Timeout,
                "Cisco ThousandEyes provider request timed out.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw new ThousandEyesProviderException(
                ThousandEyesError.Unavailable,
                "Cisco ThousandEyes provider is unavailable.");
        }
        catch
        {
            throw new ThousandEyesProviderException(
                ThousandEyesError.ProviderFailure,
                "Cisco ThousandEyes provider request failed.");
        }
    }

    private async Task<GeneralNetworkDiagnosticResult> PollResultAsync(
        GeneralNetworkDiagnosticRequest request,
        string testId,
        Uri resultUri,
        CancellationToken ct)
    {
        var attempts = Math.Clamp(
            _options.ResultPollAttempts,
            1,
            MaximumPollAttempts);

        var intervalMilliseconds = Math.Clamp(
            _options.ResultPollIntervalMilliseconds,
            MinimumPollIntervalMilliseconds,
            MaximumPollIntervalMilliseconds);

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var resultRequest = CreateRequest(
                HttpMethod.Get,
                resultUri);

            using var response = await httpClient.SendAsync(
                resultRequest,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                if (attempt < attempts)
                {
                    await Task.Delay(
                        intervalMilliseconds,
                        ct);

                    continue;
                }

                throw new ThousandEyesProviderException(
                    ThousandEyesError.TargetNotFound,
                    "Cisco ThousandEyes network result was not found.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw MapExecutionException(response.StatusCode);
            }

            var body =
                await response.Content.ReadAsStringAsync(ct);

            using var document =
                ParseDocument(body);

            var result =
                FindLatestResult(
                    document.RootElement);

            if (result.HasValue)
            {
                return MapResult(
                    request,
                    testId,
                    result.Value);
            }

            if (attempt < attempts)
            {
                await Task.Delay(
                    intervalMilliseconds,
                    ct);
            }
        }

        return new GeneralNetworkDiagnosticResult(
            Status: "Pending",
            Provider: DisplayNameValue,
            ObservationType: NetworkDiagnosticObservationType.ActiveTest,
            ObservedAt: timeProvider.GetUtcNow(),
            Target: request.Target,
            State: "Pending",
            Latency: null,
            PacketLoss: null,
            ExecutionId: testId,
            ProviderDetails: new NetworkDiagnosticProviderDetails(
                new Dictionary<string, string?>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["testId"] = testId,
                    ["status"] = "Pending",
                    ["state"] = "Pending",
                    ["source"] = DisplayNameValue
                }));
    }

    private GeneralNetworkDiagnosticResult MapResult(
        GeneralNetworkDiagnosticRequest request,
        string testId,
        JsonElement result)
    {
        var latency = GetDouble(result, "avgLatency");
        var packetLoss = GetDouble(result, "loss");
        var observedAt =
            GetDateTimeOffset(result, "date")
            ?? timeProvider.GetUtcNow();

        var server = GetString(result, "server");
        var serverIp = GetString(result, "serverIp");
        var roundId = GetString(result, "roundId");

        var agentName = string.Empty;
        var agentLocation = string.Empty;

        if (result.TryGetProperty("agent", out var agent) &&
            agent.ValueKind == JsonValueKind.Object)
        {
            agentName = GetString(agent, "agentName") ?? string.Empty;
            agentLocation = GetString(agent, "location") ?? string.Empty;
        }

        var state =
            result.TryGetProperty("errorDetails", out var errorDetails) &&
            errorDetails.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(errorDetails.GetString())
                ? "Error"
                : "Completed";

        var status = state == "Completed"
            ? "Success"
            : "ProviderError";

        var fields = new Dictionary<string, string?>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["testId"] = testId,
            ["status"] = status,
            ["state"] = state,
            ["source"] = DisplayNameValue
        };

        AddField(fields, "hostName", server);
        AddField(fields, "publicIp", serverIp);
        AddField(fields, "agentName", agentName);
        AddField(fields, "location", agentLocation);
        AddField(fields, "observationId", roundId);

        return new GeneralNetworkDiagnosticResult(
            Status: status,
            Provider: DisplayNameValue,
            ObservationType: NetworkDiagnosticObservationType.ActiveTest,
            ObservedAt: observedAt,
            Target: request.Target,
            State: state,
            Latency: latency,
            PacketLoss: packetLoss,
            ExecutionId: testId,
            ProviderDetails: new NetworkDiagnosticProviderDetails(fields));
    }

    private static JsonElement? FindLatestResult(JsonElement root)
    {
        if (!root.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? latest = null;
        DateTimeOffset latestDate = DateTimeOffset.MinValue;

        foreach (var result in results.EnumerateArray())
        {
            if (result.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var date =
                GetDateTimeOffset(result, "date");

            if (latest is null)
            {
                latest = result;

                if (date.HasValue)
                {
                    latestDate = date.Value;
                }

                continue;
            }

            if (date.HasValue && date.Value > latestDate)
            {
                latest = result;
                latestDate = date.Value;
            }
        }

        return latest;
    }

    private Uri GetNetworkResultUri(
        JsonElement root,
        string testId)
    {
        if (root.TryGetProperty("_links", out var links) &&
            links.TryGetProperty("testResults", out var testResults) &&
            testResults.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in testResults.EnumerateArray())
            {
                var href = GetString(link, "href");

                if (string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                if (!href.Contains(
                        "/network",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Uri.TryCreate(
                        href,
                        UriKind.Absolute,
                        out var absoluteUri))
                {
                    return AddAccountGroupIfRequired(absoluteUri);
                }

                if (Uri.TryCreate(
                        href,
                        UriKind.Relative,
                        out var relativeUri))
                {
                    return BuildUri(relativeUri.ToString());
                }
            }
        }

        return BuildUriWithAccountGroup(
            $"test-results/{Uri.EscapeDataString(testId)}/network");
    }

    private Uri AddAccountGroupIfRequired(Uri uri)
    {
        if (string.IsNullOrWhiteSpace(_options.AccountGroupId) ||
            uri.Query.Contains(
                "aid=",
                StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        var separator = uri.Query.Length == 0 ? "?" : "&";

        return new Uri(
            $"{uri}{separator}aid={Uri.EscapeDataString(_options.AccountGroupId)}");
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        Uri uri)
    {
        var request = new HttpRequestMessage(
            method,
            uri);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                _options.ApiToken);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/hal+json"));

        return request;
    }

    private Uri BuildUri(string relativePath)
    {
        var baseUri = GetBaseUri();

        return new Uri(
            baseUri,
            relativePath.TrimStart('/'));
    }

    private Uri BuildUriWithAccountGroup(string relativePath)
    {
        var uri = BuildUri(relativePath);

        if (string.IsNullOrWhiteSpace(_options.AccountGroupId))
        {
            return uri;
        }

        var separator = uri.Query.Length == 0 ? "?" : "&";

        return new Uri(
            $"{uri}{separator}aid={Uri.EscapeDataString(_options.AccountGroupId)}");
    }

    private Uri GetBaseUri()
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new ThousandEyesProviderException(
                ThousandEyesError.NotConfigured,
                "Cisco ThousandEyes BaseUrl is not configured.");
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
            throw new ThousandEyesProviderException(
                ThousandEyesError.ProviderFailure,
                "Cisco ThousandEyes BaseUrl must use HTTPS.");
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
        !string.IsNullOrWhiteSpace(_options.AgentId);

    private void ValidateConfiguration()
    {
        if (!IsConfigured())
        {
            throw new ThousandEyesProviderException(
                ThousandEyesError.NotConfigured,
                "Cisco ThousandEyes credentials and agent configuration are not available.");
        }

        ValidateBaseUrl();
    }

    private static void ValidateTarget(
        GeneralNetworkDiagnosticRequest request)
    {
        if (request.TargetType is
            NetworkDiagnosticTargetType.Ip or
            NetworkDiagnosticTargetType.Host)
        {
            return;
        }

        throw new ThousandEyesProviderException(
            ThousandEyesError.UnsupportedTarget,
            "Cisco ThousandEyes Agent-to-Server diagnostics support Ip and Host targets.");
    }

    private static NetworkDiagnosticProviderHealth MapHealthFailure(
        HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized =>
                new(
                    NetworkDiagnosticConnectionStates.AuthenticationFailed,
                    "Cisco ThousandEyes authentication failed."),

            HttpStatusCode.Forbidden =>
                new(
                    NetworkDiagnosticConnectionStates.PermissionDenied,
                    "Cisco ThousandEyes permission was denied."),

            HttpStatusCode.RequestTimeout =>
                new(
                    NetworkDiagnosticConnectionStates.Timeout,
                    "Cisco ThousandEyes provider request timed out."),

            HttpStatusCode.TooManyRequests =>
                new(
                    NetworkDiagnosticConnectionStates.ProviderError,
                    "Cisco ThousandEyes provider request was throttled."),

            >= HttpStatusCode.InternalServerError =>
                new(
                    NetworkDiagnosticConnectionStates.Unavailable,
                    "Cisco ThousandEyes provider is unavailable."),

            _ =>
                new(
                    NetworkDiagnosticConnectionStates.ProviderError,
                    "Cisco ThousandEyes provider request failed.")
        };

    private static ThousandEyesProviderException MapExecutionException(
        HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized =>
                new(
                    ThousandEyesError.AuthenticationFailed,
                    "Cisco ThousandEyes authentication failed."),

            HttpStatusCode.Forbidden =>
                new(
                    ThousandEyesError.PermissionDenied,
                    "Cisco ThousandEyes permission was denied."),

            HttpStatusCode.NotFound =>
                new(
                    ThousandEyesError.TargetNotFound,
                    "Cisco ThousandEyes network diagnostic resource was not found."),

            HttpStatusCode.RequestTimeout =>
                new(
                    ThousandEyesError.Timeout,
                    "Cisco ThousandEyes provider request timed out."),

            HttpStatusCode.TooManyRequests =>
                new(
                    ThousandEyesError.Throttled,
                    "Cisco ThousandEyes provider request was throttled."),

            >= HttpStatusCode.InternalServerError =>
                new(
                    ThousandEyesError.Unavailable,
                    "Cisco ThousandEyes provider is unavailable."),

            _ =>
                new(
                    ThousandEyesError.ProviderFailure,
                    "Cisco ThousandEyes provider request failed.")
        };

    private static bool ContainsConfiguredAgent(
        string body,
        string? configuredAgentId = null)
    {
        // The configured ID is intentionally passed by the caller through
        // the overload below. This helper is kept defensive for malformed
        // provider responses.
        if (string.IsNullOrWhiteSpace(configuredAgentId))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty(
                    "agents",
                    out var agents) ||
                agents.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var agent in agents.EnumerateArray())
            {
                var agentId = GetString(agent, "agentId");

                if (string.Equals(
                        agentId,
                        configuredAgentId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static JsonDocument ParseDocument(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new ThousandEyesProviderException(
                ThousandEyesError.ProviderFailure,
                "Cisco ThousandEyes provider response was malformed.");
        }
    }

    private static string? GetString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()?.Trim();
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.ToString();
        }

        return null;
    }

    private static double? GetDouble(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(
                value.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateTimeOffset? GetDateTimeOffset(
        JsonElement element,
        string propertyName)
    {
        var value = GetString(
            element,
            propertyName);

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal |
            System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
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
}

public enum ThousandEyesError
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

public sealed class ThousandEyesProviderException(
    ThousandEyesError category,
    string message) : Exception(message), IAIOpsSafeTelemetryFailure
{
    public ThousandEyesError Category { get; } = category;

    public string TelemetryErrorType => Category.ToString();

    public string HealthState =>
        Category switch
        {
            ThousandEyesError.NotConfigured =>
                NetworkDiagnosticConnectionStates.NotConfigured,

            ThousandEyesError.AuthenticationFailed =>
                NetworkDiagnosticConnectionStates.AuthenticationFailed,

            ThousandEyesError.PermissionDenied =>
                NetworkDiagnosticConnectionStates.PermissionDenied,

            ThousandEyesError.Timeout =>
                NetworkDiagnosticConnectionStates.Timeout,

            ThousandEyesError.Unavailable =>
                NetworkDiagnosticConnectionStates.Unavailable,

            _ =>
                NetworkDiagnosticConnectionStates.ProviderError
        };
}
