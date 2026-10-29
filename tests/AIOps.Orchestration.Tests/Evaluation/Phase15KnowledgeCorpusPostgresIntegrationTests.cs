using System.Text.Json;
using AIOps.Abstractions.Evaluation;
using AIOps.Abstractions.Knowledge;
using AIOps.Infrastructure.EfCore;
using AIOps.Infrastructure.Knowledge;
using AIOps.Orchestration.Evaluation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector.EntityFrameworkCore;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class Phase15KnowledgeCorpusPostgresIntegrationTests
{
    private const string TestDatabaseName = "aios_phase15_test";
    private const string EqualDistanceChunkContent =
        "Phase 15 PostgreSQL equal-distance ordering fixture.";

    [RequiresPhase15PostgresFact]
    public async Task Corpus_retrieval_and_repeated_runs_are_consistent_in_postgres()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "AIOPS_PHASE15_TEST_CONNECTION");
        var expectedHost = Environment.GetEnvironmentVariable(
            "AIOPS_PHASE15_TEST_HOST");
        var expectedPortValue = Environment.GetEnvironmentVariable(
            "AIOPS_PHASE15_TEST_PORT");
        var expectedDatabase = Environment.GetEnvironmentVariable(
            "AIOPS_PHASE15_TEST_DATABASE");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "AIOPS_PHASE15_TEST_CONNECTION was not available when the integration test started.");
        }

        if (string.IsNullOrWhiteSpace(expectedHost) ||
            string.IsNullOrWhiteSpace(expectedPortValue) ||
            string.IsNullOrWhiteSpace(expectedDatabase) ||
            !int.TryParse(expectedPortValue, out var expectedPort) ||
            expectedPort is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                "The isolated Phase 15 PostgreSQL host, port, and database configuration is invalid.");
        }

        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        if (!string.Equals(
                connection.Database,
                TestDatabaseName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Phase 15 PostgreSQL integration tests require the aios_phase15_test database.");
        }

        if (!string.Equals(
                expectedDatabase,
                TestDatabaseName,
                StringComparison.Ordinal) ||
            !connection.ContainsKey("Host") ||
            !connection.ContainsKey("Port") ||
            !connection.ContainsKey("Database") ||
            string.IsNullOrWhiteSpace(connection.Host) ||
            connection.Host.Contains(',') ||
            !string.Equals(
                connection.Host,
                expectedHost,
                StringComparison.OrdinalIgnoreCase) ||
            connection.Port != expectedPort)
        {
            throw new InvalidOperationException(
                "The PostgreSQL connection must match the explicitly configured isolated Phase 15 host, port, and database.");
        }

        var options = new DbContextOptionsBuilder<AIOpsDbContext>()
            .UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.UseVector())
            .Options;

        await using var context = new AIOpsDbContext(options);
        await using var transaction =
            await context.Database.BeginTransactionAsync();

        try
        {
            var corpus = Phase15KnowledgeCorpus.Create();
            var equalDistanceDocuments = CreateEqualDistanceDocuments();
            var fixtureDocuments = corpus.Documents
                .Concat(equalDistanceDocuments)
                .ToArray();
            var documentIds = fixtureDocuments
                .Select(entry => entry.Document.Id)
                .ToArray();
            var chunkIds = fixtureDocuments
                .SelectMany(entry => entry.Chunks)
                .Select(chunk => chunk.Id)
                .ToArray();

            var existingDocumentIds = await context.KnowledgeDocuments
                .AsNoTracking()
                .Where(document => documentIds.Contains(document.Id))
                .Select(document => document.Id)
                .ToArrayAsync();
            var existingChunkIds = await context.KnowledgeChunks
                .AsNoTracking()
                .Where(chunk => chunkIds.Contains(chunk.Id))
                .Select(chunk => chunk.Id)
                .ToArrayAsync();

            if (existingDocumentIds.Length != 0 ||
                existingChunkIds.Length != 0)
            {
                throw new InvalidOperationException(
                    "The isolated Phase 15 database already contains one or more fixture document or chunk IDs; refusing to write fixture data.");
            }

            var embeddingGenerator = new DeterministicEmbeddingGenerator();
            var knowledgeStore = new PostgresKnowledgeStore(
                context,
                embeddingGenerator);

            foreach (var corpusDocument in corpus.Documents)
            {
                await knowledgeStore.SaveDocumentAsync(
                    corpusDocument.Document,
                    corpusDocument.Chunks);
            }

            foreach (var corpusDocument in corpus.Documents)
            {
                foreach (var chunk in corpusDocument.Chunks)
                {
                    var results = await knowledgeStore.SearchAsync(
                        chunk.Content,
                        limit: 6);
                    var repeatedResults = await knowledgeStore.SearchAsync(
                        chunk.Content,
                        limit: 6);

                    Assert.Equal(
                        results.Select(result => result.ChunkId),
                        repeatedResults.Select(result => result.ChunkId));

                    var retrievedChunk = Assert.Single(
                        results,
                        result => result.ChunkId == chunk.Id);
                    Assert.Equal(corpusDocument.Document.Id, retrievedChunk.DocumentId);
                    Assert.Equal(Phase15KnowledgeCorpus.Source, retrievedChunk.Source);
                    Assert.Equal(corpusDocument.Document.Title, retrievedChunk.Title);
                    Assert.Equal(chunk.ChunkIndex, retrievedChunk.ChunkIndex);
                    Assert.Equal(chunk.Content, retrievedChunk.Content);
                }
            }

            var evaluationStore = new InMemoryEvaluationStore();
            var runner = new RetrievalEvaluationRunner(
                knowledgeStore,
                evaluationStore);
            var cases = Phase15KnowledgeCorpus.CreateRetrievalCases();

            var baseline = await runner.RunAsync(
                corpus,
                cases,
                "phase15-postgres-baseline");
            var candidate = await runner.RunAsync(
                corpus,
                cases,
                "phase15-postgres-candidate");

            Assert.Equal(3, baseline.Cases.Count);
            Assert.Equal(3, candidate.Cases.Count);
            Assert.Equal(cases.Select(testCase => testCase.Id), baseline.Cases.Select(result => result.CaseId));
            Assert.Equal(cases.Select(testCase => testCase.Id), candidate.Cases.Select(result => result.CaseId));
            for (var index = 0; index < baseline.Cases.Count; index++)
            {
                var baselineCase = baseline.Cases[index];
                var candidateCase = candidate.Cases[index];

                Assert.Equal(
                    baselineCase.RetrievedDocumentIds,
                    candidateCase.RetrievedDocumentIds);
                Assert.Equal(
                    baselineCase.Results.Select(result => result.ChunkId),
                    candidateCase.Results.Select(result => result.ChunkId));
                Assert.Equal(
                    baselineCase.Results.Select(result => result.Similarity),
                    candidateCase.Results.Select(result => result.Similarity));
            }

            using var baselineConfig = JsonDocument.Parse(baseline.Run.ConfigJson);
            using var candidateConfig = JsonDocument.Parse(candidate.Run.ConfigJson);
            Assert.Equal(
                corpus.ComputeSha256(),
                baselineConfig.RootElement
                    .GetProperty("KnowledgeCorpusSha256")
                    .GetString());
            Assert.Equal(
                corpus.ComputeSha256(),
                candidateConfig.RootElement
                    .GetProperty("KnowledgeCorpusSha256")
                    .GetString());
            Assert.Equal(
                corpus.CorpusId,
                baselineConfig.RootElement
                    .GetProperty("KnowledgeCorpusId")
                    .GetString());
            Assert.Equal(
                corpus.Version,
                baselineConfig.RootElement
                    .GetProperty("KnowledgeCorpusVersion")
                    .GetString());
            Assert.Equal(
                baselineConfig.RootElement
                    .GetProperty("KnowledgeCorpusSha256")
                    .GetString(),
                candidateConfig.RootElement
                    .GetProperty("KnowledgeCorpusSha256")
                    .GetString());
            Assert.Equal(
                baselineConfig.RootElement.GetProperty("TopK").EnumerateArray()
                    .Select(element => element.GetInt32()),
                candidateConfig.RootElement.GetProperty("TopK").EnumerateArray()
                    .Select(element => element.GetInt32()));
            Assert.Equal(baseline.Run.DatasetName, candidate.Run.DatasetName);
            Assert.Equal(baseline.Run.DatasetVersion, candidate.Run.DatasetVersion);
            Assert.Equal(cases.Count, baseline.Run.SampleCount);
            Assert.Equal(cases.Count, candidate.Run.SampleCount);
            Assert.Equal(2, evaluationStore.Runs.Count);

            foreach (var tieDocument in equalDistanceDocuments)
            {
                await knowledgeStore.SaveDocumentAsync(
                    tieDocument.Document,
                    tieDocument.Chunks);
            }

            var equalDistanceResults = await knowledgeStore.SearchAsync(
                EqualDistanceChunkContent,
                limit: 20);
            var tiedChunks = equalDistanceResults
                .Where(result => result.Content == EqualDistanceChunkContent)
                .ToArray();

            Assert.Equal(3, tiedChunks.Length);
            Assert.Equal(
                new[]
                {
                    Guid.Parse("15000000-0000-4000-8000-000000004001"),
                    Guid.Parse("15000000-0000-4000-8000-000000004002"),
                    Guid.Parse("15000000-0000-4000-8000-000000005001")
                },
                tiedChunks
                    .Select(result => result.ChunkId)
                    .OrderBy(chunkId => chunkId));

            var distances = tiedChunks
                .Select(result => 1.0 - result.Similarity)
                .ToArray();
            var firstDistance = distances[0];

            Assert.All(
                distances,
                distance => Assert.InRange(
                    Math.Abs(distance - firstDistance),
                    0.0,
                    1e-6));
            Assert.Equal(
                new[]
                {
                    (
                        DocumentId: Guid.Parse("15000000-0000-4000-8000-000000000004"),
                        ChunkIndex: 0),
                    (
                        DocumentId: Guid.Parse("15000000-0000-4000-8000-000000000004"),
                        ChunkIndex: 1),
                    (
                        DocumentId: Guid.Parse("15000000-0000-4000-8000-000000000005"),
                        ChunkIndex: 0)
                },
                tiedChunks.Select(result => (
                    result.DocumentId,
                    result.ChunkIndex)));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static IReadOnlyList<EvaluationKnowledgeCorpusDocument>
        CreateEqualDistanceDocuments()
    {
        var firstDocumentId =
            Guid.Parse("15000000-0000-4000-8000-000000000004");
        var secondDocumentId =
            Guid.Parse("15000000-0000-4000-8000-000000000005");

        return
        [
            new EvaluationKnowledgeCorpusDocument(
                new KnowledgeDocument(
                    firstDocumentId,
                    "phase-15-ordering-test",
                    "Equal distance ordering fixture one",
                    EqualDistanceChunkContent),
                [
                    new KnowledgeChunk(
                        Guid.Parse("15000000-0000-4000-8000-000000004002"),
                        firstDocumentId,
                        0,
                        EqualDistanceChunkContent),
                    new KnowledgeChunk(
                        Guid.Parse("15000000-0000-4000-8000-000000004001"),
                        firstDocumentId,
                        1,
                        EqualDistanceChunkContent)
                ]),
            new EvaluationKnowledgeCorpusDocument(
                new KnowledgeDocument(
                    secondDocumentId,
                    "phase-15-ordering-test",
                    "Equal distance ordering fixture two",
                    EqualDistanceChunkContent),
                [
                    new KnowledgeChunk(
                        Guid.Parse("15000000-0000-4000-8000-000000005001"),
                        secondDocumentId,
                        0,
                        EqualDistanceChunkContent)
                ])
        ];
    }

    private sealed class RequiresPhase15PostgresFactAttribute : FactAttribute
    {
        public RequiresPhase15PostgresFactAttribute()
        {
            if (new[]
                {
                    "AIOPS_PHASE15_TEST_CONNECTION",
                    "AIOPS_PHASE15_TEST_HOST",
                    "AIOPS_PHASE15_TEST_PORT",
                    "AIOPS_PHASE15_TEST_DATABASE"
                }.Any(name => string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable(name))))
            {
                Skip =
                    "Configure the isolated Phase 15 PostgreSQL connection, host, port, and aios_phase15_test database.";
            }
        }
    }

    private sealed class InMemoryEvaluationStore : IEvaluationStore
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
            IReadOnlyList<ExperimentRunRecord> runs = Runs
                .Where(run =>
                    experimentId is null ||
                    run.ExperimentId == experimentId)
                .Where(run =>
                    datasetName is null ||
                    run.DatasetName == datasetName)
                .ToArray();

            return Task.FromResult(runs);
        }
    }
}
