using AIOps.Abstractions.Evaluation;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class EvaluationModelMetadataTests
{
    [Fact]
    public void Validate_AllowsClassicalMlModel()
    {
        var model = new EvaluationModelMetadata(
            "classical_ml",
            "tfidf-logreg-v1");

        model.Validate();

        Assert.True(model.IsClassicalMl);
        Assert.False(model.IsLlm);
    }

    [Fact]
    public void Validate_AllowsLlmModel()
    {
        var model = new EvaluationModelMetadata(
            "llm",
            "moonshotai/kimi-k3",
            Provider: "nvidia",
            PromptVersion: "v1");

        model.Validate();

        Assert.True(model.IsLlm);
        Assert.False(model.IsClassicalMl);
    }

    [Fact]
    public void Validate_RejectsMissingModelType()
    {
        var model = new EvaluationModelMetadata(
            "",
            "test-model");

        Assert.Throws<ArgumentException>(() => model.Validate());
    }

    [Fact]
    public void Validate_RejectsMissingModelName()
    {
        var model = new EvaluationModelMetadata(
            "llm",
            "");

        Assert.Throws<ArgumentException>(() => model.Validate());
    }

    [Fact]
    public void Validate_RejectsUnsupportedModelType()
    {
        var model = new EvaluationModelMetadata(
            "unknown",
            "test-model");

        Assert.Throws<ArgumentException>(() => model.Validate());
    }
}