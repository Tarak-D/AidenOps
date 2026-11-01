using AIOps.Contracts.Evaluation;

namespace AIOps.Abstractions.Evaluation;

public interface IClassicalMlEvaluationGateway
{
    Task<ClassicalMlEvaluationResponse> EvaluateAsync(
        CancellationToken cancellationToken = default);
}
