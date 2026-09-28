using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Integrations;
using AIOps.Infrastructure;
using AIOps.Infrastructure.Integrations;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.Runtime;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace AIOps.Infrastructure.Tests;

public sealed class AwsEc2CloudProviderTests
{
    private static readonly IOptions<CloudIntegrationOptions> DefaultOptions =
        Options.Create(new CloudIntegrationOptions());

    [Fact]
    public async Task GetInstanceStatusAsync_ReportsStatusReturnedByEc2()
    {
        var ec2 = new Mock<IAmazonEC2>();
        ec2.Setup(client => client.DescribeInstancesAsync(
                It.IsAny<DescribeInstancesRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Describe("i-123", InstanceStateName.Stopped));
        var provider = CreateProvider(ec2, DefaultOptions);

        var result = await provider.GetInstanceStatusAsync("i-123");

        Assert.Contains("\"status\":\"stopped\"", result);
        Assert.Contains("\"provider\":\"aws-ec2\"", result);
        ec2.Verify(client => client.DescribeInstancesAsync(
            It.Is<DescribeInstancesRequest>(request =>
                request.InstanceIds.SequenceEqual(new[] { "i-123" })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetInstanceStatusAsync_MissingInstanceIsNotReportedAsSuccess()
    {
        var ec2 = new Mock<IAmazonEC2>();
        ec2.Setup(client => client.DescribeInstancesAsync(
                It.IsAny<DescribeInstancesRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DescribeInstancesResponse
            {
                Reservations = []
            });
        var provider = CreateProvider(ec2, DefaultOptions);

        var error = await Assert.ThrowsAsync<AwsCloudProviderException>(
            () => provider.GetInstanceStatusAsync("i-missing"));

        Assert.Equal("AwsInstanceNotFound", error.Code);
        Assert.DoesNotContain("i-missing", error.Message);
    }

    [Fact]
    public async Task RestartInstanceAsync_RequestsRebootAndReportsAcceptanceOnly()
    {
        var ec2 = new Mock<IAmazonEC2>();
        ec2.Setup(client => client.RebootInstancesAsync(
                It.IsAny<RebootInstancesRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RebootInstancesResponse());
        var provider = CreateProvider(ec2, DefaultOptions);

        var result = await provider.RestartInstanceAsync("i-123");

        Assert.Contains("\"status\":\"reboot-request-accepted\"", result);
        ec2.Verify(client => client.RebootInstancesAsync(
            It.Is<RebootInstancesRequest>(request =>
                request.InstanceIds.SequenceEqual(new[] { "i-123" })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProviderErrors_AreCategorizedWithoutExposingSdkMessages()
    {
        const string sensitiveMessage = "secret-credential-details";
        var ec2 = new Mock<IAmazonEC2>();
        ec2.Setup(client => client.DescribeInstancesAsync(
                It.IsAny<DescribeInstancesRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonClientException(sensitiveMessage));
        var provider = CreateProvider(ec2, DefaultOptions);

        var error = await Assert.ThrowsAsync<AwsCloudProviderException>(
            () => provider.GetInstanceStatusAsync("i-123"));

        Assert.Equal("AwsCredentialsOrConfigurationUnavailable", error.Code);
        Assert.DoesNotContain(sensitiveMessage, error.Message);
    }

    [Fact]
    public async Task UnexpectedTransportErrors_AreSanitized()
    {
        const string sensitiveMessage = "private transport detail";
        var ec2 = new Mock<IAmazonEC2>();
        ec2.Setup(client => client.DescribeInstancesAsync(
                It.IsAny<DescribeInstancesRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(sensitiveMessage));
        var provider = CreateProvider(ec2, DefaultOptions);

        var error = await Assert.ThrowsAsync<AwsCloudProviderException>(
            () => provider.GetInstanceStatusAsync("i-123"));

        Assert.Equal("AwsOperationFailed", error.Code);
        Assert.DoesNotContain(sensitiveMessage, error.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "AwsAccessDenied")]
    [InlineData(HttpStatusCode.TooManyRequests, "AwsThrottled")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "AwsServiceUnavailable")]
    public async Task ServiceFailures_AreSanitizedAndCategorized(
        HttpStatusCode statusCode,
        string expectedCode)
    {
        const string sensitiveMessage = "request token and private detail";
        var ec2 = new Mock<IAmazonEC2>();
        ec2.Setup(client => client.DescribeInstancesAsync(
                It.IsAny<DescribeInstancesRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonServiceException(sensitiveMessage)
            {
                StatusCode = statusCode
            });
        var provider = CreateProvider(ec2, DefaultOptions);

        var error = await Assert.ThrowsAsync<AwsCloudProviderException>(
            () => provider.GetInstanceStatusAsync("i-123"));

        Assert.Equal(expectedCode, error.Code);
        Assert.DoesNotContain(sensitiveMessage, error.Message);
    }

    [Fact]
    public async Task GetInstanceStatusAsync_EnforcesTimeout()
    {
        var ec2 = new Mock<IAmazonEC2>();
        ec2.Setup(client => client.DescribeInstancesAsync(
                It.IsAny<DescribeInstancesRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<DescribeInstancesRequest, CancellationToken>(
                (_, token) => WaitForDescribeAsync(token));
        var provider = CreateProvider(
            ec2,
            Options.Create(new CloudIntegrationOptions { TimeoutSeconds = 1 }));

        var error = await Assert.ThrowsAsync<AwsCloudProviderException>(
            () => provider.GetInstanceStatusAsync("i-123"));

        Assert.Equal("AwsTimeout", error.Code);
    }

    [Fact]
    public async Task CallerCancellation_IsPropagated()
    {
        var ec2 = new Mock<IAmazonEC2>();
        ec2.Setup(client => client.DescribeInstancesAsync(
                It.IsAny<DescribeInstancesRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<DescribeInstancesRequest, CancellationToken>(
                (_, token) => WaitForDescribeAsync(token));
        var provider = CreateProvider(ec2, DefaultOptions);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetInstanceStatusAsync("i-123", cancellation.Token));
    }

    [Fact]
    public void ProviderSelection_DefaultsToSimulated()
    {
        using var services = CreateServices(new Dictionary<string, string?>());

        Assert.IsType<SimulatedCloudProvider>(
            services.GetRequiredService<ICloudProvider>());
    }

    [Fact]
    public void ProviderSelection_AwsEc2UsesConfiguredProvider()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:Cloud:Provider"] = "AwsEc2",
                ["Integrations:Cloud:Region"] = "us-east-1"
            })
            .Build();
        services.AddAIOpsInfrastructure(config);
        services.AddSingleton<IAmazonEC2>(new Mock<IAmazonEC2>().Object);

        using var serviceProvider = services.BuildServiceProvider();

        Assert.IsType<AwsEc2CloudProvider>(
            serviceProvider.GetRequiredService<ICloudProvider>());
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("")]
    public void ProviderSelection_RejectsInvalidProvider(string provider)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:Cloud:Provider"] = provider
            })
            .Build();

        Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddAIOpsInfrastructure(config));
    }

    [Fact]
    public void ProviderSelection_RejectsExplicitNullProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:Cloud:Provider"] = null
            })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddAIOpsInfrastructure(config));

        Assert.Equal(
            "Integrations:Cloud:Provider must be 'Simulated' or 'AwsEc2'.",
            error.Message);
    }

    private static ServiceProvider CreateServices(
        Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();
        services.AddAIOpsInfrastructure(config);
        return services.BuildServiceProvider();
    }

    private static AwsEc2CloudProvider CreateProvider(
        Mock<IAmazonEC2> ec2,
        IOptions<CloudIntegrationOptions> options) =>
        new(new Lazy<IAmazonEC2>(() => ec2.Object), options);

    private static DescribeInstancesResponse Describe(
        string instanceId,
        InstanceStateName status) => new()
    {
        Reservations =
        [
            new Reservation
            {
                Instances =
                [
                    new Instance
                    {
                        InstanceId = instanceId,
                        State = new InstanceState { Name = status }
                    }
                ]
            }
        ]
    };

    private static async Task<DescribeInstancesResponse> WaitForDescribeAsync(
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return new DescribeInstancesResponse();
    }
}
