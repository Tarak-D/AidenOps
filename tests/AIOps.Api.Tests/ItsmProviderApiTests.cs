using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using AIOps.Domain;
using AIOps.Abstractions.Persistence;
using AIOps.Contracts.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIOps.Api.Tests;

public sealed class ItsmProviderApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ItsmProviderApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task StatusEndpoint_ReportsSimulatedByDefault()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/api/v1/integrations/itsm/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = document.RootElement;
        Assert.Equal("Simulated", body.GetProperty("configuredProvider").GetString());
        Assert.Equal("Simulated", body.GetProperty("activeProvider").GetString());
        Assert.Equal("Simulated", body.GetProperty("displayName").GetString());
        Assert.Equal("Simulated", body.GetProperty("mode").GetString());
        Assert.False(body.GetProperty("isProduction").GetBoolean());
        Assert.Equal("Simulated", body.GetProperty("connectionStatus").GetString());
    }

    [Fact]
    public async Task AdminCanSelectRealProviderAndStatusRemainsNotConfigured()
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "admin");

        using var selectionResponse = await client.PostAsJsonAsync(
            "/api/v1/integrations/itsm/provider",
            new SetItsmProviderRequest("ServiceNow"));
        Assert.Equal(HttpStatusCode.OK, selectionResponse.StatusCode);

        using var statusResponse = await client.GetAsync("/api/v1/integrations/itsm/status");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        using var document = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        var body = document.RootElement;
        Assert.Equal("ServiceNow", body.GetProperty("configuredProvider").GetString());
        Assert.Equal("ServiceNow", body.GetProperty("activeProvider").GetString());
        Assert.Equal("ServiceNow", body.GetProperty("displayName").GetString());
        Assert.Equal("Real", body.GetProperty("mode").GetString());
        Assert.True(body.GetProperty("isProduction").GetBoolean());
        Assert.Equal("NotConfigured", body.GetProperty("connectionStatus").GetString());
    }

    [Fact]
    public async Task DevelopmentIdentityCanManageIntegrations()
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "viewer");

        using var response = await client.PostAsJsonAsync(
            "/api/v1/integrations/itsm/provider",
            new SetItsmProviderRequest("JiraServiceManagement"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var status = await client.GetAsync("/api/v1/integrations/itsm/status");
        using var document = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.Equal(
            "JiraServiceManagement",
            document.RootElement.GetProperty("activeProvider").GetString());
    }

    [Fact]
    public async Task NonAdminCannotChangeProviderOutsideDevelopment()
    {
        using var client = CreateClient("Production");
        client.DefaultRequestHeaders.Add("X-Dev-User", "engineer");

        using var response = await client.PostAsJsonAsync(
            "/api/v1/integrations/itsm/provider",
            new SetItsmProviderRequest("Zendesk"));

        Assert.False(response.IsSuccessStatusCode);
        using var statusResponse = await client.GetAsync("/api/v1/integrations/itsm/status");
        using var document = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "Simulated",
            document.RootElement.GetProperty("activeProvider").GetString());
    }

    [Fact]
    public async Task ProductionAdminIdentityCanManageIntegrations()
    {
        using var client = CreateClient("Production", useProductionAdmin: true);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/integrations/itsm/provider",
            new SetItsmProviderRequest("Zendesk"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProviderStatusRemainsVisibleToUnauthorizedIdentity()
    {
        using var client = CreateClient("Production");
        client.DefaultRequestHeaders.Add("X-Dev-User", "engineer");

        using var response = await client.GetAsync("/api/v1/integrations/itsm/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Simulated", document.RootElement.GetProperty("activeProvider").GetString());
    }

    [Theory]
    [InlineData("/", "Autonomous IT Operations Agent Platform")]
    [InlineData("/integrations", "ITSM Integration")]
    public async Task DashboardAndIntegrationsRoutesLoadDirectlyAndOnRefresh(
        string path,
        string expectedPageContent)
    {
        using var client = CreateClient();

        for (var load = 0; load < 2; load++)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("Dashboard", html);
            Assert.Contains("Integrations", html);
            Assert.Contains("href=\"/\"", html);
            Assert.Contains("href=\"/integrations\"", html);
            Assert.Contains(expectedPageContent, html);
        }
    }

    [Fact]
    public async Task DevelopmentAdminHeaderCannotChangeProviderOutsideDevelopment()
    {
        using var client = CreateClient("Production");
        client.DefaultRequestHeaders.Add("X-Dev-User", "admin");

        using var response = await client.PostAsJsonAsync(
            "/api/v1/integrations/itsm/provider",
            new SetItsmProviderRequest("Zendesk"));

        Assert.False(response.IsSuccessStatusCode);
        using var statusResponse = await client.GetAsync("/api/v1/integrations/itsm/status");
        using var document = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "Simulated",
            document.RootElement.GetProperty("activeProvider").GetString());
    }

    private HttpClient CreateClient(
        string environment = "Development",
        bool useProductionAdmin = false) => _factory.WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment(environment);
        builder.ConfigureTestServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<IItsmProviderSelectionStore>();
            services.AddSingleton<IItsmProviderSelectionStore, MemorySelectionStore>();
            if (useProductionAdmin)
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = ProductionAdminHandler.TestScheme;
                        options.DefaultChallengeScheme = ProductionAdminHandler.TestScheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, ProductionAdminHandler>(
                    ProductionAdminHandler.TestScheme,
                        _ => { });
            }
        });
    }).CreateClient();

    private sealed class ProductionAdminHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string TestScheme = "TestProductionAdmin";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, "production-admin"),
                    new Claim(ClaimTypes.Role, nameof(UserRole.Admin))
                ],
                TestScheme);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
        }
    }

    private sealed class MemorySelectionStore : IItsmProviderSelectionStore
    {
        private string? _provider;

        public Task<string?> GetProviderAsync(CancellationToken ct = default) =>
            Task.FromResult(_provider);

        public Task SetProviderAsync(
            string provider,
            string changedBy,
            CancellationToken ct = default)
        {
            _provider = provider;
            return Task.CompletedTask;
        }
    }
}
