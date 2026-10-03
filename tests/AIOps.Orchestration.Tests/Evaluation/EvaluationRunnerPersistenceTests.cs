using System.Text.Json;
using AIOps.Abstractions.Agents;
using AIOps.Abstractions.Evaluation;
using AIOps.Contracts.AgentGateway;
using AIOps.Domain;
using AIOps.Orchestration.Evaluation;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class EvaluationRunnerPersistenceTests
{
    [Fact]
    public async Task RunAsync_WithConfiguration_PersistsModelAndExecutionMetadata()
    {
        // Arrange
        var dataset = Phase8EvaluationDataset.Create();

        var gateway = new FakeAgentGateway();
        var store = new FakeEvaluationStore();

        var runner = new EvaluationRunner(
            gateway,
            store);

        var configuration = new EvaluationRunConfiguration(
            ExperimentId: "phase-15-1-persistence-test",
            Model: new EvaluationModelMetadata(
                ModelType: "llm",
                ModelName: "moonshotai/kimi-k3",
                Provider: "nvidia",
                ModelVersion: "test-version",
                PromptVersion: "prompt-v15.1"),
            DatasetName: dataset.Name,
            DatasetVersion: dataset.Version,
            MaxCases: 1,
            Deterministic: true,
            ExecuteExternalTools: false,
            Notes: "Phase 15.1 persistence test.");

        // Use a single case so this test only verifies persistence.
        var singleCaseDataset = new EvaluationDataset(
            dataset.Name,
            dataset.Version,
            new[] { dataset.Cases[0] });

        // Act
        var summary = await runner.RunAsync(
            singleCaseDataset,
            configuration);

        // Assert
        Assert.NotNull(summary);
        Assert.NotNull(store.RecordedRun);

        var run = store.RecordedRun!;

        Assert.Equal(
            "phase-15-1-persistence-test",
            run.ExperimentId);

        Assert.Equal(
            "llm",
            run.ModelType);

        Assert.Equal(
            "moonshotai/kimi-k3",
            run.ModelName);

        Assert.Equal(
            "prompt-v15.1",
            run.PromptVersion);

        Assert.Equal(
            dataset.Name,
            run.DatasetName);

        Assert.Equal(
            dataset.Version,
            run.DatasetVersion);

        Assert.Equal(
            1,
            run.SampleCount);

        Assert.Equal(
            "Phase 15.1 persistence test.",
            run.Notes);

        using var document =
            JsonDocument.Parse(run.ConfigJson);

        var root = document.RootElement;

        Assert.Equal(
            dataset.Name,
            root.GetProperty("Name").GetString());

        Assert.Equal(
            dataset.Version,
            root.GetProperty("Version").GetString());

        Assert.Equal(
            "deterministic-agent-gateway",
            root.GetProperty("ExecutionMode").GetString());

        var model =
            root.GetProperty("Model");

        Assert.Equal(
            "llm",
            model.GetProperty("Type").GetString());

        Assert.Equal(
            "moonshotai/kimi-k3",
            model.GetProperty("Name").GetString());

        Assert.Equal(
            "nvidia",
            model.GetProperty("Provider").GetString());

        Assert.Equal(
            "test-version",
            model.GetProperty("ModelVersion").GetString());

        Assert.Equal(
            "prompt-v15.1",
            model.GetProperty("PromptVersion").GetString());

        Assert.Equal(
            1,
            root.GetProperty("MaxCases").GetInt32());

        Assert.True(
            root.GetProperty("Deterministic").GetBoolean());

        Assert.False(
            root.GetProperty("ExecuteExternalTools").GetBoolean());

        Assert.Equal(
            "Phase 15.1 persistence test.",
            root.GetProperty("Notes").GetString());

        Assert.Contains(
            "External tool execution was disabled",
            root.GetProperty("ExecutionNote").GetString());
    }

    private sealed class FakeAgentGateway : IAgentGateway
    {
        public Task<AgentRunResult> StartRunAsync(
            AgentRunRequest request,
            CancellationToken ct = default)
        {
            var trace = new[]
            {
                new StepTrace(
                    "EvaluationTestAgent",
                    "test",
                    "test-model",
                    "test-prompt",
                    10,
                    5,
                    1.0,
                    "Deterministic evaluation test.")
            };

            return Task.FromResult(
                new AgentRunResult(
                    AgentRunOutcome.Resolved,
                    1.0,
                    TicketDomain.Infrastructure,
                    Severity.P1,
                    null,
                    null,
                    "Test resolution.",
                    trace,
                    null));
        }

        public Task<AgentRunResult> ResumeRunAsync(
            ResumeAgentRunRequest request,
            CancellationToken ct = default)
        {
            throw new InvalidOperationException(
                "ResumeRunAsync should not be called by this persistence test.");
        }
    }

    private sealed class FakeEvaluationStore : IEvaluationStore
    {
        public ExperimentRunRecord? RecordedRun { get; private set; }

        public Task RecordRunAsync(
            ExperimentRunRecord run,
            CancellationToken ct = default)
        {
            RecordedRun = run;

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ExperimentRunRecord>> ListRunsAsync(
            string? experimentId = null,
            string? datasetName = null,
            CancellationToken ct = default)
        {
            IReadOnlyList<ExperimentRunRecord> result =
                RecordedRun is null
                    ? Array.Empty<ExperimentRunRecord>()
                    : new[] { RecordedRun };

            return Task.FromResult(result);
        }
    }
}