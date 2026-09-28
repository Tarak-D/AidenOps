using System.Text.Json;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Integrations;
using AIOps.Infrastructure.Integrations;
using Amazon.EC2;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AIOps.Api.Tests;

public sealed class CloudProviderStatusApiTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CloudProviderStatusApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task StatusEndpoint_ReportsResolvedSimulatedProvider()
    {
        using var client = _factory.WithWebHostBuilder(builder =>
                builder.UseEnvironment("Test"))
            .CreateClient();

        using var response = await client.GetAsync(
            "/api/v1/integrations/cloud/status");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var body = document.RootElement;
        Assert.Equal("Simulated", body.GetProperty("configuredProvider").GetString());
        Assert.Equal("Simulated", body.GetProperty("activeProvider").GetString());
        Assert.Equal("Simulated", body.GetProperty("displayName").GetString());
        Assert.Equal("Simulated", body.GetProperty("mode").GetString());
        Assert.False(body.GetProperty("isProduction").GetBoolean());
    }

    [Fact]
    public async Task StatusEndpoint_ReportsAwsProviderWithoutCreatingClientOrCallingAws()
    {
        using var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Integrations:Cloud:Provider"] = "AwsEc2",
                    ["Integrations:Cloud:Region"] = "us-east-1"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICloudProvider>();
                services.AddSingleton<ICloudProvider>(_ =>
                    new AwsEc2CloudProvider(
                        new Lazy<IAmazonEC2>(
                            () => throw new InvalidOperationException(
                                "Status endpoint attempted to create an AWS client.")),
                        Options.Create(new CloudIntegrationOptions
                        {
                            Provider = "AwsEc2"
                        })));
            });
        }).CreateClient();

        using var response = await client.GetAsync(
            "/api/v1/integrations/cloud/status");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var body = document.RootElement;
        Assert.Equal("AwsEc2", body.GetProperty("configuredProvider").GetString());
        Assert.Equal("AwsEc2", body.GetProperty("activeProvider").GetString());
        Assert.Equal("AWS EC2", body.GetProperty("displayName").GetString());
        Assert.Equal("Real", body.GetProperty("mode").GetString());
        Assert.True(body.GetProperty("isProduction").GetBoolean());
    }
}
