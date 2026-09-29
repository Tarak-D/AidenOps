using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIOps.Abstractions.Configuration;
using AIOps.Infrastructure.Integrations;
using Microsoft.Extensions.Options;

namespace AIOps.Infrastructure.Tests;

public sealed class JiraServiceManagementItsmConnectorTests
{
    private const string Email = "agent@example.test";
    private const string ApiToken = "test-jira-api-token-not-for-output";
    private const string ProjectKey = "OPS";
    private const string IssueKey = "OPS-123";

    [Fact]
    public async Task MissingConfiguration_IsNotConfigured_AndUpdateFailsClosed()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK)));
        var connector = CreateConnector(handler, new JiraServiceManagementItsmOptions());

        Assert.Equal("NotConfigured", await connector.GetConnectionStatusAsync());
        var exception = await Assert.ThrowsAsync<JiraConnectorException>(
            () => connector.UpdateTicketAsync(IssueKey, "note"));

        Assert.Equal("JiraNotConfigured", exception.Category);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("http://jira.example.test")]
    [InlineData("not-a-url")]
    [InlineData("https://user:pass@jira.example.test")]
    [InlineData("https://jira.example.test?token=bad")]
    public async Task InvalidBaseUrl_IsRejectedWithoutRequest(string baseUrl)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK)));
        var connector = CreateConnector(handler, ConfiguredOptions(baseUrl));

        var exception = await Assert.ThrowsAsync<JiraConnectorException>(
            () => connector.UpdateTicketAsync(IssueKey, "note"));

        Assert.Equal("JiraNotConfigured", exception.Category);
        Assert.DoesNotContain(ApiToken, exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SuccessfulConnectionUsesConfiguredProjectAndBasicAuthentication()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/rest/api/3/project/OPS", request.RequestUri!.AbsolutePath);
            Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
            Assert.Equal(
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Email}:{ApiToken}")),
                request.Headers.Authorization?.Parameter);
            return Task.FromResult(Json(HttpStatusCode.OK, "{\"key\":\"OPS\"}"));
        });
        var connector = CreateConnector(handler, ConfiguredOptions());

        Assert.Equal("Connected", await connector.GetConnectionStatusAsync());
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ConnectionStatusRequiresSuccessfulAuthenticatedRequest(HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(
            Json(statusCode, $"{ApiToken} {Email}")));
        var connector = CreateConnector(handler, ConfiguredOptions());

        var status = await connector.GetConnectionStatusAsync();

        Assert.Equal("Unavailable", status);
        Assert.DoesNotContain(ApiToken, status);
        Assert.Single(handler.Requests);
        Assert.Equal("Basic", handler.Requests[0].AuthorizationScheme);
    }

    [Fact]
    public async Task TicketLookupUsesEscapedIssueKeyAndChecksConfiguredProjectAndType()
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, Issue())
                : Json(HttpStatusCode.Created, "{\"id\":\"comment-id\"}")));
        var connector = CreateConnector(handler, ConfiguredOptions());

        await connector.UpdateTicketAsync(IssueKey, "triaged");

        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("/rest/api/3/issue/OPS-123", handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal("?fields=key,project,issuetype", handler.Requests[0].Uri.Query);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.EndsWith("/comment", handler.Requests[1].Uri.AbsolutePath);
    }

    [Fact]
    public async Task IssueKeyMustMatchConfiguredProject()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK)));
        var connector = CreateConnector(handler, ConfiguredOptions());

        var exception = await Assert.ThrowsAsync<JiraConnectorException>(
            () => connector.UpdateTicketAsync("OTHER-123", "note"));

        Assert.Equal("JiraTicketNotFound", exception.Category);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TicketNotFoundIsSanitized()
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.NotFound, $"raw {ApiToken} provider body")
                : Json(HttpStatusCode.OK)));
        var connector = CreateConnector(handler, ConfiguredOptions());

        var exception = await Assert.ThrowsAsync<JiraConnectorException>(
            () => connector.UpdateTicketAsync(IssueKey, "note"));

        Assert.Equal("JiraTicketNotFound", exception.Category);
        Assert.DoesNotContain(ApiToken, exception.Message);
        Assert.DoesNotContain("provider body", exception.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CommentUsesJiraDocumentFormat_AndReturnsOnlyAllowlistedSummary()
    {
        string? commentBody = null;
        var handler = new RecordingHandler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, Issue());
            }

            commentBody = await request.Content!.ReadAsStringAsync(ct);
            return Json(HttpStatusCode.Created, $"{{\"body\":\"{ApiToken}\"}}");
        });
        var connector = CreateConnector(handler, ConfiguredOptions());

        var result = await connector.UpdateTicketAsync(IssueKey, "safe work note");

        using var body = JsonDocument.Parse(commentBody!);
        Assert.Equal("doc", body.RootElement.GetProperty("body").GetProperty("type").GetString());
        Assert.Equal(
            "safe work note",
            body.RootElement.GetProperty("body").GetProperty("content")[0]
                .GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Contains(IssueKey, result);
        Assert.Contains("\"commentAdded\":true", result);
        Assert.DoesNotContain(ApiToken, result);
        Assert.DoesNotContain("provider body", result);
    }

    [Fact]
    public async Task ConfiguredStateDiscoversAndAppliesAvailableTransition()
    {
        string? transitionBody = null;
        var handler = new RecordingHandler(async (request, ct) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith(IssueKey, StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, Issue());
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/transitions", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{"transitions":[{"id":"91","name":"Resolve issue","to":{"name":"Resolved"}}]}""");
            }

            if (path.EndsWith("/comment", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.Created);
            }

            transitionBody = await request.Content!.ReadAsStringAsync(ct);
            return Json(HttpStatusCode.NoContent);
        });
        var connector = CreateConnector(handler, ConfiguredOptions());

        var result = await connector.UpdateTicketAsync(IssueKey, "note", "Resolved");

        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.EndsWith("/transitions", handler.Requests[1].Uri.AbsolutePath);
        Assert.Equal(HttpMethod.Post, handler.Requests[3].Method);
        using var transition = JsonDocument.Parse(transitionBody!);
        Assert.Equal("91", transition.RootElement.GetProperty("transition").GetProperty("id").GetString());
        Assert.Contains("\"state\":\"Resolved\"", result);
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("InProgress")]
    [InlineData("In Progress")]
    [InlineData("Resolved")]
    [InlineData("Closed")]
    public async Task SupportedStateNamesAreNormalizedAndMapped(string state)
    {
        var targetStatus = state.Trim() switch
        {
            "InProgress" or "In Progress" => "In Progress",
            "Resolved" => "Resolved",
            "Closed" => "Closed",
            _ => "Open"
        };
        var handler = WorkflowHandler(targetStatus);
        var connector = CreateConnector(handler, ConfiguredOptions());

        await connector.UpdateTicketAsync(IssueKey, "note", state);

        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal(HttpMethod.Post, handler.Requests[3].Method);
    }

    [Theory]
    [InlineData("arbitrary")]
    [InlineData("Cancelled")]
    public async Task UnsupportedStateIsRejectedWithoutWrites(string state)
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, Issue())
                : Json(HttpStatusCode.OK)));
        var connector = CreateConnector(handler, ConfiguredOptions());

        var exception = await Assert.ThrowsAsync<JiraConnectorException>(
            () => connector.UpdateTicketAsync(IssueKey, "note", state));

        Assert.Equal("JiraInvalidState", exception.Category);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task MissingAvailableTransitionIsRejectedBeforeCommentWrite()
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/transitions", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, """{"transitions":[{"id":"2","to":{"name":"In Progress"}}]}""")
                : request.Method == HttpMethod.Get
                    ? Json(HttpStatusCode.OK, Issue())
                    : Json(HttpStatusCode.Created)));
        var connector = CreateConnector(handler, ConfiguredOptions());

        var exception = await Assert.ThrowsAsync<JiraConnectorException>(
            () => connector.UpdateTicketAsync(IssueKey, "note", "Resolved"));

        Assert.Equal("JiraInvalidState", exception.Category);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    [Fact]
    public async Task NullStateOnlyAddsComment()
    {
        var handler = WorkflowHandler("Resolved");
        var connector = CreateConnector(handler, ConfiguredOptions());

        await connector.UpdateTicketAsync(IssueKey, "note", state: null);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.EndsWith("/comment", handler.Requests[1].Uri.AbsolutePath);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "JiraAuthenticationFailed")]
    [InlineData(HttpStatusCode.Forbidden, "JiraPermissionDenied")]
    [InlineData(HttpStatusCode.TooManyRequests, "JiraThrottled")]
    [InlineData(HttpStatusCode.InternalServerError, "JiraProviderFailure")]
    public async Task UpdateErrorsAreStableAndDoNotExposeProviderBody(
        HttpStatusCode statusCode,
        string expectedCategory)
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, Issue())
                : Json(statusCode, $"raw response contains {ApiToken} and {Email}")));
        var connector = CreateConnector(handler, ConfiguredOptions());

        var exception = await Assert.ThrowsAsync<JiraConnectorException>(
            () => connector.UpdateTicketAsync(IssueKey, "note"));

        Assert.Equal(expectedCategory, exception.Category);
        Assert.DoesNotContain(ApiToken, exception.Message);
        Assert.DoesNotContain(Email, exception.Message);
        Assert.DoesNotContain("raw response", exception.Message);
    }

    [Fact]
    public async Task TransitionFailureMapsToInvalidState()
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/transitions", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, """{"transitions":[{"id":"91","to":{"name":"Resolved"}}]}""")
                : request.Method == HttpMethod.Get
                    ? Json(HttpStatusCode.OK, Issue())
                    : request.RequestUri!.AbsolutePath.EndsWith("/comment", StringComparison.Ordinal)
                        ? Json(HttpStatusCode.Created)
                        : Json(HttpStatusCode.BadRequest, $"raw {ApiToken}")));
        var connector = CreateConnector(handler, ConfiguredOptions());

        var exception = await Assert.ThrowsAsync<JiraConnectorException>(
            () => connector.UpdateTicketAsync(IssueKey, "note", "Resolved"));

        Assert.Equal("JiraInvalidState", exception.Category);
        Assert.DoesNotContain(ApiToken, exception.Message);
    }

    [Fact]
    public async Task TimeoutIsSanitizedAndCallerCancellationPropagates()
    {
        var timeoutHandler = new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("raw timeout and token " + ApiToken)));
        var timeoutConnector = CreateConnector(timeoutHandler, ConfiguredOptions());
        var timeout = await Assert.ThrowsAsync<JiraConnectorException>(
            () => timeoutConnector.UpdateTicketAsync(IssueKey, "note"));

        Assert.Equal("JiraTimeout", timeout.Category);
        Assert.DoesNotContain(ApiToken, timeout.Message);

        var cancellationHandler = new RecordingHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json(HttpStatusCode.OK);
        });
        var cancellationConnector = CreateConnector(cancellationHandler, ConfiguredOptions());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancellationConnector.GetConnectionStatusAsync(cancellation.Token));
    }

    [Fact]
    public async Task TicketLookup404IsTicketNotFoundAndIssueTypeIsEnforced()
    {
        var notFoundHandler = new RecordingHandler((_, _) => Task.FromResult(Json(HttpStatusCode.NotFound)));
        var notFoundConnector = CreateConnector(notFoundHandler, ConfiguredOptions());
        var notFound = await Assert.ThrowsAsync<JiraConnectorException>(
            () => notFoundConnector.UpdateTicketAsync(IssueKey, "note"));
        Assert.Equal("JiraTicketNotFound", notFound.Category);

        var wrongType = Issue(issueType: "Task");
        var issueTypeHandler = new RecordingHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, wrongType)));
        var issueTypeConnector = CreateConnector(issueTypeHandler, ConfiguredOptions());
        var typeError = await Assert.ThrowsAsync<JiraConnectorException>(
            () => issueTypeConnector.UpdateTicketAsync(IssueKey, "note"));
        Assert.Equal("JiraProviderFailure", typeError.Category);
        Assert.Single(issueTypeHandler.Requests);
    }

    [Fact]
    public async Task JiraProviderSelectionUsesRealConnectorWithoutFallback()
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, Issue())
                : Json(HttpStatusCode.Created)));
        var jira = CreateConnector(handler, ConfiguredOptions());
        Assert.Equal("JiraServiceManagement", jira.ProviderName);
        Assert.Equal("Real", jira.Mode);
        Assert.True(jira.IsProduction);

        var errorHandler = new RecordingHandler((_, _) => Task.FromResult(Json(HttpStatusCode.Unauthorized)));
        var inaccessible = CreateConnector(errorHandler, ConfiguredOptions());
        Assert.Equal("Unavailable", await inaccessible.GetConnectionStatusAsync());
        Assert.Single(errorHandler.Requests);
    }

    private static RecordingHandler WorkflowHandler(string targetStatus) => new(async (request, ct) =>
    {
        if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/transitions", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                transitions = new[] { new { id = "91", to = new { name = targetStatus } } }
            }));
        }

        if (request.Method == HttpMethod.Get)
        {
            return Json(HttpStatusCode.OK, Issue());
        }

        if (request.RequestUri!.AbsolutePath.EndsWith("/comment", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.Created);
        }

        _ = await request.Content!.ReadAsStringAsync(ct);
        return Json(HttpStatusCode.NoContent);
    });

    private static string Issue(string project = ProjectKey, string issueType = "Incident") =>
        JsonSerializer.Serialize(new
        {
            key = IssueKey,
            fields = new
            {
                project = new { key = project },
                issuetype = new { name = issueType }
            }
        });

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body = "{}") => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static JiraServiceManagementItsmOptions ConfiguredOptions(
        string baseUrl = "https://jira.example.test/") => new()
    {
        BaseUrl = baseUrl,
        Email = Email,
        ApiToken = ApiToken,
        ProjectKey = ProjectKey,
        IssueType = "Incident",
        StateMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Open"] = "Open",
            ["InProgress"] = "In Progress",
            ["Resolved"] = "Resolved",
            ["Closed"] = "Closed"
        }
    };

    private static JiraServiceManagementItsmConnector CreateConnector(
        HttpMessageHandler handler,
        JiraServiceManagementItsmOptions jiraOptions)
    {
        var options = new ItsmIntegrationOptions
        {
            TimeoutSeconds = 2,
            JiraServiceManagement = jiraOptions
        };
        return new JiraServiceManagementItsmConnector(
            new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) },
            Options.Create(options));
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));
            return await send(request, cancellationToken);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string? AuthorizationScheme,
        string? AuthorizationParameter);
}
