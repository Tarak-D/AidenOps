using AIOps.Abstractions.Integrations;

namespace AIOps.Host.Api;

public static class CloudIntegrationEndpoints
{
    public static IEndpointRouteBuilder MapCloudIntegrationApi(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/integrations/cloud/status",
                (ICloudProviderStatusService providerStatus) =>
                    Results.Ok(providerStatus.GetStatus()))
            .WithName("GetCloudProviderStatus")
            .WithTags("Integrations")
            .Produces<CloudProviderStatus>(StatusCodes.Status200OK)
            .AllowAnonymous();

        return endpoints;
    }
}
