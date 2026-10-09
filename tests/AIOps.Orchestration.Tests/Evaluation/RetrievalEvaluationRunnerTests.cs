using AIOps.Abstractions.Evaluation;
using AIOps.Abstractions.Knowledge;
using AIOps.Orchestration.Evaluation;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class RetrievalEvaluationRunnerTests
{
    [Fact]
    public async Task RunAsync_Calculates_retrieval_metrics_and_persists_run()
    {
        var expectedDocumentId = Guid.NewGuid();
        var unrelatedDocumentId = Guid.NewGuid();

        var knowledgeStore = new FakeKnowledgeStore(
        [
            CreateResult(
                unrelatedDocumentId,
                "Unrelated",
                0.95),
            CreateResult(
                expectedDocumentId,
                "Expected",
                0.90)
        ]);

        var evaluationStore = new FakeEvaluationStore();

        var runner = new RetrievalEvaluationRunner(
            knowledgeStore,
            evaluationStore);

        var cases = new[]
        {
            new RetrievalEvaluationCase(
                "case-1",
                "VPN connection failure",
                [expectedDocumentId],
                TopK: 5)
        };

        var summary = await runner.RunAsync(
            "retrieval-dataset-001",
            "retrieval-golden",
            "1.0",
            cases,
            "experiment-retrieval-001");

        Assert.Equal(1, summary.Metrics.SampleCount);
        Assert.Equal(1.0, summary.Metrics.HitAtK);
        Assert.Equal(1.0, summary.Metrics.RecallAtK);
        Assert.Equal(0.5, summary.Metrics.MeanReciprocalRank);
        Assert.Equal(2.0, summary.Metrics.AverageFirstRelevantRank);

        var caseResult = Assert.Single(summary.Cases);

        Assert.True(caseResult.Hit);
        Assert.Equal(1.0, caseResult.Recall);
        Assert.Equal(0.5, caseResult.ReciprocalRank);
        Assert.Equal(2, caseResult.FirstRelevantRank);

        Assert.Equal(
            "retrieval",
            summary.Run.ModelType);

        Assert.Equal(
            "postgres-pgvector",
            summary.Run.ModelName);

        Assert.Equal(
            "retrieval-golden",
            summary.Run.DatasetName);

        Assert.Equal(
            "1.0",
            summary.Run.DatasetVersion);

        Assert.Equal(
            1,
            summary.Run.SampleCount);

        Assert.Single(evaluationStore.Runs);

        Assert.Contains(
            "\"HitAtK\":1",
            summary.Run.MetricsJson);

        Assert.Contains(
            "\"MeanReciprocalRank\":0.5",
            summary.Run.MetricsJson);
    }

    [Fact]
    public async Task RunAsync_Calculates_miss_as_zero()
    {
        var expectedDocumentId = Guid.NewGuid();

        var knowledgeStore = new FakeKnowledgeStore(
        [
            CreateResult(
                Guid.NewGuid(),
                "Unrelated",
                0.80)
        ]);

        var runner = new RetrievalEvaluationRunner(
            knowledgeStore,
            new FakeEvaluationStore());

        var summary = await runner.RunAsync(
            "retrieval-dataset-002",
            "retrieval-golden",
            "1.0",
            [
                new RetrievalEvaluationCase(
                    "case-1",
                    "database unavailable",
                    [expectedDocumentId],
                    TopK: 5)
            ],
            "experiment-retrieval-002");

        var result = Assert.Single(summary.Cases);

        Assert.False(result.Hit);
        Assert.Equal(0.0, result.Recall);
        Assert.Equal(0.0, result.ReciprocalRank);
        Assert.Null(result.FirstRelevantRank);

        Assert.Equal(0.0, summary.Metrics.HitAtK);
        Assert.Equal(0.0, summary.Metrics.RecallAtK);
        Assert.Equal(0.0, summary.Metrics.MeanReciprocalRank);
        Assert.Equal(0.0, summary.Metrics.AverageFirstRelevantRank);
    }

    [Fact]
    public async Task RunAsync_MaxCases_limits_evaluated_cases()
    {
        var expectedDocumentId = Guid.NewGuid();

        var knowledgeStore = new FakeKnowledgeStore(
        [
            CreateResult(
                expectedDocumentId,
                "Expected",
                0.99)
        ]);

        var runner = new RetrievalEvaluationRunner(
            knowledgeStore,
            new FakeEvaluationStore());

        var cases = new[]
        {
            new RetrievalEvaluationCase(
                "case-1",
                "query one",
                [expectedDocumentId]),
            new RetrievalEvaluationCase(
                "case-2",
                "query two",
                [expectedDocumentId]),
            new RetrievalEvaluationCase(
                "case-3",
                "query three",
                [expectedDocumentId])
        };

        var summary = await runner.RunAsync(
            "retrieval-dataset-003",
            "retrieval-golden",
            "1.0",
            cases,
            "experiment-retrieval-003",
            maxCases: 2);

        Assert.Equal(2, summary.Metrics.SampleCount);
        Assert.Equal(2, summary.Cases.Count);
        Assert.Equal(2, knowledgeStore.SearchCount);

        Assert.Equal(
            "case-1",
            summary.Cases[0].CaseId);

        Assert.Equal(
            "case-2",
            summary.Cases[1].CaseId);
    }

    [Fact]
    public async Task RunAsync_Calculates_recall_for_multiple_expected_documents()
    {
        var expectedDocumentOne = Guid.NewGuid();
        var expectedDocumentTwo = Guid.NewGuid();

        var knowledgeStore = new FakeKnowledgeStore(
        [
            CreateResult(
                expectedDocumentOne,
                "Expected One",
                0.95),
            CreateResult(
                Guid.NewGuid(),
                "Unrelated",
                0.85)
        ]);

        var runner = new RetrievalEvaluationRunner(
            knowledgeStore,
            new FakeEvaluationStore());

        var summary = await runner.RunAsync(
            "retrieval-dataset-004",
            "retrieval-golden",
            "1.0",
            [
                new RetrievalEvaluationCase(
                    "case-1",
                    "VPN troubleshooting",
                    [
                        expectedDocumentOne,
                        expectedDocumentTwo
                    ],
                    TopK: 5)
            ],
            "experiment-retrieval-004");

        var result = Assert.Single(summary.Cases);

        Assert.True(result.Hit);
        Assert.Equal(0.5, result.Recall);
        Assert.Equal(1.0, result.ReciprocalRank);
        Assert.Equal(1, result.FirstRelevantRank);

        Assert.Equal(
            0.5,
            summary.Metrics.RecallAtK);
    }

    [Fact]
    public async Task RunAsync_Rejects_empty_cases()
    {
        var runner = new RetrievalEvaluationRunner(
            new FakeKnowledgeStore([]),
            new FakeEvaluationStore());

        await Assert.ThrowsAsync<ArgumentException>(
            () => runner.RunAsync(
                "retrieval-dataset-005",
                "retrieval-golden",
                "1.0",
                [],
                "experiment-retrieval-005"));
    }

    [Fact]
    public async Task RunAsync_Rejects_invalid_top_k()
    {
        var runner = new RetrievalEvaluationRunner(
            new FakeKnowledgeStore([]),
            new FakeEvaluationStore());

        await Assert.ThrowsAsync<ArgumentException>(
            () => runner.RunAsync(
                "retrieval-dataset-006",
                "retrieval-golden",
                "1.0",
                [
                    new RetrievalEvaluationCase(
                        "case-1",
                        "VPN failure",
                        [Guid.NewGuid()],
                        TopK: 0)
                ],
                "experiment-retrieval-006"));
    }

    private static KnowledgeSearchResult CreateResult(
        Guid documentId,
        string title,
        double similarity)
    {
        return new KnowledgeSearchResult(
            Guid.NewGuid(),
            documentId,
            "test",
            title,
            0,
            $"{title} content",
            similarity,
            null);
    }

    private sealed class FakeKnowledgeStore : IKnowledgeStore
    {
        private readonly IReadOnlyList<KnowledgeSearchResult> _results;

        public FakeKnowledgeStore(
            IReadOnlyList<KnowledgeSearchResult> results)
        {
            _results = results;
        }

        public int SearchCount { get; private set; }

        public Task SaveDocumentAsync(
            KnowledgeDocument document,
            IReadOnlyList<KnowledgeChunk> chunks,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
            string query,
            int limit = 5,
            CancellationToken cancellationToken = default)
        {
            SearchCount++;

            IReadOnlyList<KnowledgeSearchResult> results =
                _results
                    .Take(limit)
                    .ToArray();

            return Task.FromResult(results);
        }
    }

    private sealed class FakeEvaluationStore : IEvaluationStore
    {
        public List<ExperimentRunRecord> Runs { get; } = [];

        public Task RecordRunAsync(
            ExperimentRunRecord run,
            CancellationToken ct = default)
        {
            Runs.Add(run);

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ExperimentRunRecord>> ListRunsAsync(
            string? experimentId = null,
            string? datasetName = null,
            CancellationToken ct = default)
        {
            IReadOnlyList<ExperimentRunRecord> results =
                Runs
                    .Where(run =>
                        experimentId is null ||
                        run.ExperimentId == experimentId)
                    .Where(run =>
                        datasetName is null ||
                        run.DatasetName == datasetName)
                    .ToArray();

            return Task.FromResult(results);
        }
    }
}