using System.Text.Json;
using AIOps.Abstractions.Evaluation;
using AIOps.Infrastructure.Agents;
using AIOps.Infrastructure.EfCore;
using AIOps.Infrastructure.Evaluation;
using AIOps.Infrastructure.Time;
using AIOps.Orchestration.Evaluation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector.EntityFrameworkCore;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class PostgresAcceptanceIntegrationTests
{
    [Fact]
    public async Task EvaluationRunner_runs_are_accepted_and_rolled_back_in_postgres()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "ConnectionStrings__PostgresConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__PostgresConnection to run this PostgreSQL integration test.");
        }

        var options = new DbContextOptionsBuilder<AIOpsDbContext>()
            .UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.UseVector())
            .Options;

        await using var context = new AIOpsDbContext(options);

        var originalRunCount = await context.EvaluationRuns.LongCountAsync();
        var originalAcceptanceCount =
            await context.EvaluationAcceptanceResults.LongCountAsync();

        await using var transaction =
            await context.Database.BeginTransactionAsync();

        try
        {
            var datasetStore =
                new PostgresEvaluationDatasetStore(context);
            var persistedDatasets = await datasetStore.ListAsync();
            var selectedDataset = persistedDatasets
                .Where(record =>
                    record.Metadata.Name == "Golden Incident Dataset" &&
                    record.Metadata.Version == "1.0.0")
                .OrderBy(record => record.Metadata.DatasetId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (selectedDataset is null)
            {
                throw new Xunit.Sdk.XunitException(
                    "No persisted Golden Incident Dataset version 1.0.0 was found.");
            }

            Assert.Single(selectedDataset.Dataset.Cases);
            var dataset = selectedDataset.Dataset;

            var evaluationStore = new PostgresEvaluationStore(context);
            var acceptanceStore =
                new PostgresEvaluationAcceptanceStore(context);
            var runner = new EvaluationRunner(
                new InProcessFakeAgentGateway(),
                evaluationStore);

            var testScope = $"phase15-b2-postgres-{Guid.NewGuid():N}";
            var baselineExperimentId = $"{testScope}-baseline";
            var candidateExperimentId = $"{testScope}-candidate";
            const string modelType = "deterministic_test";
            const string modelName = "InProcessFakeAgentGateway";
            const string promptVersion = "phase15-b2-integration-v1";

            var baselineSummary = await runner.RunAsync(
                dataset,
                baselineExperimentId,
                modelType,
                modelName,
                promptVersion);
            var candidateSummary = await runner.RunAsync(
                dataset,
                candidateExperimentId,
                modelType,
                modelName,
                promptVersion);

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
        }
        finally
        {
            await transaction.RollbackAsync();
            context.ChangeTracker.Clear();

            Assert.Equal(
                originalRunCount,
                await context.EvaluationRuns.LongCountAsync());
            Assert.Equal(
                originalAcceptanceCount,
                await context.EvaluationAcceptanceResults.LongCountAsync());
        }
    }
}
