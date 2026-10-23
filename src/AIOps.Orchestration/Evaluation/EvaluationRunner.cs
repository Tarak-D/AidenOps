
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

        IReadOnlyList<EvaluationCase> selectedCases =
            configuration?.MaxCases is int maxCases
                ? dataset.Cases.Take(maxCases).ToArray()
                : dataset.Cases;

        var startedAt = _timeProvider.GetUtcNow();

        var caseResults = new List<EvaluationCaseResult>(
            selectedCases.Count);

        foreach (var evaluationCase in selectedCases)
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
            configuration,
            selectedCases.Count);

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
            selectedCases.Count,
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
        EvaluationRunConfiguration? configuration,
        int sampleCount)
    {
        if (configuration is null)
        {
            return new
            {
                dataset.Name,
                dataset.Version,
                SampleCount = sampleCount,
                ExecutionMode = "deterministic-agent-gateway",
                ExecutionNote =
                    "The agent gateway is evaluated without executing tools or resuming approval-required proposals."
            };
        }

        return new
        {
            dataset.Name,
            dataset.Version,
            SampleCount = sampleCount,
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
                "The agent gateway is evaluated without executing tools or resuming approval-required proposals."
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

        // The evaluation runner has no server-side approval or execution
        // authority. Leave execution outcome unknown instead of synthesizing
        // a successful tool result and reporting a fabricated resolution.
        bool? executionSucceeded = null;

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

        var trace = CombineTraces(
            initialResult.Trace,
            finalResult.Trace);

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

    private static IReadOnlyList<StepTrace> CombineTraces(
        IReadOnlyList<StepTrace> initialTrace,
        IReadOnlyList<StepTrace> finalTrace)
    {
        if (finalTrace.Count >= initialTrace.Count &&
            finalTrace.Take(initialTrace.Count).SequenceEqual(initialTrace))
        {
            return finalTrace;
        }

        return initialTrace
            .Concat(finalTrace)
            .ToArray();
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
