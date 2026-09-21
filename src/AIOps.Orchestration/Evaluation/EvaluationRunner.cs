using System.Diagnostics;
using System.Text.Json;
using AIOps.Abstractions.Agents;
using AIOps.Abstractions.Evaluation;
using AIOps.Contracts.AgentGateway;

namespace AIOps.Orchestration.Evaluation;

public sealed class EvaluationRunner
{
    private readonly IAgentGateway _agentGateway;
    private readonly IEvaluationStore _evaluationStore;
    private readonly TimeProvider _timeProvider;

    public EvaluationRunner(
        IAgentGateway agentGateway,
        IEvaluationStore evaluationStore,
        TimeProvider? timeProvider = null)
    {
        _agentGateway = agentGateway;
        _evaluationStore = evaluationStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<EvaluationRunSummary> RunAsync(
        EvaluationDataset dataset,
        string experimentId,
        string modelType,
        string modelName,
        string? promptVersion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        if (dataset.Cases.Count == 0)
        {
            throw new ArgumentException(
                "Evaluation dataset must contain at least one case.",
                nameof(dataset));
        }

        var startedAt = _timeProvider.GetUtcNow();

        var caseResults = new List<EvaluationCaseResult>(
            dataset.Cases.Count);

        foreach (var evaluationCase in dataset.Cases)
        {
            ct.ThrowIfCancellationRequested();

            var result = await EvaluateCaseAsync(
                evaluationCase,
                ct);

            caseResults.Add(result);
        }

        var finishedAt = _timeProvider.GetUtcNow();

        var metrics = EvaluationMetrics.Calculate(caseResults);

        var persistedMetrics = new
        {
            metrics.SampleCount,
            metrics.TriageAccuracy,
            metrics.DomainClassificationAccuracy,
            metrics.SeverityClassificationAccuracy,
            metrics.ToolSelectionAccuracy,
            metrics.ToolArgumentValidity,
            metrics.ApprovalRate,
            metrics.ExecutionSuccessRate,
            metrics.ResolutionRate,
            metrics.EscalationRate,
            metrics.TotalLatencyMs,
            metrics.AverageLatencyMs,
            metrics.PromptTokens,
            metrics.CompletionTokens,
            metrics.RetrievalRelevance,
            Cases = caseResults
        };

        var config = new
        {
            dataset.Name,
            dataset.Version,
            ExecutionMode = "deterministic-agent-gateway",
            ExecutionNote =
                "Approval-required cases are resumed with a synthetic successful tool result. No real external tool is executed by the evaluation runner."
        };

        var run = new ExperimentRunRecord(
            Guid.NewGuid(),
            experimentId,
            modelType,
            modelName,
            promptVersion,
            dataset.Name,
            dataset.Version,
            JsonSerializer.Serialize(config),
            dataset.Cases.Count,
            startedAt,
            finishedAt,
            JsonSerializer.Serialize(persistedMetrics),
            "Phase 8 deterministic agent evaluation.");

        await _evaluationStore.RecordRunAsync(run, ct);

        return new EvaluationRunSummary(
            run,
            caseResults,
            metrics);
    }

    private async Task<EvaluationCaseResult> EvaluateCaseAsync(
        EvaluationCase evaluationCase,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();

        var request = new AgentRunRequest(
            correlationId,
            evaluationCase.Ticket,
            evaluationCase.AllowedTools,
            evaluationCase.TriageConfidenceThreshold,
            evaluationCase.MaxAttempts);

        var stopwatch = Stopwatch.StartNew();

        var initialResult =
            await _agentGateway.StartRunAsync(
                request,
                ct);

        AgentRunResult finalResult = initialResult;

        bool? executionSucceeded = initialResult.Proposal is null
            ? (bool?)null
            : initialResult.Outcome == AgentRunOutcome.Resolved;

        if (initialResult.Outcome == AgentRunOutcome.AwaitingApproval)
        {
            var resumeRequest = new ResumeAgentRunRequest(
                correlationId,
                evaluationCase.Ticket.TicketId,
                ApprovalGranted: true,
                ApprovalDecidedBy: "evaluation",
                ToolResultJson: "{}",
                ToolExecutionSucceeded: true);

            finalResult =
                await _agentGateway.ResumeRunAsync(
                    resumeRequest,
                    ct);

            executionSucceeded =
                finalResult.Outcome == AgentRunOutcome.Resolved;
        }

        stopwatch.Stop();

        var predictedDomain = initialResult.Domain;
        var predictedSeverity = initialResult.Severity;
        var predictedTool = initialResult.Proposal?.ToolName;

        var domainCorrect =
            predictedDomain == evaluationCase.ExpectedDomain;

        var severityCorrect =
            predictedSeverity == evaluationCase.ExpectedSeverity;

        var triageCorrect =
            domainCorrect && severityCorrect;

        var toolSelectionCorrect =
            string.Equals(
                predictedTool,
                evaluationCase.ExpectedToolName,
                StringComparison.Ordinal);

        bool? toolArgumentsValid = null;

        if (initialResult.Proposal is not null)
        {
            toolArgumentsValid =
                IsValidJsonObject(
                    initialResult.Proposal.ArgumentsJson);
        }

        var trace = initialResult.Trace
            .Concat(finalResult.Trace)
            .ToArray();

        return new EvaluationCaseResult(
            evaluationCase.Id,
            evaluationCase.Ticket.TicketId,
            correlationId,
            evaluationCase.ExpectedDomain,
            predictedDomain,
            domainCorrect,
            evaluationCase.ExpectedSeverity,
            predictedSeverity,
            severityCorrect,
            triageCorrect,
            evaluationCase.ExpectedToolName,
            predictedTool,
            toolSelectionCorrect,
            toolArgumentsValid,
            initialResult.Outcome,
            finalResult.Outcome,
            initialResult.Outcome == AgentRunOutcome.AwaitingApproval,
            executionSucceeded,
            finalResult.Outcome == AgentRunOutcome.Resolved,
            finalResult.Outcome == AgentRunOutcome.Escalated,
            stopwatch.Elapsed.TotalMilliseconds,
            trace.Sum(t => t.TokensPrompt),
            trace.Sum(t => t.TokensCompletion),
            null,
            trace);
    }

    private static bool IsValidJsonObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.ValueKind ==
                   JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}