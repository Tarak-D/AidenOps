using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Integrations;
using AIOps.Infrastructure;
using AIOps.Infrastructure.Integrations;
using Amazon.EC2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AIOps.Infrastructure.Tests;

public sealed class CloudProviderStatusServiceTests
{
    [Fact]
    public void SimulatedProvider_ReportsSimulatedStatus()
    {
        var service = new CloudProviderStatusService(
            new SimulatedCloudProvider(),
            Options.Create(new CloudIntegrationOptions()));

        var status = service.GetStatus();

        Assert.Equal("Simulated", status.ConfiguredProvider);
        Assert.Equal("Simulated", status.ActiveProvider);
        Assert.Equal("Simulated", status.DisplayName);
        Assert.Equal("Simulated", status.Mode);
        Assert.False(status.IsProduction);
    }

    [Fact]
    public void AwsEc2Provider_ReportsRealStatusWithoutCreatingClient()
    {
        var client = new Lazy<IAmazonEC2>(
            () => throw new InvalidOperationException(
                "Provider status must not create an AWS client."));
        var service = new CloudProviderStatusService(
            new AwsEc2CloudProvider(
                client,
                Options.Create(new CloudIntegrationOptions
                {
                    Provider = "AwsEc2"
                })),
            Options.Create(new CloudIntegrationOptions
            {
                Provider = "AwsEc2"
            }));

        var status = service.GetStatus();

        Assert.Equal("AwsEc2", status.ConfiguredProvider);
        Assert.Equal("AwsEc2", status.ActiveProvider);
        Assert.Equal("AWS EC2", status.DisplayName);
        Assert.Equal("Real", status.Mode);
        Assert.True(status.IsProduction);
        Assert.False(client.IsValueCreated);
    }

    [Fact]
    public void StatusUsesResolvedImplementationRatherThanConfiguredValue()
    {
        var service = new CloudProviderStatusService(
            new SimulatedCloudProvider(),
            Options.Create(new CloudIntegrationOptions
            {
                Provider = "AwsEc2"
            }));

        var status = service.GetStatus();

        Assert.Equal("AwsEc2", status.ConfiguredProvider);
        Assert.Equal("Simulated", status.ActiveProvider);
        Assert.Equal("Simulated", status.Mode);
        Assert.False(status.IsProduction);
    }

    [Fact]
    public void AwsProviderStatusFromDependencyInjectionDoesNotResolveSdkClient()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:Cloud:Provider"] = "AwsEc2",
                ["Integrations:Cloud:Region"] = "us-east-1"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddAIOpsInfrastructure(config);

        using var provider = services.BuildServiceProvider();
        var status = provider
            .GetRequiredService<ICloudProviderStatusService>()
            .GetStatus();
        var client = provider.GetRequiredService<Lazy<IAmazonEC2>>();

        Assert.Equal("AwsEc2", status.ActiveProvider);
        Assert.Equal("Real", status.Mode);
        Assert.True(status.IsProduction);
        Assert.False(client.IsValueCreated);
    }
}
