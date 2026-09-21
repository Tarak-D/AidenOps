using AIOps.Abstractions.Agents;
using AIOps.Abstractions.Evaluation;
using AIOps.Contracts.AgentGateway;
using AIOps.Infrastructure.Agents;
using AIOps.Orchestration.Evaluation;
using Moq;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class EvaluationRunnerTests
{
    [Fact]
    public async Task RunAsync_calculates_repeatable_phase8_metrics()
    {
        var gateway = new InProcessFakeAgentGateway();

        var store = new Mock<IEvaluationStore>();

        EvaluationRunSummary first;
        EvaluationRunSummary second;

        var runner = new EvaluationRunner(gateway, store.Object);

        first = await runner.RunAsync(
            Phase8EvaluationDataset.Create(),
            "phase-8-repeatability-1",
            "llm",
            "deterministic-fake",
            "v0-fake");

        second = await runner.RunAsync(
            Phase8EvaluationDataset.Create(),
            "phase-8-repeatability-2",
            "llm",
            "deterministic-fake",
            "v0-fake");

        Assert.Equal(first.Metrics.SampleCount, second.Metrics.SampleCount);
        Assert.Equal(
            first.Metrics.TriageAccuracy,
            second.Metrics.TriageAccuracy);

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

        Assert.Equal(
            first.Metrics.ApprovalRate,
            second.Metrics.ApprovalRate);

        Assert.Equal(
            first.Metrics.ExecutionSuccessRate,
            second.Metrics.ExecutionSuccessRate);

        Assert.Equal(
            first.Metrics.ResolutionRate,
            second.Metrics.ResolutionRate);

        Assert.Equal(
            first.Metrics.EscalationRate,
            second.Metrics.EscalationRate);

        Assert.Equal(
            first.Metrics.PromptTokens,
            second.Metrics.PromptTokens);

        Assert.Equal(
            first.Metrics.CompletionTokens,
            second.Metrics.CompletionTokens);

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
        Assert.Equal(
            Phase8EvaluationDataset.Name,
            result.Run.DatasetName);

        Assert.Equal(
            Phase8EvaluationDataset.Version,
            result.Run.DatasetVersion);

        Assert.NotEmpty(result.Run.MetricsJson);
        Assert.NotEmpty(result.Run.ConfigJson);

        store.Verify(
            s => s.RecordRunAsync(
                It.Is<ExperimentRunRecord>(r =>
                    r.ExperimentId == "phase-8-test" &&
                    r.SampleCount == 7 &&
                    r.DatasetName == Phase8EvaluationDataset.Name),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}