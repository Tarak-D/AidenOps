using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Integrations;
using Microsoft.Extensions.Options;

namespace AIOps.Infrastructure.Integrations;

/// <summary>
/// Describes the implementation actually resolved by dependency injection.
/// Reading the status never calls the cloud provider.
/// </summary>
public sealed class CloudProviderStatusService : ICloudProviderStatusService
{
    private readonly ICloudProvider _cloudProvider;
    private readonly string _configuredProvider;

    public CloudProviderStatusService(
        ICloudProvider cloudProvider,
        IOptions<CloudIntegrationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(cloudProvider);
        ArgumentNullException.ThrowIfNull(options);

        _cloudProvider = cloudProvider;
        _configuredProvider = options.Value.Provider;
    }

    public CloudProviderStatus GetStatus() => _cloudProvider switch
    {
        SimulatedCloudProvider => new CloudProviderStatus(
            _configuredProvider,
            "Simulated",
            "Simulated",
            "Simulated",
            false),
        AwsEc2CloudProvider => new CloudProviderStatus(
            _configuredProvider,
            "AwsEc2",
            "AWS EC2",
            "Real",
            true),
        _ => throw new InvalidOperationException(
            $"Cloud provider status is not defined for implementation '{_cloudProvider.GetType().Name}'.")
    };
}
