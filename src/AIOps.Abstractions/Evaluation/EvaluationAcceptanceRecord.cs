namespace AIOps.Abstractions.Evaluation;

public sealed record EvaluationAcceptanceRecord(
    Guid Id,
    DateTimeOffset EvaluatedAt,
    string BaselineExperimentId,
    string CandidateExperimentId,
    Guid? BaselineRunId,
    Guid? CandidateRunId,
    string PolicyName,
    string PolicyJson,
    EvaluationAcceptanceStatus Status,
    string IssuesJson,
    string MetricsJson);

public interface IEvaluationAcceptanceStore
{
    Task RecordAsync(
        EvaluationAcceptanceRecord result,
        CancellationToken cancellationToken = default);
}

public sealed class EvaluationAcceptancePersistenceException : Exception
{
    public EvaluationAcceptancePersistenceException(string message)
        : base(message)
    {
    }

    public EvaluationAcceptancePersistenceException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
