
using System.Text.Json;
using System.Reflection;
using AIOps.Abstractions.Agents;
using AIOps.Abstractions.Evaluation;
using AIOps.Contracts.AgentGateway;
using AIOps.Domain;
using AIOps.Infrastructure.Agents;
using AIOps.Orchestration.Evaluation;
using Moq;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class EvaluationRunnerTests
{
    [Fact]
    public async Task RunAsync_does_not_fabricate_execution_or_resume_approval()
    {
        var gateway = new Mock<IAgentGateway>(MockBehavior.Strict);
        gateway
            .Setup(x => x.StartRunAsync(
                It.IsAny<AgentRunRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult(
                AgentRunOutcome.AwaitingApproval,
                0.9,
                TicketDomain.Infrastructure,
                Severity.P1,
                new ToolProposal("Example.Tool", "{}", 0.9, "Test proposal."),
                null,
                null,
                Array.Empty<StepTrace>(),
                null));

        var runner = new EvaluationRunner(
            gateway.Object,
            new Mock<IEvaluationStore>().Object);

        var result = await runner.RunAsync(
            Phase8EvaluationDataset.Create(),
            "phase-15-no-synthetic-execution",
            "llm",
            "test-model",
            "test-prompt");

        Assert.All(result.Cases, caseResult =>
        {
            Assert.Equal(AgentRunOutcome.AwaitingApproval, caseResult.FinalOutcome);
            Assert.Null(caseResult.ExecutionSucceeded);
        });

        gateway.Verify(x => x.ResumeRunAsync(
            It.IsAny<ResumeAgentRunRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_calculates_repeatable_phase8_metrics()
    {
        var gateway = new InProcessFakeAgentGateway();
        var store = new Mock<IEvaluationStore>();
        var runner = new EvaluationRunner(gateway, store.Object);

        var first = await runner.RunAsync(
            Phase8EvaluationDataset.Create(),
            "phase-8-repeatability-1",
            "llm",
            "deterministic-fake",
            "v0-fake");

        var second = await runner.RunAsync(
            Phase8EvaluationDataset.Create(),
            "phase-8-repeatability-2",
            "llm",
            "deterministic-fake",
            "v0-fake");

        Assert.Equal(first.Metrics.SampleCount, second.Metrics.SampleCount);
        Assert.Equal(first.Metrics.TriageAccuracy, second.Metrics.TriageAccuracy);
        Assert.Equal(first.Metrics.DecisionAccuracy, second.Metrics.DecisionAccuracy);
        Assert.Equal(
            first.Metrics.DomainClassificationAccuracy,
            second.Metrics.DomainClassificationAccuracy);
        Assert.Equal(
            first.Metrics.SeverityClassificationAccuracy,
            second.Metrics.SeverityClassificationAccuracy);
        Assert.Equal(
            first.Metrics.ToolSelectionAccuracy,
            second.Metrics.ToolSelectionAccuracy);
        Assert.Equal(
            first.Metrics.ToolArgumentValidity,
            second.Metrics.ToolArgumentValidity);
        Assert.Equal(first.Metrics.ApprovalRate, second.Metrics.ApprovalRate);
        Assert.Equal(
            first.Metrics.ApprovalPolicyAccuracy,
            second.Metrics.ApprovalPolicyAccuracy);
        Assert.Equal(
            first.Metrics.ExecutionSuccessRate,
            second.Metrics.ExecutionSuccessRate);
        Assert.Equal(first.Metrics.ResolutionRate, second.Metrics.ResolutionRate);
        Assert.Equal(first.Metrics.EscalationRate, second.Metrics.EscalationRate);
        Assert.Equal(first.Metrics.PromptTokens, second.Metrics.PromptTokens);
        Assert.Equal(first.Metrics.CompletionTokens, second.Metrics.CompletionTokens);

        store.Verify(
            s => s.RecordRunAsync(
                It.IsAny<ExperimentRunRecord>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_persists_experiment_run()
    {
        var gateway = new InProcessFakeAgentGateway();
        var store = new Mock<IEvaluationStore>();
        var runner = new EvaluationRunner(gateway, store.Object);

        var result = await runner.RunAsync(
            Phase8EvaluationDataset.Create(),
            "phase-8-test",
            "llm",
            "deterministic-fake",
            "v0-fake");

        Assert.Equal(7, result.Run.SampleCount);
        Assert.Equal(Phase8EvaluationDataset.Name, result.Run.DatasetName);
        Assert.Equal(Phase8EvaluationDataset.Version, result.Run.DatasetVersion);
        Assert.NotEmpty(result.Run.MetricsJson);
        Assert.NotEmpty(result.Run.ConfigJson);

        using var metricsDocument = JsonDocument.Parse(result.Run.MetricsJson);

        Assert.True(
            metricsDocument.RootElement.TryGetProperty(
                "ApprovalPolicyAccuracy",
                out var approvalPolicyAccuracy));

        Assert.Equal(
            result.Metrics.ApprovalPolicyAccuracy,
            approvalPolicyAccuracy.GetDouble());

        Assert.True(
            metricsDocument.RootElement.TryGetProperty(
                "DecisionAccuracy",
                out var decisionAccuracy));

        Assert.Equal(
            result.Metrics.DecisionAccuracy,
            decisionAccuracy.GetDouble());

        store.Verify(
            s => s.RecordRunAsync(
                It.Is<ExperimentRunRecord>(r =>
                    r.ExperimentId == "phase-8-test" &&
                    r.SampleCount == 7 &&
                    r.DatasetName == Phase8EvaluationDataset.Name),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_with_configuration_respects_max_cases_and_persists_evaluated_count()
    {
        var dataset = Phase8EvaluationDataset.Create();
        var gateway = new Mock<IAgentGateway>(MockBehavior.Strict);
        gateway
            .Setup(x => x.StartRunAsync(
                It.IsAny<AgentRunRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult(
                AgentRunOutcome.Resolved,
                0.9,
                TicketDomain.Infrastructure,
                Severity.P1,
                null,
                null,
                "Test resolution.",
                Array.Empty<StepTrace>(),
                null));

        var runner = new EvaluationRunner(
            gateway.Object,
            new Mock<IEvaluationStore>().Object);
        var configuration = new EvaluationRunConfiguration(
            "phase-15-max-cases",
            new EvaluationModelMetadata("llm", "test-model"),
            dataset.Name,
            dataset.Version,
            MaxCases: 2);

        var result = await runner.RunAsync(dataset, configuration);

        Assert.Equal(2, result.Cases.Count);
        Assert.Equal(2, result.Metrics.SampleCount);
        Assert.Equal(2, result.Run.SampleCount);

        using var metricsDocument = JsonDocument.Parse(result.Run.MetricsJson);
        Assert.Equal(
            2,
            metricsDocument.RootElement.GetProperty("SampleCount").GetInt32());

        using var configDocument = JsonDocument.Parse(result.Run.ConfigJson);
        Assert.Equal(
            2,
            configDocument.RootElement.GetProperty("SampleCount").GetInt32());
        Assert.Equal(
            2,
            configDocument.RootElement.GetProperty("MaxCases").GetInt32());

        gateway.Verify(
            x => x.StartRunAsync(
                It.IsAny<AgentRunRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_does_not_duplicate_trace_entries_or_token_usage()
    {
        var dataset = Phase8EvaluationDataset.Create();
        var evaluationCase = dataset.Cases[0];
        var trace = new StepTrace(
            "EvaluationTestAgent",
            "triage",
            "test-model",
            "test-prompt",
            11,
            7,
            1.0,
            "Evaluation trace.");
        var gateway = new Mock<IAgentGateway>(MockBehavior.Strict);
        gateway
            .Setup(x => x.StartRunAsync(
                It.IsAny<AgentRunRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult(
                AgentRunOutcome.Resolved,
                0.9,
                evaluationCase.ExpectedDomain,
                evaluationCase.ExpectedSeverity,
                null,
                null,
                "Test resolution.",
                new[] { trace },
                null));

        var runner = new EvaluationRunner(
            gateway.Object,
            new Mock<IEvaluationStore>().Object);
        var singleCaseDataset = new EvaluationDataset(
            dataset.Name,
            dataset.Version,
            new[] { evaluationCase });

        var result = await runner.RunAsync(
            singleCaseDataset,
            "phase-15-trace-count",
            "llm",
            "test-model",
            "test-prompt");

        var caseResult = Assert.Single(result.Cases);
        Assert.Equal(new[] { trace }, caseResult.Trace);
        Assert.Equal(11, caseResult.PromptTokens);
        Assert.Equal(7, caseResult.CompletionTokens);
        Assert.Equal(11, result.Metrics.PromptTokens);
        Assert.Equal(7, result.Metrics.CompletionTokens);

        using var metricsDocument = JsonDocument.Parse(result.Run.MetricsJson);
        Assert.Equal(
            11,
            metricsDocument.RootElement.GetProperty("PromptTokens").GetInt32());
        Assert.Equal(
            7,
            metricsDocument.RootElement.GetProperty("CompletionTokens").GetInt32());
    }

    [Fact]
    public void CombineTraces_preserves_additional_events_without_double_counting_tokens()
    {
        var initialEvent = new StepTrace(
            "EvaluationTestAgent",
            "triage",
            "test-model",
            "test-prompt",
            11,
            7,
            1.0,
            "Initial evaluation trace.");
        var additionalEvent = new StepTrace(
            "EvaluationTestAgent",
            "verify",
            "test-model",
            "test-prompt",
            5,
            3,
            1.0,
            "Additional final trace.");
        var initialTrace = new[] { initialEvent };
        var finalTrace = new[] { initialEvent, additionalEvent };

        var combineTraces = typeof(EvaluationRunner).GetMethod(
            "CombineTraces",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(combineTraces);
        var mergedTrace = Assert.IsAssignableFrom<IReadOnlyList<StepTrace>>(
            combineTraces.Invoke(null, [initialTrace, finalTrace]));

        Assert.Equal(2, mergedTrace.Count);
        Assert.Equal(1, mergedTrace.Count(trace => trace == initialEvent));
        Assert.Contains(additionalEvent, mergedTrace);
        Assert.Equal(16, mergedTrace.Sum(trace => trace.TokensPrompt));
        Assert.Equal(10, mergedTrace.Sum(trace => trace.TokensCompletion));
    }
}
