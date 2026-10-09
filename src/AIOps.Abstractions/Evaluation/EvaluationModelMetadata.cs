namespace AIOps.Abstractions.Evaluation;

public sealed record EvaluationModelMetadata(
    string ModelType,
    string ModelName,
    string? Provider = null,
    string? ModelVersion = null,
    string? PromptVersion = null,
    decimal? InputPricePerMillionTokens = null,
    decimal? OutputPricePerMillionTokens = null)
{
    public bool IsClassicalMl =>
        string.Equals(
            ModelType,
            "classical_ml",
            StringComparison.OrdinalIgnoreCase);

    public bool IsLlm =>
        string.Equals(
            ModelType,
            "llm",
            StringComparison.OrdinalIgnoreCase);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ModelType))
        {
            throw new ArgumentException(
                "Model type is required.",
                nameof(ModelType));
        }

        if (string.IsNullOrWhiteSpace(ModelName))
        {
            throw new ArgumentException(
                "Model name is required.",
                nameof(ModelName));
        }

        if (!IsClassicalMl && !IsLlm)
        {
            throw new ArgumentException(
                $"Unsupported evaluation model type '{ModelType}'. " +
                "Expected 'classical_ml' or 'llm'.",
                nameof(ModelType));
        }

        if (InputPricePerMillionTokens is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(InputPricePerMillionTokens),
                "Input token price cannot be negative.");
        }

        if (OutputPricePerMillionTokens is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(OutputPricePerMillionTokens),
                "Output token price cannot be negative.");
        }

        if (InputPricePerMillionTokens.HasValue !=
            OutputPricePerMillionTokens.HasValue)
        {
            throw new ArgumentException(
                "Both input and output token prices must be configured together.");
        }

        if (!IsLlm &&
            (InputPricePerMillionTokens.HasValue ||
             OutputPricePerMillionTokens.HasValue))
        {
            throw new ArgumentException(
                "Token pricing can only be configured for LLM models.");
        }
    }
}