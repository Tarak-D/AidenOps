using System.Text.Json;
using AIOps.Abstractions.Evaluation;
using AIOps.Contracts.Evaluation;

namespace AIOps.Orchestration.Evaluation;

public sealed class LlmEvaluationRunner
{
    private const decimal TokensPerMillion = 1_000_000m;

    private readonly ILlmEvaluationGateway _gateway;
    private readonly IEvaluationStore _evaluationStore;
    private readonly TimeProvider _timeProvider;

    public LlmEvaluationRunner(
        ILlmEvaluationGateway gateway,
        IEvaluationStore evaluationStore,
        TimeProvider? timeProvider = null)
    {
        _gateway = gateway;
        _evaluationStore = evaluationStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ExperimentRunRecord> RunAsync(
        string datasetId,
        EvaluationDataset dataset,
        EvaluationRunConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(datasetId))
        {
            throw new ArgumentException(
                "Dataset id is required.",
                nameof(datasetId));
        }

        configuration.Validate();

        if (!configuration.Model.IsLlm)
        {
            throw new ArgumentException(
                "LLM evaluation requires model type 'llm'.",
                nameof(configuration));
        }

        if (!string.Equals(
                configuration.DatasetName,
                dataset.Name,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Evaluation configuration dataset name does not match the dataset.",
                nameof(configuration));
        }

        if (!string.Equals(
                configuration.DatasetVersion,
                dataset.Version,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Evaluation configuration dataset version does not match the dataset.",
                nameof(configuration));
        }

        if (dataset.Cases.Count == 0)
        {
            throw new ArgumentException(
                "Evaluation dataset must contain at least one case.",
                nameof(dataset));
        }

        var selectedCases =
            configuration.MaxCases is int maxCases
                ? dataset.Cases.Take(maxCases).ToArray()
                : dataset.Cases.ToArray();

        var requestCases = selectedCases
            .Select(evaluationCase =>
                new LlmEvaluationCaseRequest(
                    evaluationCase.Id,
                    evaluationCase.Ticket.Title,
                    evaluationCase.Ticket.Description,
                    evaluationCase.ExpectedDomain.ToString(),
                    evaluationCase.ExpectedSeverity.ToString()))
            .ToArray();

        var request = new LlmEvaluationRequest(
            datasetId,
            dataset.Name,
            dataset.Version,
            configuration.Model.Provider
                ?? throw new ArgumentException(
                    "LLM evaluation requires a model provider.",
                    nameof(configuration)),
            requestCases);

        var startedAt = _timeProvider.GetUtcNow();

        var response = await _gateway.EvaluateAsync(
            request,
            cancellationToken);

        var finishedAt = _timeProvider.GetUtcNow();

        ValidateResponse(response, request, configuration);

        var inputPrice = configuration.Model.InputPricePerMillionTokens;
        var outputPrice = configuration.Model.OutputPricePerMillionTokens;

        decimal? estimatedCost = inputPrice.HasValue && outputPrice.HasValue
            ? CalculateEstimatedCost(
                response.Metrics.PromptTokens,
                response.Metrics.CompletionTokens,
                inputPrice.Value,
                outputPrice.Value)
            : null;

        var persistedConfiguration = new
        {
            DatasetId = datasetId,
            DatasetName = dataset.Name,
            DatasetVersion = dataset.Version,
            Model = new
            {
                Type = response.ModelType,
                Provider = response.Model.Provider,
                Name = response.Model.Name,
                ModelVersion = configuration.Model.ModelVersion,
                PromptVersion = string.IsNullOrWhiteSpace(
                    response.Model.PromptVersion)
                    ? configuration.Model.PromptVersion
                    : response.Model.PromptVersion
            },
            Pricing = inputPrice.HasValue && outputPrice.HasValue
                ? new
                {
                    Currency = "USD",
                    InputPricePerMillionTokens = inputPrice,
                    OutputPricePerMillionTokens = outputPrice
                }
                : null,
            configuration.MaxCases,
            configuration.Deterministic,
            configuration.ExecuteExternalTools,
            configuration.Notes,
            ExecutionMode = "python-llm-evaluation"
        };

        var persistedMetrics = new
        {
            response.SampleCount,
            response.Metrics.DomainAccuracy,
            response.Metrics.SeverityAccuracy,
            response.Metrics.TriageAccuracy,
            response.Metrics.AverageConfidence,
            response.Metrics.TotalLatencyMs,
            response.Metrics.AverageLatencyMs,
            response.Metrics.PromptTokens,
            response.Metrics.CompletionTokens,
            response.Metrics.TotalTokens,
            EstimatedCost = estimatedCost,
            CostCurrency = estimatedCost.HasValue ? "USD" : null,
            Predictions = response.Predictions
        };

        var run = new ExperimentRunRecord(
            Guid.NewGuid(),
            configuration.ExperimentId,
            "llm",
            response.Model.Name,
            string.IsNullOrWhiteSpace(response.Model.PromptVersion)
                ? configuration.Model.PromptVersion
                : response.Model.PromptVersion,
            dataset.Name,
            dataset.Version,
            JsonSerializer.Serialize(persistedConfiguration),
            response.SampleCount,
            startedAt,
            finishedAt,
            JsonSerializer.Serialize(persistedMetrics),
            configuration.Notes
                ?? $"LLM evaluation using provider '{response.Model.Provider}'.");

        await _evaluationStore.RecordRunAsync(
            run,
            cancellationToken);

        return run;
    }

    private static decimal CalculateEstimatedCost(
        int promptTokens,
        int completionTokens,
        decimal inputPricePerMillionTokens,
        decimal outputPricePerMillionTokens)
    {
        if (promptTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(promptTokens));
        }

        if (completionTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completionTokens));
        }

        if (inputPricePerMillionTokens < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inputPricePerMillionTokens));
        }

        if (outputPricePerMillionTokens < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputPricePerMillionTokens));
        }

        return (
            promptTokens * inputPricePerMillionTokens
            + completionTokens * outputPricePerMillionTokens
        ) / TokensPerMillion;
    }

    private static void ValidateResponse(
        LlmEvaluationResponse response,
        LlmEvaluationRequest request,
        EvaluationRunConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!string.Equals(
                response.ModelType,
                "llm",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "LLM evaluation returned an invalid model type.");
        }

        if (!string.Equals(
                response.Dataset.Id,
                request.DatasetId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "LLM evaluation returned a different dataset id.");
        }

        if (!string.Equals(
                response.Dataset.Name,
                request.DatasetName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "LLM evaluation returned a different dataset name.");
        }

        if (!string.Equals(
                response.Dataset.Version,
                request.DatasetVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "LLM evaluation returned a different dataset version.");
        }

        if (!string.Equals(
                response.Model.Provider,
                request.Provider,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "LLM evaluation returned a different provider.");
        }

        if (response.SampleCount != request.Cases.Count)
        {
            throw new InvalidOperationException(
                "LLM evaluation sample count does not match the submitted cases.");
        }

        if (response.Predictions.Count != request.Cases.Count)
        {
            throw new InvalidOperationException(
                "LLM evaluation prediction count does not match the submitted cases.");
        }

        if (configuration.MaxCases is int maxCases &&
            request.Cases.Count > maxCases)
        {
            throw new InvalidOperationException(
                "LLM evaluation submitted more cases than the configured maximum.");
        }
    }
}