using System.Net;
using System.Net.Http.Json;
using AIOps.Abstractions.Evaluation;
using AIOps.Contracts.Api;
using AIOps.Contracts.Evaluation;
using AIOps.Domain;
using AIOps.Host.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;

namespace AIOps.Api.Tests;

public sealed class AuthenticationBoundaryApiTests
    : IClassFixture<ApiTestWebApplicationFactory>
{
    private readonly ApiTestWebApplicationFactory _developmentFactory;

    public AuthenticationBoundaryApiTests(
        ApiTestWebApplicationFactory developmentFactory)
    {
        _developmentFactory = developmentFactory;
    }

    [Fact]
    public async Task Development_viewer_cannot_decide_approvals_or_run_acceptance()
    {
        var acceptanceStore = new RecordingAcceptanceStore();
        using var client = _developmentFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEvaluationAcceptanceStore>();
                services.AddSingleton<IEvaluationAcceptanceStore>(
                    acceptanceStore);
            });
        }).CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "viewer");

        foreach (var approve in new[] { true, false })
        {
            using var approvalResponse = await client.PostAsJsonAsync(
                $"/api/v1/approvals/{Guid.NewGuid()}/decision",
                new DecideApprovalRequest(
                    approve,
                    "Unauthorized test decision."));

            Assert.True(
                approvalResponse.StatusCode == HttpStatusCode.Forbidden,
                await approvalResponse.Content.ReadAsStringAsync());
        }

        using var acceptanceResponse = await client.PostAsJsonAsync(
            "/api/v1/evaluations/acceptance",
            new EvaluationAcceptanceRequest("baseline", "candidate"));

        Assert.True(
            acceptanceResponse.StatusCode == HttpStatusCode.Forbidden,
            await acceptanceResponse.Content.ReadAsStringAsync());
        Assert.Empty(acceptanceStore.Records);
    }

    [Fact]
    public async Task Development_header_authenticates_for_queue_access()
    {
        using var client = _developmentFactory.WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development")).CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "viewer");

        using var response = await client.GetAsync("/api/v1/tickets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Production_ignores_development_identity_header_for_sensitive_routes()
    {
        var acceptanceStore = new RecordingAcceptanceStore();
        using var client = _developmentFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEvaluationAcceptanceStore>();
                services.AddSingleton<IEvaluationAcceptanceStore>(
                    acceptanceStore);
            });
        }).CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "admin");

        using var ticketResponse = await client.GetAsync("/api/v1/tickets");
        using var knowledgeResponse = await client.GetAsync(
            "/api/v1/knowledge/search?q=VPN");
        using var acceptanceResponse = await client.PostAsJsonAsync(
            "/api/v1/evaluations/acceptance",
            new EvaluationAcceptanceRequest("baseline", "candidate"));

        Assert.Equal(HttpStatusCode.Unauthorized, ticketResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, knowledgeResponse.StatusCode);
        Assert.True(
            acceptanceResponse.StatusCode == HttpStatusCode.Unauthorized,
            await acceptanceResponse.Content.ReadAsStringAsync());

        foreach (var approve in new[] { true, false })
        {
            using var approvalResponse = await client.PostAsJsonAsync(
                $"/api/v1/approvals/{Guid.NewGuid()}/decision",
                new DecideApprovalRequest(
                    approve,
                    "Untrusted production header."));

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                approvalResponse.StatusCode);
        }

        Assert.Empty(acceptanceStore.Records);
    }

    [Fact]
    public async Task Production_without_identity_provider_fails_closed_for_privileged_operations()
    {
        var acceptanceStore = new RecordingAcceptanceStore();
        using var client = _developmentFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEvaluationAcceptanceStore>();
                services.AddSingleton<IEvaluationAcceptanceStore>(
                    acceptanceStore);
            });
        }).CreateClient();

        using var acceptanceResponse = await client.PostAsJsonAsync(
            "/api/v1/evaluations/acceptance",
            new EvaluationAcceptanceRequest("baseline", "candidate"));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            acceptanceResponse.StatusCode);

        foreach (var approve in new[] { true, false })
        {
            using var approvalResponse = await client.PostAsJsonAsync(
                $"/api/v1/approvals/{Guid.NewGuid()}/decision",
                new DecideApprovalRequest(
                    approve,
                    "No configured identity."));

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                approvalResponse.StatusCode);
        }

        using var resumeResponse = await client.PostAsJsonAsync(
            $"/api/v1/tickets/{Guid.NewGuid()}/agent-runs/resume",
            new { actionExecutionId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Unauthorized, resumeResponse.StatusCode);
        Assert.Empty(acceptanceStore.Records);
    }

    [Fact]
    public async Task Staging_ignores_development_identity_header_and_fails_closed()
    {
        using var client = _developmentFactory.WithWebHostBuilder(builder =>
            builder.UseEnvironment("Staging")).CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "admin");

        using var ticketResponse = await client.GetAsync("/api/v1/tickets");
        using var approvalResponse = await client.PostAsJsonAsync(
            $"/api/v1/approvals/{Guid.NewGuid()}/decision",
            new DecideApprovalRequest(true, "Untrusted staging header."));

        Assert.Equal(HttpStatusCode.Unauthorized, ticketResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, approvalResponse.StatusCode);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void Development_identity_configuration_is_rejected_outside_development(
        string enabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:DevelopmentIdentity:Enabled"] = enabled
            })
            .Build();
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddAIOpsSecurity(
                configuration,
                new TestWebHostEnvironment("Production")));

        Assert.Contains(
            "configured outside the Development environment",
            exception.Message);
    }

    [Fact]
    public async Task Development_identity_scheme_is_not_registered_outside_development()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddAIOpsSecurity(
            configuration,
            new TestWebHostEnvironment("Test"));
        using var provider = services.BuildServiceProvider();

        var schemeProvider =
            provider.GetRequiredService<IAuthenticationSchemeProvider>();

        Assert.Null(await schemeProvider.GetSchemeAsync(
            SecurityExtensions.DevIdentityHandler.SchemeName));
        Assert.Equal(
            SecurityExtensions.UnconfiguredAuthenticationHandler.SchemeName,
            provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<AuthenticationOptions>>()
                .Value.DefaultScheme);
    }

    private sealed class RecordingAcceptanceStore
        : IEvaluationAcceptanceStore
    {
        public List<EvaluationAcceptanceRecord> Records { get; } = [];

        public Task RecordAsync(
            EvaluationAcceptanceRecord result,
            CancellationToken cancellationToken = default)
        {
            Records.Add(result);
            return Task.CompletedTask;
        }

        public Task<EvaluationAcceptanceRecord?> GetAsync(
            Guid resultId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Records.SingleOrDefault(record => record.Id == resultId));
    }

    private sealed class TestWebHostEnvironment(string environmentName)
        : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "AIOps.Api.Tests";

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();

        public string ContentRootPath { get; set; } = string.Empty;

        public string EnvironmentName { get; set; } = environmentName;

        public string WebRootPath { get; set; } = string.Empty;

        public IFileProvider WebRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
