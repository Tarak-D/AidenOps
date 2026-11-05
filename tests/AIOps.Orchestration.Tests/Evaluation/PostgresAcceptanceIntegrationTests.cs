using System.Text.Json;
using AIOps.Abstractions.Evaluation;
using AIOps.Contracts.AgentGateway;
using AIOps.Domain;
using AIOps.Infrastructure.Agents;
using AIOps.Infrastructure.EfCore;
using AIOps.Infrastructure.Evaluation;
using AIOps.Infrastructure.Time;
using AIOps.Orchestration.Evaluation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.EntityFrameworkCore;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class PostgresAcceptanceIntegrationTests
{
    [RequiresAcceptancePostgresFact]
    public async Task EvaluationRunner_runs_are_accepted_and_rolled_back_in_postgres()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "AIOPS_ACCEPTANCE_TEST_CONNECTION");
        var expectedHost = Environment.GetEnvironmentVariable(
            "AIOPS_ACCEPTANCE_TEST_HOST");
        var expectedPortValue = Environment.GetEnvironmentVariable(
            "AIOPS_ACCEPTANCE_TEST_PORT");
        var expectedDatabase = Environment.GetEnvironmentVariable(
            "AIOPS_ACCEPTANCE_TEST_DATABASE");

        if (string.IsNullOrWhiteSpace(connectionString) ||
            string.IsNullOrWhiteSpace(expectedHost) ||
            !int.TryParse(expectedPortValue, out var expectedPort) ||
            !string.Equals(
                expectedDatabase,
                "aios_acceptance_test",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The isolated acceptance PostgreSQL test configuration is invalid.");
        }

        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        if (!string.Equals(
                connection.Database,
                "aios_acceptance_test",
                StringComparison.Ordinal) ||
            !string.Equals(
                connection.Host,
                expectedHost,
                StringComparison.OrdinalIgnoreCase) ||
            connection.Port != expectedPort)
        {
            throw new InvalidOperationException(
                "The acceptance integration test requires its explicitly configured isolated PostgreSQL database.");
        }

        var options = new DbContextOptionsBuilder<AIOpsDbContext>()
            .UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.UseVector())
            .Options;

        await using var context = new AIOpsDbContext(options);

        var testScope = $"phase15-b3-postgres-{Guid.NewGuid():N}";
        var datasetMetadata = new EvaluationDatasetMetadata(
            $"{testScope}-dataset",
            "Golden Incident Dataset",
            "1.0.0",
            "Isolated Phase 15 PostgreSQL acceptance integration fixture.");
        var baselineExperimentId = $"{testScope}-baseline";
        var candidateExperimentId = $"{testScope}-candidate";
        Guid? acceptanceResultId = null;

        await using var transaction =
            await context.Database.BeginTransactionAsync();

        try
        {
            var datasetCase = new EvaluationCase(
                "database-unavailable",
                new AgentTicketContext(
                    Guid.NewGuid(),
                    $"{testScope}-ticket",
                    "Database unavailable",
                    "The production database is unavailable.",
                    "evaluation@example.com",
                    TicketDomain.Unknown,
                    Severity.P3,
                    TicketStatus.New),
                Array.Empty<ToolManifestEntry>(),
                TicketDomain.Database,
                Severity.P1,
                AgentRunOutcome.Escalated,
                null,
                "Escalate because no database remediation tool is available.",
                ExpectedApprovalRequired: false);
            var fixtureDataset = new EvaluationDataset(
                datasetMetadata.Name,
                datasetMetadata.Version,
                [datasetCase]);
            var datasetStore =
                new PostgresEvaluationDatasetStore(context);

            await datasetStore.SaveAsync(
                datasetMetadata,
                fixtureDataset);

            var selectedDataset = await datasetStore.GetAsync(
                datasetMetadata.DatasetId,
                datasetMetadata.Version);
            Assert.NotNull(selectedDataset);
            Assert.Single(selectedDataset.Dataset.Cases);
            var dataset = selectedDataset.Dataset;

            var evaluationStore = new PostgresEvaluationStore(context);
            var acceptanceStore =
                new PostgresEvaluationAcceptanceStore(context);
            var runner = new EvaluationRunner(
                new InProcessFakeAgentGateway(),
                evaluationStore);

            const string modelType = "deterministic_test";
            const string modelName = "InProcessFakeAgentGateway";
            const string promptVersion = "phase15-b3-integration-v1";
            const string baselineRevision =
                "1111111111111111111111111111111111111111";
            const string candidateRevision =
                "2222222222222222222222222222222222222222";

            var baselineSummary = await runner.RunAsync(
                dataset,
                baselineExperimentId,
                modelType,
                modelName,
                promptVersion,
                sourceRevision: baselineRevision);
            var candidateSummary = await runner.RunAsync(
                dataset,
                candidateExperimentId,
                modelType,
                modelName,
                promptVersion,
                sourceRevision: candidateRevision);

            var baselineRuns = await evaluationStore.ListRunsAsync(
                baselineExperimentId);
            var candidateRuns = await evaluationStore.ListRunsAsync(
                candidateExperimentId);
            var baselineRun = Assert.Single(baselineRuns);
            var candidateRun = Assert.Single(candidateRuns);

            Assert.NotEqual(baselineRun.Id, candidateRun.Id);
            Assert.Equal(baselineExperimentId, baselineRun.ExperimentId);
            Assert.Equal(candidateExperimentId, candidateRun.ExperimentId);
            Assert.Equal(dataset.Name, baselineRun.DatasetName);
            Assert.Equal(dataset.Name, candidateRun.DatasetName);
            Assert.Equal(dataset.Version, baselineRun.DatasetVersion);
            Assert.Equal(dataset.Version, candidateRun.DatasetVersion);
            Assert.Equal(dataset.Cases.Count, baselineRun.SampleCount);
            Assert.Equal(dataset.Cases.Count, candidateRun.SampleCount);
            Assert.Equal(modelType, baselineRun.ModelType);
            Assert.Equal(modelType, candidateRun.ModelType);
            Assert.Equal(modelName, baselineRun.ModelName);
            Assert.Equal(modelName, candidateRun.ModelName);
            Assert.Equal(promptVersion, baselineRun.PromptVersion);
            Assert.Equal(promptVersion, candidateRun.PromptVersion);
            Assert.Equal(baselineRun.ConfigJson, candidateRun.ConfigJson);
            Assert.Equal(baselineRevision, baselineRun.SourceRevision);
            Assert.Equal(candidateRevision, candidateRun.SourceRevision);
            Assert.Equal(
                EvaluationInputFingerprint.ComputeSha256(dataset),
                baselineRun.DatasetSha256);
            Assert.Equal(
                baselineRun.DatasetSha256,
                candidateRun.DatasetSha256);
            Assert.Equal(
                baselineSummary.Metrics.TriageAccuracy,
                candidateSummary.Metrics.TriageAccuracy);

            using var baselineMetrics = JsonDocument.Parse(
                baselineRun.MetricsJson);
            using var candidateMetrics = JsonDocument.Parse(
                candidateRun.MetricsJson);
            Assert.True(
                baselineMetrics.RootElement.TryGetProperty(
                    "TriageAccuracy",
                    out var baselineTriageAccuracy));
            Assert.True(
                candidateMetrics.RootElement.TryGetProperty(
                    "TriageAccuracy",
                    out var candidateTriageAccuracy));
            Assert.Equal(
                baselineTriageAccuracy.GetDouble(),
                candidateTriageAccuracy.GetDouble());

            var policyOptions = new EvaluationAcceptancePolicyOptions
            {
                Name = "Phase15-Postgres-Integration-Test",
                RequiredMetrics =
                [
                    new EvaluationAcceptanceMetricRuleOptions
                    {
                        MetricName = "TriageAccuracy",
                        Direction = "HigherIsBetter",
                        AllowedRegression = 0m
                    }
                ]
            };
            var gate = new EvaluationAcceptanceGate(evaluationStore);
            var acceptanceService = new EvaluationAcceptanceService(
                gate,
                acceptanceStore,
                Options.Create(policyOptions),
                new SystemClock());

            var decision = await acceptanceService.EvaluateAndPersistAsync(
                baselineExperimentId,
                candidateExperimentId);
            acceptanceResultId = decision.ResultId;

            Assert.Equal(
                EvaluationAcceptanceStatus.Passed,
                decision.Result.Status);
            Assert.Empty(decision.Result.Issues);
            Assert.Equal(baselineRun.Id, decision.Result.BaselineRunId);
            Assert.Equal(candidateRun.Id, decision.Result.CandidateRunId);
            Assert.Equal(
                policyOptions.Name,
                decision.Result.PolicyName);

            var persistedAcceptance =
                await context.EvaluationAcceptanceResults
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        result => result.Id == decision.ResultId);

            Assert.NotNull(persistedAcceptance);
            Assert.Equal(
                baselineExperimentId,
                persistedAcceptance.BaselineExperimentId);
            Assert.Equal(
                candidateExperimentId,
                persistedAcceptance.CandidateExperimentId);
            Assert.Equal(
                baselineRun.Id,
                persistedAcceptance.BaselineRunId);
            Assert.Equal(
                candidateRun.Id,
                persistedAcceptance.CandidateRunId);
            Assert.Equal("Passed", persistedAcceptance.Status);
            var readBack = await acceptanceStore.GetAsync(decision.ResultId);
            Assert.NotNull(readBack);
            Assert.Equal(decision.ResultId, readBack.Id);
            Assert.Equal(
                EvaluationAcceptanceStatus.Passed,
                readBack.Status);
            Assert.Equal(
                baselineRun.Id,
                readBack.BaselineRunId);
            Assert.Equal(
                candidateRun.Id,
                readBack.CandidateRunId);
        }
        finally
        {
            await transaction.RollbackAsync();
            context.ChangeTracker.Clear();

            Assert.False(
                await context.EvaluationDatasets.AnyAsync(
                    dataset => dataset.DatasetId == datasetMetadata.DatasetId));
            Assert.False(
                await context.EvaluationRuns.AnyAsync(
                    run => run.ExperimentId == baselineExperimentId ||
                           run.ExperimentId == candidateExperimentId));

            if (acceptanceResultId is Guid resultId)
            {
                Assert.False(
                    await context.EvaluationAcceptanceResults.AnyAsync(
                        result => result.Id == resultId));
            }
        }
    }

    private sealed class RequiresAcceptancePostgresFactAttribute : FactAttribute
    {
        public RequiresAcceptancePostgresFactAttribute()
        {
            if (new[]
            {
                "AIOPS_ACCEPTANCE_TEST_CONNECTION",
                "AIOPS_ACCEPTANCE_TEST_HOST",
                "AIOPS_ACCEPTANCE_TEST_PORT",
                "AIOPS_ACCEPTANCE_TEST_DATABASE"
            }
            .Any(name => string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(name))))
            {
                Skip =
                    "The isolated PostgreSQL acceptance integration database is not configured.";
            }
        }
    }
}
