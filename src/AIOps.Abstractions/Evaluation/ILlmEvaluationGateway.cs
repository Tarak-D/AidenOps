using AIOps.Contracts.Evaluation;

namespace AIOps.Abstractions.Evaluation;

public interface ILlmEvaluationGateway
{
    Task<LlmEvaluationResponse> EvaluateAsync(
        LlmEvaluationRequest request,
        CancellationToken cancellationToken = default);
}