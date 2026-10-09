
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

    public Task<EvaluationRunSummary> RunAsync(
        EvaluationDataset dataset,
        EvaluationRunConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.Validate();

        return RunAsyncCore(
            dataset,
            configuration.ExperimentId,
            configuration.Model.ModelType,
            configuration.Model.ModelName,
            configuration.Model.PromptVersion,
            configuration,
            cancellationToken);
    }

    public Task<EvaluationRunSummary> RunAsync(
        EvaluationDataset dataset,
        string experimentId,
        string modelType,
        string modelName,
        string? promptVersion,
        CancellationToken ct = default)
    {
        return RunAsyncCore(
            dataset,
            experimentId,
            modelType,
            modelName,
            promptVersion,
            configuration: null,
            ct);
    }

    private async Task<EvaluationRunSummary> RunAsyncCore(
        EvaluationDataset dataset,
        string experimentId,
        string modelType,
        string modelName,
        string? promptVersion,
        EvaluationRunConfiguration? configuration,
        CancellationToken ct)
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
            metrics.DecisionAccuracy,
            metrics.DomainClassificationAccuracy,
            metrics.SeverityClassificationAccuracy,
            metrics.ToolSelectionAccuracy,
            metrics.ToolArgumentValidity,
            metrics.ApprovalRate,
            metrics.ApprovalPolicyAccuracy,
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

        var config = BuildPersistedConfiguration(
            dataset,
            modelType,
            modelName,
            promptVersion,
            configuration);

        var notes = configuration?.Notes
            ?? "Phase 15.1 evaluation foundation.";

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
            notes);

        await _evaluationStore.RecordRunAsync(run, ct);

        return new EvaluationRunSummary(
            run,
            caseResults,
            metrics);
    }

    private static object BuildPersistedConfiguration(
        EvaluationDataset dataset,
        string modelType,
        string modelName,
        string? promptVersion,
        EvaluationRunConfiguration? configuration)
    {
        if (configuration is null)
        {
            return new
            {
                dataset.Name,
                dataset.Version,
                ExecutionMode = "deterministic-agent-gateway",
                ExecutionNote =
                    "Approval-required cases are resumed with a synthetic successful tool result. No real external tool is executed by the evaluation runner."
            };
        }

        return new
        {
            dataset.Name,
            dataset.Version,
            ExecutionMode = configuration.Deterministic
                ? "deterministic-agent-gateway"
                : "agent-gateway",
            Model = new
            {
                Type = modelType,
                Name = modelName,
                configuration.Model.Provider,
                configuration.Model.ModelVersion,
                PromptVersion = promptVersion
            },
            configuration.MaxCases,
            configuration.Deterministic,
            configuration.ExecuteExternalTools,
            configuration.Notes,
            ExecutionNote =
                configuration.ExecuteExternalTools
                    ? "External tool execution was enabled by the evaluation configuration."
                    : "External tool execution was disabled. Approval-required cases are resumed with a synthetic successful tool result."
        };
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

        var initialResult = await _agentGateway.StartRunAsync(
            request,
            ct);

        AgentRunResult finalResult = initialResult;

        bool? executionSucceeded = initialResult.Proposal is null
            ? null
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

            finalResult = await _agentGateway.ResumeRunAsync(
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

        var decisionCorrect =
            initialResult.Outcome == evaluationCase.ExpectedInitialOutcome;

        var toolSelectionCorrect = string.Equals(
            predictedTool,
            evaluationCase.ExpectedToolName,
            StringComparison.Ordinal);

        var approvalRequired =
            initialResult.Outcome == AgentRunOutcome.AwaitingApproval;

        var approvalPolicyCorrect =
            approvalRequired == evaluationCase.ExpectedApprovalRequired;

        bool? toolArgumentsValid = null;

        if (initialResult.Proposal is not null)
        {
            toolArgumentsValid = IsValidJsonObject(
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
            decisionCorrect,
            finalResult.Outcome,
            evaluationCase.ExpectedApprovalRequired,
            approvalRequired,
            approvalPolicyCorrect,
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