
using AIOps.Host.Security;
using AIOps.Orchestration.Evaluation;

namespace AIOps.Host.Api;

/// <summary>
/// Read-only evaluation endpoints for comparing persisted experiment runs.
/// </summary>
public static class EvaluationEndpoints
{
    public static IEndpointRouteBuilder MapEvaluationApi(
        this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/evaluations")
            .WithTags("Evaluations")
            .RequireAuthorization(AuthPolicies.CanViewQueue);

        group.MapGet(
                "/comparison",
                async (
                    EvaluationComparisonService service,
                    string? experimentId,
                    string? datasetName,
                    CancellationToken ct) =>
                {
                    var report = await service.CompareAsync(
                        experimentId,
                        datasetName,
                        ct);

                    return Results.Ok(report);
                })
            .WithName("CompareEvaluationRuns")
            .Produces<EvaluationComparisonReport>(
                StatusCodes.Status200OK);

        return app;
    }
}