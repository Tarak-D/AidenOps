using AIOps.Abstractions.Integrations;
using AIOps.Contracts.Api;
using AIOps.Host.Security;

namespace AIOps.Host.Api;

public static class ItsmIntegrationEndpoints
{
    public static IEndpointRouteBuilder MapItsmIntegrationApi(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/integrations/itsm/status",
                async (
                    IItsmProviderStatusService providerStatus,
                    CancellationToken ct) =>
                    Results.Ok(await providerStatus.GetStatusAsync(ct)))
            .WithName("GetItsmProviderStatus")
            .WithTags("Integrations")
            .Produces<ItsmProviderStatus>(StatusCodes.Status200OK)
            .AllowAnonymous();

        endpoints.MapPost(
                "/api/v1/integrations/itsm/provider",
                async (
                    SetItsmProviderRequest request,
                    IItsmProviderStatusService providerStatus,
                    IWebHostEnvironment environment,
                    HttpContext context,
                    CancellationToken ct) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Provider))
                    {
                        return Results.ValidationProblem(
                            new Dictionary<string, string[]>
                            {
                                ["provider"] = ["A provider value is required."]
                            });
                    }

                    var actor = context.User.Identity?.Name;
                    if (string.IsNullOrWhiteSpace(actor))
                    {
                        return Results.Unauthorized();
                    }

                    if (!environment.IsDevelopment() &&
                        context.User.Identity?.AuthenticationType ==
                        SecurityExtensions.DevIdentityHandler.SchemeName)
                    {
                        return Results.StatusCode(StatusCodes.Status403Forbidden);
                    }

                    try
                    {
                        return Results.Ok(await providerStatus.SelectProviderAsync(
                            request.Provider,
                            actor,
                            ct));
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.ValidationProblem(
                            new Dictionary<string, string[]>
                            {
                                ["provider"] = [ex.Message]
                            });
                    }
                })
            .WithName("SelectItsmProvider")
            .WithTags("Integrations")
            .RequireAuthorization(AuthPolicies.CanManageIntegrations)
            .Produces<ItsmProviderStatus>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        return endpoints;
    }
}
