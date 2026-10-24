
using AIOps.Host.Security;
using AIOps.Contracts.Evaluation;
using AIOps.Orchestration.Evaluation;

namespace AIOps.Host.Api;

/// <summary>
/// Evaluation endpoints for comparing runs and recording acceptance decisions.
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

        group.MapPost(
                "/acceptance",
                async (
                    EvaluationAcceptanceRequest? request,
                    EvaluationAcceptanceService service,
                    ILogger<Program> logger,
                    CancellationToken ct) =>
                {
                    if (request is null)
                    {
                        return Results.ValidationProblem(
                            new Dictionary<string, string[]>
                            {
                                ["request"] = ["A request body is required."]
                            });
                    }

                    var errors = ValidateAcceptanceRequest(request);
                    if (errors.Count > 0)
                    {
                        return Results.ValidationProblem(errors);
                    }

                    try
                    {
                        var decision = await service.EvaluateAndPersistAsync(
                            request.BaselineExperimentId!,
                            request.CandidateExperimentId!,
                            ct);

                        var result = decision.Result;
                        return Results.Ok(new EvaluationAcceptanceResponse(
                            decision.ResultId,
                            decision.EvaluatedAt,
                            result.BaselineExperimentId,
                            result.CandidateExperimentId,
                            result.BaselineRunId,
                            result.CandidateRunId,
                            result.PolicyName,
                            result.Status.ToString(),
                            result.Issues.Select(issue =>
                                new EvaluationAcceptanceIssueDto(
                                    issue.Code.ToString(),
                                    issue.Message,
                                    issue.MetricName)).ToArray(),
                            result.Metrics.Select(metric =>
                                new EvaluationMetricAcceptanceDto(
                                    metric.MetricName,
                                    metric.Direction.ToString(),
                                    metric.BaselineValue,
                                    metric.CandidateValue,
                                    metric.Regression,
                                    metric.AllowedRegression,
                                    metric.ThresholdViolation,
                                    metric.Status.ToString())).ToArray()));
                    }
                    catch (EvaluationAcceptancePolicyConfigurationException exception)
                    {
                        logger.LogError(
                            exception,
                            "Evaluation acceptance policy is unavailable.");
                        return Results.Problem(
                            title: "Evaluation acceptance policy unavailable",
                            detail: "A valid server-side acceptance policy is not configured.",
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                    catch (EvaluationAcceptanceDataAccessException exception)
                    {
                        logger.LogError(
                            exception,
                            "Evaluation runs could not be read for baseline {BaselineExperimentId} and candidate {CandidateExperimentId}.",
                            request.BaselineExperimentId,
                            request.CandidateExperimentId);
                        return Results.Problem(
                            title: "Evaluation data unavailable",
                            detail: "Persisted evaluation runs could not be read.",
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                    catch (AIOps.Abstractions.Evaluation.EvaluationAcceptancePersistenceException exception)
                    {
                        logger.LogError(
                            exception,
                            "Acceptance result could not be persisted for baseline {BaselineExperimentId} and candidate {CandidateExperimentId}.",
                            request.BaselineExperimentId,
                            request.CandidateExperimentId);
                        return Results.Problem(
                            title: "Acceptance result could not be persisted",
                            detail: "The acceptance decision was not saved; no successful result is available.",
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                })
            .WithName("EvaluateEvaluationAcceptance")
            .Produces<EvaluationAcceptanceResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static Dictionary<string, string[]> ValidateAcceptanceRequest(
        EvaluationAcceptanceRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateExperimentId(
            request.BaselineExperimentId,
            "baselineExperimentId",
            errors);
        ValidateExperimentId(
            request.CandidateExperimentId,
            "candidateExperimentId",
            errors);
        return errors;
    }

    private static void ValidateExperimentId(
        string? value,
        string fieldName,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[fieldName] = ["An experiment ID is required."];
        }
        else if (value.Length > 200)
        {
            errors[fieldName] = ["An experiment ID cannot exceed 200 characters."];
        }
        else if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            errors[fieldName] = ["An experiment ID cannot have surrounding whitespace."];
        }
    }
}