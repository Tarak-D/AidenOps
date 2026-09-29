using System.Net;
using System.Text;
using System.Text.Json;
using AIOps.Abstractions.Configuration;
using AIOps.Infrastructure.Integrations;
using Microsoft.Extensions.Options;

namespace AIOps.Infrastructure.Tests;

public sealed class ZendeskItsmConnectorTests
{
    private const string Email = "agent@example.test";
    private const string ApiToken = "zendesk-test-token-not-for-output";
    private const string TicketId = "123456";
    private const string BaseUrl = "https://example.zendesk.com/";

    [Fact]
    public async Task MissingConfiguration_IsNotConfiguredAndUpdateFailsClosed()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK)));
        var connector = CreateConnector(handler, new ZendeskItsmOptions());

        Assert.Equal("NotConfigured", await connector.GetConnectionStatusAsync());
        var exception = await Assert.ThrowsAsync<ZendeskConnectorException>(
            () => connector.UpdateTicketAsync(TicketId, "private note"));

        Assert.Equal("ZendeskNotConfigured", exception.Category);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("http://example.zendesk.com")]
    [InlineData("not-a-url")]
    [InlineData("https://user:pass@example.zendesk.com")]
    [InlineData("https://example.zendesk.com?token=bad")]
    public async Task InvalidBaseUrl_FailsWithoutRequest(string baseUrl)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK)));
        var connector = CreateConnector(handler, ConfiguredOptions(baseUrl));

        var exception = await Assert.ThrowsAsync<ZendeskConnectorException>(
            () => connector.UpdateTicketAsync(TicketId, "private note"));

        Assert.Equal("ZendeskNotConfigured", exception.Category);
        Assert.DoesNotContain(ApiToken, exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("BaseUrl")]
    [InlineData("Email")]
    [InlineData("ApiToken")]
    public async Task MissingRequiredSetting_ReturnsNotConfiguredWithoutRequest(string missing)
    {
        var options = ConfiguredOptions();
        switch (missing)
        {
            case "BaseUrl": options.BaseUrl = ""; break;
            case "Email": options.Email = ""; break;
            case "ApiToken": options.ApiToken = ""; break;
        }
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK)));
        var connector = CreateConnector(handler, options);

        Assert.Equal("NotConfigured", await connector.GetConnectionStatusAsync());
        var exception = await Assert.ThrowsAsync<ZendeskConnectorException>(
            () => connector.UpdateTicketAsync(TicketId, "note"));
        Assert.Equal("ZendeskNotConfigured", exception.Category);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ConnectionStatusUsesAuthenticatedCurrentUserEndpoint()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/v2/users/me.json", request.RequestUri!.AbsolutePath);
            AssertBasicAuth(request);
            return Task.FromResult(Json(HttpStatusCode.OK, """{"user":{"id":9}}"""));
        });
        var connector = CreateConnector(handler, ConfiguredOptions());

        Assert.Equal("Connected", await connector.GetConnectionStatusAsync());
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ConnectionStatusReturnsUnavailableWhenAuthenticatedCheckFails(HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(statusCode, ApiToken)));
        var connector = CreateConnector(handler, ConfiguredOptions());

        Assert.Equal("Unavailable", await connector.GetConnectionStatusAsync());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TicketLookupUsesSafeNumericIdAndAuthenticates()
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, Ticket())
                : Json(HttpStatusCode.OK)));
        var connector = CreateConnector(handler, ConfiguredOptions());

        await connector.UpdateTicketAsync("00123456", "note");

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal($"/api/v2/tickets/{TicketId}.json", handler.Requests[0].Uri.AbsolutePath);
        AssertBasicAuth(handler.Requests[0]);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.Equal(handler.Requests[0].Uri.AbsolutePath, handler.Requests[1].Uri.AbsolutePath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1/../2")]
    [InlineData("tickets/123")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("１２３")]
    public async Task InvalidTicketIdIsRejectedBeforeNetwork(string ticketId)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK)));
        var connector = CreateConnector(handler, ConfiguredOptions());

        var exception = await Assert.ThrowsAsync<ZendeskConnectorException>(
            () => connector.UpdateTicketAsync(ticketId, "note"));

        Assert.Equal("ZendeskTicketNotFound", exception.Category);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MissingTicketMaps404ToSanitizedNotFound()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(
            Json(HttpStatusCode.NotFound, $"raw Zendesk body {ApiToken}")));
        var connector = CreateConnector(handler, ConfiguredOptions());

        var exception = await Assert.ThrowsAsync<ZendeskConnectorException>(
            () => connector.UpdateTicketAsync(TicketId, "note"));

        Assert.Equal("ZendeskTicketNotFound", exception.Category);
        Assert.DoesNotContain(ApiToken, exception.Message);
        Assert.DoesNotContain("raw Zendesk body", exception.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CommentUpdateIsPrivateAndResponseBodyIsNotReturned()
    {
        string? capturedBody = null;
        var handler = new RecordingHandler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, Ticket());
            }

            capturedBody = await request.Content!.ReadAsStringAsync(ct);
            return Json(HttpStatusCode.OK, $"{{\"ticket\":{{\"description\":\"{ApiToken}\"}}}}");
        });
        var connector = CreateConnector(handler, ConfiguredOptions());

        var result = await connector.UpdateTicketAsync(TicketId, "investigation note");

        using var body = JsonDocument.Parse(capturedBody!);
        var comment = body.RootElement.GetProperty("ticket").GetProperty("comment");
        Assert.Equal("investigation note", comment.GetProperty("body").GetString());
        Assert.False(comment.GetProperty("public").GetBoolean());
        Assert.False(body.RootElement.GetProperty("ticket").TryGetProperty("status", out _));
        Assert.Contains(TicketId, result);
        Assert.Contains("\"commentVisibility\":\"private\"", result);
        Assert.DoesNotContain(ApiToken, result);
        Assert.DoesNotContain(EncodedCredentials(), result);
        Assert.DoesNotContain("description", result);
    }

    [Theory]
    [InlineData("Open", "open")]
    [InlineData("InProgress", "open")]
    [InlineData("In Progress", "open")]
    [InlineData("Resolved", "solved")]
    [InlineData("Closed", "closed")]
    public async Task SupportedApplicationStatesMapToZendeskStatuses(string state, string expectedStatus)
    {
        string? capturedBody = null;
        var handler = new RecordingHandler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, Ticket());
            }

            capturedBody = await request.Content!.ReadAsStringAsync(ct);
            return Json(HttpStatusCode.OK);
        });
        var connector = CreateConnector(handler, ConfiguredOptions());

        var result = await connector.UpdateTicketAsync(TicketId, "note", state);

        using var body = JsonDocument.Parse(capturedBody!);
        Assert.Equal(expectedStatus, body.RootElement.GetProperty("ticket").GetProperty("status").GetString());
        Assert.Contains($"\"status\":\"{expectedStatus}\"", result);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("solved-ish")]
    [InlineData("arbitrary")]
    public async Task UnsupportedStateIsRejectedBeforeAnyRequest(string state)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK)));
        var connector = CreateConnector(handler, ConfiguredOptions());

        var exception = await Assert.ThrowsAsync<ZendeskConnectorException>(
            () => connector.UpdateTicketAsync(TicketId, "note", state));

        Assert.Equal("ZendeskInvalidState", exception.Category);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NoteAndStateAreUpdatedTogetherInOnePut()
    {
        string? capturedBody = null;
        var handler = new RecordingHandler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, Ticket());
            }

            capturedBody = await request.Content!.ReadAsStringAsync(ct);
            return Json(HttpStatusCode.OK);
        });
        var connector = CreateConnector(handler, ConfiguredOptions());

        await connector.UpdateTicketAsync(TicketId, "resolved privately", "Resolved");

        Assert.Equal(2, handler.Requests.Count);
        using var body = JsonDocument.Parse(capturedBody!);
        var ticket = body.RootElement.GetProperty("ticket");
        Assert.Equal("resolved privately", ticket.GetProperty("comment").GetProperty("body").GetString());
        Assert.False(ticket.GetProperty("comment").GetProperty("public").GetBoolean());
        Assert.Equal("solved", ticket.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "ZendeskAuthenticationFailed")]
    [InlineData(HttpStatusCode.Forbidden, "ZendeskPermissionDenied")]
    [InlineData(HttpStatusCode.TooManyRequests, "ZendeskThrottled")]
    [InlineData(HttpStatusCode.InternalServerError, "ZendeskProviderFailure")]
    public async Task UpdateFailuresMapToStableSanitizedCategories(HttpStatusCode statusCode, string expectedCategory)
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, Ticket())
                : Json(statusCode, $"raw provider body {ApiToken} {Email}")));
        var connector = CreateConnector(handler, ConfiguredOptions());

        var exception = await Assert.ThrowsAsync<ZendeskConnectorException>(
            () => connector.UpdateTicketAsync(TicketId, "note"));

        Assert.Equal(expectedCategory, exception.Category);
        Assert.DoesNotContain(ApiToken, exception.Message);
        Assert.DoesNotContain(EncodedCredentials(), exception.Message);
        Assert.DoesNotContain(Email, exception.Message);
        Assert.DoesNotContain("raw provider body", exception.Message);
    }

    [Fact]
    public async Task MalformedTicketResponseIsSanitizedProviderFailure()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(
            Json(HttpStatusCode.OK, $"not-json {ApiToken}")));
        var connector = CreateConnector(handler, ConfiguredOptions());

        var exception = await Assert.ThrowsAsync<ZendeskConnectorException>(
            () => connector.UpdateTicketAsync(TicketId, "note"));

        Assert.Equal("ZendeskProviderFailure", exception.Category);
        Assert.DoesNotContain(ApiToken, exception.Message);
    }

    [Fact]
    public async Task TimeoutIsSanitizedAndCallerCancellationPropagates()
    {
        var timeoutHandler = new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout token " + ApiToken)));
        var timeoutConnector = CreateConnector(timeoutHandler, ConfiguredOptions());
        var timeout = await Assert.ThrowsAsync<ZendeskConnectorException>(
            () => timeoutConnector.UpdateTicketAsync(TicketId, "note"));
        Assert.Equal("ZendeskTimeout", timeout.Category);
        Assert.DoesNotContain(ApiToken, timeout.Message);

        var cancelHandler = new RecordingHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json(HttpStatusCode.OK);
        });
        var cancelConnector = CreateConnector(cancelHandler, ConfiguredOptions());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancelConnector.GetConnectionStatusAsync(cancellation.Token));
    }

    [Fact]
    public async Task ProviderSelectionResolvesZendeskWithoutFallback()
    {
        var connector = CreateConnector(
            new RecordingHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK))),
            ConfiguredOptions());
        Assert.Equal("Zendesk", connector.ProviderName);
        Assert.Equal("Real", connector.Mode);
        Assert.True(connector.IsProduction);

        var selected = new ItsmProviderSelectionService(
            new SelectionStore("Zendesk"),
            [connector, new SimulatedItsmConnector()],
            Options.Create(new ItsmIntegrationOptions()));
        var status = await selected.GetStatusAsync();
        Assert.Equal("Zendesk", status.ActiveProvider);
        Assert.Equal("Connected", status.ConnectionStatus);
    }

    private static string Ticket(string id = TicketId) =>
        JsonSerializer.Serialize(new
        {
            ticket = new { id = ulong.Parse(id, System.Globalization.CultureInfo.InvariantCulture) }
        });

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body = "{}") => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static void AssertBasicAuth(HttpRequestMessage request)
    {
        Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Email}/token:{ApiToken}"));
        Assert.Equal(expected, request.Headers.Authorization?.Parameter);
    }

    private static void AssertBasicAuth(RecordedRequest request)
    {
        Assert.Equal("Basic", request.AuthorizationScheme);
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Email}/token:{ApiToken}"));
        Assert.Equal(expected, request.AuthorizationParameter);
    }

    private static string EncodedCredentials() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Email}/token:{ApiToken}"));

    private static ZendeskItsmOptions ConfiguredOptions(string baseUrl = BaseUrl) => new()
    {
        BaseUrl = baseUrl,
        Email = Email,
        ApiToken = ApiToken
    };

    private static ZendeskItsmConnector CreateConnector(
        HttpMessageHandler handler,
        ZendeskItsmOptions zendeskOptions)
    {
        var options = new ItsmIntegrationOptions
        {
            TimeoutSeconds = 2,
            Zendesk = zendeskOptions
        };
        return new ZendeskItsmConnector(
            new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) },
            Options.Create(options));
    }

    private sealed class SelectionStore(string provider)
        : AIOps.Abstractions.Persistence.IItsmProviderSelectionStore
    {
        public Task<string?> GetProviderAsync(CancellationToken ct = default) => Task.FromResult<string?>(provider);
        public Task SetProviderAsync(string value, string changedBy, CancellationToken ct = default) => Task.CompletedTask;
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
