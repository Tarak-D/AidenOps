using System.Text.Json;
using AIOps.Abstractions.Evaluation;
using AIOps.Abstractions.Knowledge;

namespace AIOps.Orchestration.Evaluation;

public sealed class RetrievalEvaluationRunner
{
    private readonly IKnowledgeStore _knowledgeStore;
    private readonly IEvaluationStore _evaluationStore;
    private readonly TimeProvider _timeProvider;

    public RetrievalEvaluationRunner(
        IKnowledgeStore knowledgeStore,
        IEvaluationStore evaluationStore,
        TimeProvider? timeProvider = null)
    {
        _knowledgeStore = knowledgeStore;
        _evaluationStore = evaluationStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<RetrievalEvaluationSummary> RunAsync(
        string datasetId,
        string datasetName,
        string datasetVersion,
        IReadOnlyList<RetrievalEvaluationCase> cases,
        string experimentId,
        int? maxCases = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);

        if (string.IsNullOrWhiteSpace(datasetId))
        {
            throw new ArgumentException(
                "Dataset id is required.",
                nameof(datasetId));
        }

        if (string.IsNullOrWhiteSpace(datasetName))
        {
            throw new ArgumentException(
                "Dataset name is required.",
                nameof(datasetName));
        }

        if (string.IsNullOrWhiteSpace(datasetVersion))
        {
            throw new ArgumentException(
                "Dataset version is required.",
                nameof(datasetVersion));
        }

        if (string.IsNullOrWhiteSpace(experimentId))
        {
            throw new ArgumentException(
                "Experiment id is required.",
                nameof(experimentId));
        }

        if (cases.Count == 0)
        {
            throw new ArgumentException(
                "Retrieval evaluation dataset must contain at least one case.",
                nameof(cases));
        }

        if (maxCases is <= 0)
        {
            throw new ArgumentException(
                "Max cases must be greater than zero when specified.",
                nameof(maxCases));
        }

        var selectedCases = maxCases is int limit
            ? cases.Take(limit).ToArray()
            : cases.ToArray();

        var startedAt = _timeProvider.GetUtcNow();

        var caseResults =
            new List<RetrievalEvaluationCaseResult>(
                selectedCases.Length);

        foreach (var evaluationCase in selectedCases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            caseResults.Add(
                await EvaluateCaseAsync(
                    evaluationCase,
                    cancellationToken));
        }

        var finishedAt = _timeProvider.GetUtcNow();

        var metrics = CalculateMetrics(caseResults);

        var persistedMetrics = new
        {
            metrics.SampleCount,
            metrics.HitAtK,
            metrics.RecallAtK,
            metrics.MeanReciprocalRank,
            metrics.AverageFirstRelevantRank,
            Cases = caseResults
        };

        var configuration = new
        {
            DatasetId = datasetId,
            DatasetName = datasetName,
            DatasetVersion = datasetVersion,
            ExecutionMode = "knowledge-store-retrieval",
            MaxCases = maxCases,
            TopK = selectedCases
                .Select(c => c.TopK)
                .Distinct()
                .ToArray()
        };

        var run = new ExperimentRunRecord(
            Guid.NewGuid(),
            experimentId,
            "retrieval",
            "postgres-pgvector",
            null,
            datasetName,
            datasetVersion,
            JsonSerializer.Serialize(configuration),
            caseResults.Count,
            startedAt,
            finishedAt,
            JsonSerializer.Serialize(persistedMetrics),
            "Phase 15 retrieval evaluation.");

        await _evaluationStore.RecordRunAsync(
            run,
            cancellationToken);

        return new RetrievalEvaluationSummary(
            run,
            caseResults,
            metrics);
    }

    private async Task<RetrievalEvaluationCaseResult> EvaluateCaseAsync(
        RetrievalEvaluationCase evaluationCase,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(evaluationCase.Id))
        {
            throw new ArgumentException(
                "Retrieval evaluation case id is required.");
        }

        if (string.IsNullOrWhiteSpace(evaluationCase.Query))
        {
            throw new ArgumentException(
                "Retrieval evaluation query is required.");
        }

        if (evaluationCase.ExpectedDocumentIds.Count == 0)
        {
            throw new ArgumentException(
                "At least one expected document id is required.");
        }

        if (evaluationCase.TopK <= 0)
        {
            throw new ArgumentException(
                "TopK must be greater than zero.");
        }

        var results = await _knowledgeStore.SearchAsync(
            evaluationCase.Query.Trim(),
            evaluationCase.TopK,
            cancellationToken);

        var retrievedDocumentIds = results
            .Select(result => result.DocumentId)
            .ToArray();

        var expected = evaluationCase.ExpectedDocumentIds
            .ToHashSet();

        var relevantRetrievedCount = retrievedDocumentIds
            .Distinct()
            .Count(expected.Contains);

        var hit = relevantRetrievedCount > 0;

        var recall =
            relevantRetrievedCount /
            (double)expected.Count;

        int? firstRelevantRank = null;

        for (var index = 0; index < retrievedDocumentIds.Length; index++)
        {
            if (expected.Contains(retrievedDocumentIds[index]))
            {
                firstRelevantRank = index + 1;
                break;
            }
        }

        var reciprocalRank = firstRelevantRank.HasValue
            ? 1.0 / firstRelevantRank.Value
            : 0.0;

        return new RetrievalEvaluationCaseResult(
            evaluationCase.Id,
            evaluationCase.Query,
            evaluationCase.ExpectedDocumentIds,
            retrievedDocumentIds,
            evaluationCase.TopK,
            hit,
            recall,
            reciprocalRank,
            firstRelevantRank,
            results);
    }

    private static RetrievalEvaluationMetrics CalculateMetrics(
        IReadOnlyList<RetrievalEvaluationCaseResult> results)
    {
        if (results.Count == 0)
        {
            return new RetrievalEvaluationMetrics(
                0,
                0,
                0,
                0,
                0);
        }

        var firstRelevantRanks = results
            .Where(result => result.FirstRelevantRank.HasValue)
            .Select(result => result.FirstRelevantRank!.Value)
            .ToArray();

        var averageFirstRelevantRank =
            firstRelevantRanks.Length == 0
                ? 0
                : firstRelevantRanks.Average();

        return new RetrievalEvaluationMetrics(
            results.Count,
            results.Count(result => result.Hit) /
                (double)results.Count,
            results.Average(result => result.Recall),
            results.Average(result => result.ReciprocalRank),
            averageFirstRelevantRank);
    }
}