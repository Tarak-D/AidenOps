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

    [Fact]
    public void Validate_AllowsLlmWithBothTokenPrices()
    {
        var model = new EvaluationModelMetadata(
            "llm",
            "test-model",
            Provider: "nvidia",
            InputPricePerMillionTokens: 2m,
            OutputPricePerMillionTokens: 8m);

        model.Validate();

        Assert.Equal(2m, model.InputPricePerMillionTokens);
        Assert.Equal(8m, model.OutputPricePerMillionTokens);
    }

    [Fact]
    public void Validate_AllowsLlmWithoutTokenPrices()
    {
        var model = new EvaluationModelMetadata(
            "llm",
            "test-model");

        model.Validate();

        Assert.Null(model.InputPricePerMillionTokens);
        Assert.Null(model.OutputPricePerMillionTokens);
    }

    [Fact]
    public void Validate_RejectsNegativeInputTokenPrice()
    {
        var model = new EvaluationModelMetadata(
            "llm",
            "test-model",
            InputPricePerMillionTokens: -1m,
            OutputPricePerMillionTokens: 8m);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => model.Validate());
    }

    [Fact]
    public void Validate_RejectsNegativeOutputTokenPrice()
    {
        var model = new EvaluationModelMetadata(
            "llm",
            "test-model",
            InputPricePerMillionTokens: 2m,
            OutputPricePerMillionTokens: -1m);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => model.Validate());
    }

    [Fact]
    public void Validate_RejectsInputPriceWithoutOutputPrice()
    {
        var model = new EvaluationModelMetadata(
            "llm",
            "test-model",
            InputPricePerMillionTokens: 2m);

        Assert.Throws<ArgumentException>(
            () => model.Validate());
    }

    [Fact]
    public void Validate_RejectsOutputPriceWithoutInputPrice()
    {
        var model = new EvaluationModelMetadata(
            "llm",
            "test-model",
            OutputPricePerMillionTokens: 8m);

        Assert.Throws<ArgumentException>(
            () => model.Validate());
    }

    [Fact]
    public void Validate_RejectsTokenPricingForClassicalMl()
    {
        var model = new EvaluationModelMetadata(
            "classical_ml",
            "tfidf-logreg-v1",
            InputPricePerMillionTokens: 2m,
            OutputPricePerMillionTokens: 8m);

        Assert.Throws<ArgumentException>(
            () => model.Validate());
    }
}