using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIOps.Abstractions.Evaluation;
using AIOps.Abstractions.Time;
using Microsoft.Extensions.Options;

namespace AIOps.Orchestration.Evaluation;

public sealed record EvaluationAcceptanceDecision(
    Guid ResultId,
    DateTimeOffset EvaluatedAt,
    EvaluationAcceptanceResult Result);

public sealed class EvaluationAcceptanceService(
    EvaluationAcceptanceGate gate,
    IEvaluationAcceptanceStore acceptanceStore,
    IOptions<EvaluationAcceptancePolicyOptions> policyOptions,
    IClock clock)
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<EvaluationAcceptanceDecision> EvaluateAndPersistAsync(
        string baselineExperimentId,
        string candidateExperimentId,
        CancellationToken cancellationToken = default)
    {
        EvaluationAcceptancePolicy policy;
        try
        {
            policy = policyOptions.Value.CreatePolicy();
        }
        catch (InvalidOperationException exception)
        {
            throw new EvaluationAcceptancePolicyConfigurationException(
                "A valid server-side evaluation acceptance policy is not configured.",
                exception);
        }

        EvaluationAcceptanceResult result;
        try
        {
            result = await gate.EvaluateAsync(
                baselineExperimentId,
                candidateExperimentId,
                policy,
                cancellationToken);
        }
        catch (DbException exception)
        {
            throw new EvaluationAcceptanceDataAccessException(
                "Persisted evaluation runs could not be read.",
                exception);
        }
        catch (EvaluationStoreUnavailableException exception)
        {
            throw new EvaluationAcceptanceDataAccessException(
                "Persisted evaluation runs could not be read.",
                exception);
        }

        var evaluatedAt = clock.UtcNow;
        var resultId = Guid.NewGuid();
        var record = new EvaluationAcceptanceRecord(
            resultId,
            evaluatedAt,
            result.BaselineExperimentId,
            result.CandidateExperimentId,
            result.BaselineRunId,
            result.CandidateRunId,
            result.PolicyName,
            JsonSerializer.Serialize(
                new
                {
                    policy.Name,
                    policy.RequiredMetrics
                },
                AuditJsonOptions),
            result.Status,
            JsonSerializer.Serialize(result.Issues, AuditJsonOptions),
            JsonSerializer.Serialize(result.Metrics, AuditJsonOptions));

        await acceptanceStore.RecordAsync(record, cancellationToken);

        return new EvaluationAcceptanceDecision(
            resultId,
            evaluatedAt,
            result);
    }
}

public sealed class EvaluationAcceptanceDataAccessException : Exception
{
    public EvaluationAcceptanceDataAccessException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
