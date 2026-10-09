
using AIOps.Contracts.AgentGateway;
using AIOps.Domain;

namespace AIOps.Abstractions.Evaluation;

public sealed record EvaluationCaseResult(
    string CaseId,
    Guid TicketId,
    Guid CorrelationId,
    TicketDomain ExpectedDomain,
    TicketDomain? PredictedDomain,
    bool DomainCorrect,
    Severity ExpectedSeverity,
    Severity? PredictedSeverity,
    bool SeverityCorrect,
    bool TriageCorrect,
    string? ExpectedToolName,
    string? PredictedToolName,
    bool ToolSelectionCorrect,
    bool? ToolArgumentsValid,
    AgentRunOutcome InitialOutcome,
    bool DecisionCorrect,
    AgentRunOutcome FinalOutcome,
    bool ExpectedApprovalRequired,
    bool ApprovalRequired,
    bool ApprovalPolicyCorrect,
    bool? ExecutionSucceeded,
    bool Resolved,
    bool Escalated,
    double LatencyMs,
    int PromptTokens,
    int CompletionTokens,
    double? RetrievalRelevance,
    IReadOnlyList<StepTrace> Trace);

public sealed record EvaluationMetrics(
    int SampleCount,
    double TriageAccuracy,
    double DecisionAccuracy,
    double DomainClassificationAccuracy,
    double SeverityClassificationAccuracy,
    double ToolSelectionAccuracy,
    double? ToolArgumentValidity,
    double ApprovalRate,
    double ApprovalPolicyAccuracy,
    double? ExecutionSuccessRate,
    double ResolutionRate,
    double EscalationRate,
    double TotalLatencyMs,
    double AverageLatencyMs,
    int PromptTokens,
    int CompletionTokens,
    double? RetrievalRelevance)
{
    public static EvaluationMetrics Calculate(
        IReadOnlyList<EvaluationCaseResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        if (results.Count == 0)
        {
            return new EvaluationMetrics(
                0,
                0,
                0,
                0,
                0,
                0,
                null,
                0,
                0,
                null,
                0,
                0,
                0,
                0,
                0,
                0,
                null);
        }

        var sampleCount = results.Count;

        var domainAccuracy =
            results.Count(r => r.DomainCorrect) / (double)sampleCount;

        var severityAccuracy =
            results.Count(r => r.SeverityCorrect) / (double)sampleCount;

        var triageAccuracy =
            results.Count(r => r.TriageCorrect) / (double)sampleCount;

        var decisionAccuracy =
            results.Count(r => r.DecisionCorrect) / (double)sampleCount;

        var toolSelectionAccuracy =
            results.Count(r => r.ToolSelectionCorrect) / (double)sampleCount;

        var argumentResults = results
            .Where(r => r.ToolArgumentsValid.HasValue)
            .Select(r => r.ToolArgumentsValid!.Value)
            .ToArray();

        double? toolArgumentValidity = argumentResults.Length == 0
            ? null
            : argumentResults.Count(v => v) / (double)argumentResults.Length;

        var approvalRate =
            results.Count(r => r.ApprovalRequired) / (double)sampleCount;

        var approvalPolicyAccuracy =
            results.Count(r => r.ApprovalPolicyCorrect) / (double)sampleCount;

        var executionResults = results
            .Where(r => r.ExecutionSucceeded.HasValue)
            .Select(r => r.ExecutionSucceeded!.Value)
            .ToArray();

        double? executionSuccessRate = executionResults.Length == 0
            ? null
            : executionResults.Count(v => v) / (double)executionResults.Length;

        var resolutionRate =
            results.Count(r => r.Resolved) / (double)sampleCount;

        var escalationRate =
            results.Count(r => r.Escalated) / (double)sampleCount;

        var totalLatency = results.Sum(r => r.LatencyMs);

        var retrievalResults = results
            .Where(r => r.RetrievalRelevance.HasValue)
            .Select(r => r.RetrievalRelevance!.Value)
            .ToArray();

        double? retrievalRelevance = retrievalResults.Length == 0
            ? null
            : retrievalResults.Average();

        return new EvaluationMetrics(
            sampleCount,
            triageAccuracy,
            decisionAccuracy,
            domainAccuracy,
            severityAccuracy,
            toolSelectionAccuracy,
            toolArgumentValidity,
            approvalRate,
            approvalPolicyAccuracy,
            executionSuccessRate,
            resolutionRate,
            escalationRate,
            totalLatency,
            totalLatency / sampleCount,
            results.Sum(r => r.PromptTokens),
            results.Sum(r => r.CompletionTokens),
            retrievalRelevance);
    }
}

public sealed record EvaluationRunSummary(
    ExperimentRunRecord Run,
    IReadOnlyList<EvaluationCaseResult> Cases,
    EvaluationMetrics Metrics);