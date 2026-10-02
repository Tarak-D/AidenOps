using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Diagnostics;
using AIOps.Abstractions.Integrations;
using Microsoft.Extensions.Options;

namespace AIOps.Infrastructure.Integrations;

/// <summary>A sanitized, stable Jira Service Management provider failure.</summary>
public sealed class JiraConnectorException(string category)
    : InvalidOperationException(JiraConnectorException.GetMessage(category)), IAIOpsSafeTelemetryFailure
{
    public string Category { get; } = category;
    public string TelemetryErrorType => Category;

    private static string GetMessage(string category) => category switch
    {
        "JiraNotConfigured" => "JiraNotConfigured: Jira Service Management is not configured.",
        "JiraAuthenticationFailed" => "JiraAuthenticationFailed: Jira authentication failed.",
        "JiraPermissionDenied" => "JiraPermissionDenied: Jira permission was denied.",
        "JiraTicketNotFound" => "JiraTicketNotFound: The Jira ticket was not found.",
        "JiraInvalidState" => "JiraInvalidState: The requested Jira state is unsupported or unavailable.",
        "JiraThrottled" => "JiraThrottled: Jira is throttling requests.",
        "JiraTimeout" => "JiraTimeout: The Jira request timed out.",
        _ => "JiraProviderFailure: The Jira request failed."
    };
}

/// <summary>
/// Jira Cloud REST API adapter. Credentials are used only in server-side HTTP
/// authorization and are never included in results or provider errors.
/// </summary>
public sealed class JiraServiceManagementItsmConnector : IItsmProviderConnector
{
    private const string NotConfigured = "JiraNotConfigured";
    private const string AuthenticationFailed = "JiraAuthenticationFailed";
    private const string PermissionDenied = "JiraPermissionDenied";
    private const string TicketNotFound = "JiraTicketNotFound";
    private const string InvalidState = "JiraInvalidState";
    private const string Throttled = "JiraThrottled";
    private const string TimedOut = "JiraTimeout";
    private const string ProviderFailure = "JiraProviderFailure";

    private static readonly Regex ProjectKeyPattern = new(
        "^[A-Za-z][A-Za-z0-9_]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IssueKeyPattern = new(
        "^(?<project>[A-Za-z][A-Za-z0-9_]*)-(?<number>[0-9]+)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> SupportedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "Open",
        "InProgress",
        "Resolved",
        "Closed"
    };

    private readonly HttpClient _httpClient;
    private readonly JiraServiceManagementItsmOptions _options;

    public JiraServiceManagementItsmConnector(
        HttpClient httpClient,
        IOptions<ItsmIntegrationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;
        _options = options.Value.JiraServiceManagement ?? new JiraServiceManagementItsmOptions();
    }

    public string ProviderName => "JiraServiceManagement";
    public string DisplayName => "Jira Service Management";
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
                $"rest/api/3/project/{Uri.EscapeDataString(configuration.ProjectKey)}");
            using var response = await SendAsync(request, ct);
            EnsureSuccess(response, FailureContext.General);
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
            ?? throw new JiraConnectorException(NotConfigured);
        var issueKey = ValidateIssueKey(externalRef, configuration.ProjectKey);
        issueKey = await LookupIssueAsync(configuration, issueKey, ct);

        // Discover and validate the workflow transition before making any writes.
        var transitionId = string.IsNullOrWhiteSpace(state)
            ? null
            : await FindTransitionAsync(configuration, issueKey, state, ct);

        await AddCommentAsync(configuration, issueKey, note, ct);
        if (transitionId is not null)
        {
            await ApplyTransitionAsync(configuration, issueKey, transitionId, ct);
        }

        return JsonSerializer.Serialize(new
        {
            provider = ProviderName,
            ticketKey = issueKey,
            commentAdded = true,
            state = string.IsNullOrWhiteSpace(state) ? null : NormalizeState(state)
        });
    }

    private async Task AddCommentAsync(
        JiraConfiguration configuration,
        string issueKey,
        string note,
        CancellationToken ct)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            configuration,
            $"rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/comment");
        request.Content = JsonContent(new
        {
            body = new
            {
                type = "doc",
                version = 1,
                content = new[]
                {
                    new
                    {
                        type = "paragraph",
                        content = new[] { new { type = "text", text = note } }
                    }
                }
            }
        });

        using var response = await SendAsync(request, ct);
        EnsureSuccess(response, FailureContext.General);
    }

    private async Task<string> FindTransitionAsync(
        JiraConfiguration configuration,
        string issueKey,
        string requestedState,
        CancellationToken ct)
    {
        var normalizedState = NormalizeState(requestedState);
        if (!SupportedStates.Contains(normalizedState) ||
            !configuration.StateMappings.TryGetValue(normalizedState, out var targetStatus) ||
            string.IsNullOrWhiteSpace(targetStatus))
        {
            throw new JiraConnectorException(InvalidState);
        }

        using var request = CreateRequest(
            HttpMethod.Get,
            configuration,
            $"rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/transitions");
        using var response = await SendAsync(request, ct);
        EnsureSuccess(response, FailureContext.TransitionLookup);

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!document.RootElement.TryGetProperty("transitions", out var transitions) ||
                transitions.ValueKind != JsonValueKind.Array)
            {
                throw new JiraConnectorException(ProviderFailure);
            }

            foreach (var transition in transitions.EnumerateArray())
            {
                if (!transition.TryGetProperty("id", out var idElement) ||
                    !transition.TryGetProperty("to", out var toElement) ||
                    !toElement.TryGetProperty("name", out var nameElement) ||
                    !string.Equals(nameElement.GetString(), targetStatus, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var id = idElement.ValueKind == JsonValueKind.String
                    ? idElement.GetString()
                    : idElement.GetRawText();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    return id;
                }
            }

            throw new JiraConnectorException(InvalidState);
        }
        catch (JiraConnectorException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new JiraConnectorException(TimedOut);
        }
        catch
        {
            throw new JiraConnectorException(ProviderFailure);
        }
    }

    private async Task ApplyTransitionAsync(
        JiraConfiguration configuration,
        string issueKey,
        string transitionId,
        CancellationToken ct)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            configuration,
            $"rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/transitions");
        request.Content = JsonContent(new { transition = new { id = transitionId } });

        using var response = await SendAsync(request, ct);
        EnsureSuccess(response, FailureContext.TransitionApply);
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        JiraConfiguration configuration,
        string relativePath)
    {
        var request = new HttpRequestMessage(
            method,
            new Uri(configuration.BaseUri, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{configuration.Email}:{configuration.ApiToken}")));
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
            throw new JiraConnectorException(TimedOut);
        }
        catch (HttpRequestException exception) when (HasTimeoutInnerException(exception))
        {
            throw new JiraConnectorException(TimedOut);
        }
        catch
        {
            throw new JiraConnectorException(ProviderFailure);
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

    private static void EnsureSuccess(HttpResponseMessage response, FailureContext context)
    {
        if ((int)response.StatusCode is >= 200 and < 300)
        {
            return;
        }

        var category = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => AuthenticationFailed,
            HttpStatusCode.Forbidden => PermissionDenied,
            HttpStatusCode.NotFound when context == FailureContext.TicketLookup => TicketNotFound,
            HttpStatusCode.NotFound when context is FailureContext.TransitionLookup or FailureContext.TransitionApply => InvalidState,
            HttpStatusCode.TooManyRequests => Throttled,
            HttpStatusCode.RequestTimeout => TimedOut,
            HttpStatusCode.BadRequest when context == FailureContext.TransitionApply => InvalidState,
            _ => ProviderFailure
        };
        throw new JiraConnectorException(category);
    }

    private JiraConfiguration? GetConfiguration()
    {
        var baseUrl = _options.BaseUrl?.Trim();
        var email = _options.Email?.Trim();
        var apiToken = _options.ApiToken;
        var projectKey = _options.ProjectKey?.Trim();
        var issueType = _options.IssueType?.Trim();

        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(apiToken) ||
            string.IsNullOrWhiteSpace(projectKey) ||
            string.IsNullOrWhiteSpace(issueType))
        {
            return null;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedBaseUrl) ||
            parsedBaseUrl.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(parsedBaseUrl.Host) ||
            !string.IsNullOrEmpty(parsedBaseUrl.UserInfo) ||
            !string.IsNullOrEmpty(parsedBaseUrl.Query) ||
            !string.IsNullOrEmpty(parsedBaseUrl.Fragment) ||
            !ProjectKeyPattern.IsMatch(projectKey))
        {
            throw new JiraConnectorException(NotConfigured);
        }

        var stateMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in _options.StateMappings ?? [])
        {
            if (SupportedStates.Contains(mapping.Key) && !string.IsNullOrWhiteSpace(mapping.Value))
            {
                stateMappings[mapping.Key] = mapping.Value.Trim();
            }
        }

        return new JiraConfiguration(
            new Uri(parsedBaseUrl.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute),
            email,
            apiToken,
            projectKey,
            issueType,
            stateMappings);
    }

    private static string ValidateIssueKey(string? externalRef, string projectKey)
    {
        var issueKey = externalRef?.Trim();
        var match = issueKey is null ? Match.Empty : IssueKeyPattern.Match(issueKey);
        if (!match.Success ||
            !string.Equals(match.Groups["project"].Value, projectKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new JiraConnectorException(TicketNotFound);
        }

        return issueKey!;
    }

    private async Task<string> LookupIssueAsync(
        JiraConfiguration configuration,
        string issueKey,
        CancellationToken ct)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            configuration,
            $"rest/api/3/issue/{Uri.EscapeDataString(issueKey)}?fields=key,project,issuetype");
        using var response = await SendAsync(request, ct);
        EnsureSuccess(response, FailureContext.TicketLookup);

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = document.RootElement;
            var returnedKey = root.TryGetProperty("key", out var keyElement) ? keyElement.GetString() : null;
            var fields = root.TryGetProperty("fields", out var fieldsElement)
                ? fieldsElement
                : default;
            var project = fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty("project", out var projectElement)
                ? projectElement
                : default;
            var returnedProject = project.ValueKind == JsonValueKind.Object && project.TryGetProperty("key", out var projectKeyElement)
                ? projectKeyElement.GetString()
                : null;
            var issueType = fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty("issuetype", out var issueTypeElement)
                ? issueTypeElement
                : default;
            var returnedIssueType = issueType.ValueKind == JsonValueKind.Object && issueType.TryGetProperty("name", out var issueTypeName)
                ? issueTypeName.GetString()
                : null;

            if (!string.Equals(returnedKey, issueKey, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(returnedProject, configuration.ProjectKey, StringComparison.OrdinalIgnoreCase))
            {
                throw new JiraConnectorException(TicketNotFound);
            }

            if (!string.Equals(returnedIssueType, configuration.IssueType, StringComparison.OrdinalIgnoreCase))
            {
                throw new JiraConnectorException(ProviderFailure);
            }

            return returnedKey!;
        }
        catch (JiraConnectorException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new JiraConnectorException(TimedOut);
        }
        catch
        {
            throw new JiraConnectorException(ProviderFailure);
        }
    }

    private static string NormalizeState(string state) => state.Trim() switch
    {
        var value when value.Equals("In Progress", StringComparison.OrdinalIgnoreCase) => "InProgress",
        var value when value.Equals("InProgress", StringComparison.OrdinalIgnoreCase) => "InProgress",
        var value when value.Equals("Open", StringComparison.OrdinalIgnoreCase) => "Open",
        var value when value.Equals("Resolved", StringComparison.OrdinalIgnoreCase) => "Resolved",
        var value when value.Equals("Closed", StringComparison.OrdinalIgnoreCase) => "Closed",
        _ => state.Trim()
    };

    private static StringContent JsonContent<T>(T value) => new(
        JsonSerializer.Serialize(value),
        Encoding.UTF8,
        "application/json");

    private enum FailureContext
    {
        General,
        TicketLookup,
        TransitionLookup,
        TransitionApply
    }

    private sealed record JiraConfiguration(
        Uri BaseUri,
        string Email,
        string ApiToken,
        string ProjectKey,
        string IssueType,
        IReadOnlyDictionary<string, string> StateMappings);
}
