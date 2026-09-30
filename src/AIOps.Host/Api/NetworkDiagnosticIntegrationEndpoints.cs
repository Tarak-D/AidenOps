using AIOps.Abstractions.Integrations;
using AIOps.Contracts.Api;
using AIOps.Host.Security;

namespace AIOps.Host.Api;

public static class NetworkDiagnosticIntegrationEndpoints
{
    public static IEndpointRouteBuilder MapNetworkDiagnosticIntegrationApi(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/integrations/network-diagnostics/status",
                async (INetworkDiagnosticProviderSelectionService status, CancellationToken ct) =>
                    Results.Ok(await status.GetStatusAsync(ct)))
            .WithName("GetNetworkDiagnosticProviderStatus")
            .WithTags("Integrations")
            .Produces<NetworkDiagnosticProviderStatus>(StatusCodes.Status200OK)
            .AllowAnonymous();

        endpoints.MapPost(
                "/api/v1/integrations/network-diagnostics/provider",
                async (
                    SetNetworkDiagnosticProviderRequest request,
                    INetworkDiagnosticProviderSelectionService selection,
                    IWebHostEnvironment environment,
                    HttpContext context,
                    CancellationToken ct) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Category) ||
                        string.IsNullOrWhiteSpace(request.Provider))
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            [string.IsNullOrWhiteSpace(request.Category) ? "category" : "provider"] =
                                ["A category and provider value are required."]
                        });
                    }

                    var actor = context.User.Identity?.Name;
                    if (string.IsNullOrWhiteSpace(actor))
                    {
                        return Results.Unauthorized();
                    }

                    if (!environment.IsDevelopment() &&
                        context.User.Identity?.AuthenticationType == SecurityExtensions.DevIdentityHandler.SchemeName)
                    {
                        return Results.StatusCode(StatusCodes.Status403Forbidden);
                    }

                    try
                    {
                        return Results.Ok(await selection.SelectProviderAsync(
                            request.Category,
                            request.Provider,
                            actor,
                            ct));
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["provider"] = [ex.Message]
                        });
                    }
                })
            .WithName("SelectNetworkDiagnosticProvider")
            .WithTags("Integrations")
            .RequireAuthorization(AuthPolicies.CanManageIntegrations)
            .Produces<NetworkDiagnosticProviderStatus>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        return endpoints;
    }
}
