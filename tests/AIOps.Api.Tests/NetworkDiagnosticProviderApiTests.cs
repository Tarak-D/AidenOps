using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using AIOps.Domain;
using AIOps.Abstractions.Integrations;
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

public sealed class NetworkDiagnosticProviderApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public NetworkDiagnosticProviderApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task StatusEndpointReportsSimulatedDefaultWithoutProviderSecrets()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/api/v1/integrations/network-diagnostics/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var body = document.RootElement;
        Assert.Equal("Vpn", body.GetProperty("category").GetString());
        Assert.Equal("Simulated", body.GetProperty("configuredProvider").GetString());
        Assert.Equal("Simulated", body.GetProperty("activeProvider").GetString());
        Assert.Equal("Simulated", body.GetProperty("connectionStatus").GetString());
        Assert.True(body.TryGetProperty("statusReason", out _));
        Assert.DoesNotContain("credential", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Vpn", "Cisco")]
    [InlineData("GeneralNetwork", "Datadog")]
    public async Task AdminCanSelectProviderAndStatusShowsNotConfigured(string category, string provider)
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "admin");
        using var response = await client.PostAsJsonAsync(
            "/api/v1/integrations/network-diagnostics/provider",
            new SetNetworkDiagnosticProviderRequest(category, provider));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(category, document.RootElement.GetProperty("category").GetString());
        Assert.Equal(provider, document.RootElement.GetProperty("activeProvider").GetString());
        Assert.Equal("NotConfigured", document.RootElement.GetProperty("connectionStatus").GetString());
    }

    [Fact]
    public async Task CategoryProviderMismatchReturnsValidationFailureWithoutPersisting()
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "admin");

        using var response = await client.PostAsJsonAsync(
            "/api/v1/integrations/network-diagnostics/provider",
            new SetNetworkDiagnosticProviderRequest("Vpn", "Datadog"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var status = await client.GetAsync("/api/v1/integrations/network-diagnostics/status");
        using var document = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.Equal("Simulated", document.RootElement.GetProperty("activeProvider").GetString());
    }

    [Fact]
    public async Task UnauthorizedProductionIdentityCannotChangeSelection()
    {
        using var client = CreateClient("Production");
        client.DefaultRequestHeaders.Add("X-Dev-User", "engineer");
        using var response = await client.PostAsJsonAsync(
            "/api/v1/integrations/network-diagnostics/provider",
            new SetNetworkDiagnosticProviderRequest("Vpn", "Fortinet"));
        Assert.False(response.IsSuccessStatusCode);

        using var status = await client.GetAsync("/api/v1/integrations/network-diagnostics/status");
        using var document = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.Equal("Simulated", document.RootElement.GetProperty("activeProvider").GetString());
    }

    private HttpClient CreateClient(string environment = "Development") => _factory.WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment(environment);
        builder.ConfigureTestServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<INetworkDiagnosticProviderSelectionStore>();
            services.AddSingleton<INetworkDiagnosticProviderSelectionStore, MemorySelectionStore>();
            services.RemoveAll<IItsmProviderSelectionStore>();
            services.AddSingleton<IItsmProviderSelectionStore, MemoryItsmSelectionStore>();
        });
    }).CreateClient();

    private sealed class MemorySelectionStore : INetworkDiagnosticProviderSelectionStore
    {
        private NetworkDiagnosticProviderSelection? _selection;
        public Task<NetworkDiagnosticProviderSelection?> GetSelectionAsync(CancellationToken ct = default) => Task.FromResult(_selection);
        public Task SetSelectionAsync(NetworkDiagnosticProviderSelection selection, string changedBy, CancellationToken ct = default)
        {
            _selection = selection;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryItsmSelectionStore : IItsmProviderSelectionStore
    {
        public Task<string?> GetProviderAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task SetProviderAsync(string provider, string changedBy, CancellationToken ct = default) => Task.CompletedTask;
    }
}
