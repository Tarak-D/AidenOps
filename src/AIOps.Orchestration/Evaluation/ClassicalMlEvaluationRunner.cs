using System.Text.Json;
using AIOps.Abstractions.Evaluation;
using AIOps.Contracts.Evaluation;
using AIOps.Domain;

namespace AIOps.Orchestration.Evaluation;

public sealed class ClassicalMlEvaluationRunner
{
    private const string ExpectedTrainingCorpusId =
        "phase15-classical-ml-training";
    private const string ExpectedTrainingCorpusName =
        "Phase 15 Classical ML Training Corpus";
    private const string ExpectedTrainingCorpusVersion = "1.0.0";
    private const string ExpectedDatasetId =
        "phase15-classical-ml-baseline";
    private const string ExpectedDatasetName =
        "Phase 15 Classical ML Baseline";
    private const string ExpectedDatasetVersion = "1.0.0";
    private const string ExpectedModelType = "classical_ml";
    private const string ExpectedModelName =
        "tfidf-logistic-regression";
    private static readonly string[] ExpectedCaseIds =
    [
        "network-001",
        "network-002",
        "identity-001",
        "identity-002",
        "database-001",
        "database-002",
        "infrastructure-001",
        "infrastructure-002"
    ];

    private readonly IClassicalMlEvaluationGateway _gateway;
    private readonly IEvaluationStore _evaluationStore;
    private readonly TimeProvider _timeProvider;

    public ClassicalMlEvaluationRunner(
        IClassicalMlEvaluationGateway gateway,
        IEvaluationStore evaluationStore,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(evaluationStore);
        _gateway = gateway;
        _evaluationStore = evaluationStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ExperimentRunRecord> RunAsync(
        string experimentId,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(experimentId) ||
            experimentId.Length > 200 ||
            !string.Equals(
                experimentId,
                experimentId.Trim(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Experiment ID must be non-empty, at most 200 characters, and have no surrounding whitespace.",
                nameof(experimentId));
        }

        var startedAt = _timeProvider.GetUtcNow();
        var response = await _gateway.EvaluateAsync(cancellationToken);
        var finishedAt = _timeProvider.GetUtcNow();

        ValidateResponse(response);

        var configuration = new
        {
            DatasetId = response.DatasetId,
            DatasetName = response.DatasetName,
            DatasetVersion = response.DatasetVersion,
            TrainingCorpus = new
            {
                Id = response.TrainingCorpusId,
                Name = response.TrainingCorpusName,
                Version = response.TrainingCorpusVersion
            },
            Model = new
            {
                Type = response.ModelType,
                Name = response.ModelName
            },
            ExecutionMode = "python-classical-ml-baseline",
            Deterministic = true,
            ExternalToolsExecuted = false
        };
        var metrics = new
        {
            SampleCount = response.TotalCases,
            CorrectCases = response.CorrectCases,
            DomainClassificationAccuracy = response.Accuracy,
            Predictions = response.CaseResults
        };

        var run = new ExperimentRunRecord(
            Guid.NewGuid(),
            experimentId,
            response.ModelType,
            response.ModelName,
            PromptVersion: null,
            response.DatasetName,
            response.DatasetVersion,
            JsonSerializer.Serialize(configuration),
            response.TotalCases,
            startedAt,
            finishedAt,
            JsonSerializer.Serialize(metrics),
            notes ?? "Fixed Phase 15 classical ML baseline evaluation.");

        await _evaluationStore.RecordRunAsync(run, cancellationToken);
        return run;
    }

    private static void ValidateResponse(
        ClassicalMlEvaluationResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.TrainingCorpusId != ExpectedTrainingCorpusId ||
            response.TrainingCorpusName != ExpectedTrainingCorpusName ||
            response.TrainingCorpusVersion != ExpectedTrainingCorpusVersion ||
            response.DatasetId != ExpectedDatasetId ||
            response.DatasetName != ExpectedDatasetName ||
            response.DatasetVersion != ExpectedDatasetVersion ||
            response.ModelType != ExpectedModelType ||
            response.ModelName != ExpectedModelName)
        {
            throw new InvalidOperationException(
                "Python classical ML evaluation returned unexpected corpus, dataset, or model metadata.");
        }

        if (response.TotalCases <= 0 ||
            response.CaseResults is null ||
            response.CaseResults.Count != response.TotalCases ||
            !response.CaseResults
                .Select(prediction => prediction.CaseId)
                .SequenceEqual(ExpectedCaseIds, StringComparer.Ordinal) ||
            response.CorrectCases < 0 ||
            response.CorrectCases > response.TotalCases ||
            !double.IsFinite(response.Accuracy) ||
            response.Accuracy is < 0 or > 1 ||
            response.Accuracy !=
                response.CorrectCases / (double)response.TotalCases)
        {
            throw new InvalidOperationException(
                "Python classical ML evaluation returned inconsistent case counts or accuracy.");
        }

        var caseIds = new HashSet<string>(StringComparer.Ordinal);
        var correctCases = 0;
        foreach (var prediction in response.CaseResults)
        {
            if (string.IsNullOrWhiteSpace(prediction.CaseId) ||
                !caseIds.Add(prediction.CaseId) ||
                !IsClassificationDomain(prediction.PredictedDomain) ||
                !IsClassificationDomain(prediction.ExpectedDomain) ||
                !double.IsFinite(prediction.Confidence) ||
                prediction.Confidence is < 0 or > 1)
            {
                throw new InvalidOperationException(
                    "Python classical ML evaluation returned invalid or duplicate predictions.");
            }

            if (prediction.PredictedDomain == prediction.ExpectedDomain)
            {
                correctCases++;
            }
        }

        if (correctCases != response.CorrectCases)
        {
            throw new InvalidOperationException(
                "Python classical ML evaluation accuracy does not match its predictions.");
        }
    }

    private static bool IsClassificationDomain(string value) =>
        Enum.TryParse<TicketDomain>(
            value,
            ignoreCase: false,
            out var domain) &&
        domain != TicketDomain.Unknown &&
        string.Equals(
            Enum.GetName(domain),
            value,
            StringComparison.Ordinal);
}
