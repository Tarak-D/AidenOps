using AIOps.Abstractions.Evaluation;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class EvaluationRunConfigurationTests
{
    [Fact]
    public void Validate_AllowsValidConfiguration()
    {
        var configuration = new EvaluationRunConfiguration(
            ExperimentId: "phase-15-baseline",
            Model: new EvaluationModelMetadata(
                "llm",
                "moonshotai/kimi-k3",
                Provider: "nvidia",
                PromptVersion: "v1"),
            DatasetName: "phase-8-incident-evaluation",
            DatasetVersion: "1.0");

        configuration.Validate();
    }

    [Fact]
    public void Validate_RejectsMissingExperimentId()
    {
        var configuration = new EvaluationRunConfiguration(
            ExperimentId: "",
            Model: new EvaluationModelMetadata(
                "llm",
                "test-model"),
            DatasetName: "test-dataset",
            DatasetVersion: "1.0");

        Assert.Throws<ArgumentException>(
            () => configuration.Validate());
    }

    [Fact]
    public void Validate_RejectsMissingDatasetName()
    {
        var configuration = new EvaluationRunConfiguration(
            ExperimentId: "test",
            Model: new EvaluationModelMetadata(
                "llm",
                "test-model"),
            DatasetName: "",
            DatasetVersion: "1.0");

        Assert.Throws<ArgumentException>(
            () => configuration.Validate());
    }

    [Fact]
    public void Validate_RejectsMissingDatasetVersion()
    {
        var configuration = new EvaluationRunConfiguration(
            ExperimentId: "test",
            Model: new EvaluationModelMetadata(
                "llm",
                "test-model"),
            DatasetName: "dataset",
            DatasetVersion: "");

        Assert.Throws<ArgumentException>(
            () => configuration.Validate());
    }

    [Fact]
    public void Validate_RejectsInvalidMaxCases()
    {
        var configuration = new EvaluationRunConfiguration(
            ExperimentId: "test",
            Model: new EvaluationModelMetadata(
                "llm",
                "test-model"),
            DatasetName: "dataset",
            DatasetVersion: "1.0",
            MaxCases: 0);

        Assert.Throws<ArgumentException>(
            () => configuration.Validate());
    }

    [Fact]
    public void Validate_RejectsInvalidModel()
    {
        var configuration = new EvaluationRunConfiguration(
            ExperimentId: "test",
            Model: new EvaluationModelMetadata(
                "invalid",
                "test-model"),
            DatasetName: "dataset",
            DatasetVersion: "1.0");

        Assert.Throws<ArgumentException>(
            () => configuration.Validate());
    }
}