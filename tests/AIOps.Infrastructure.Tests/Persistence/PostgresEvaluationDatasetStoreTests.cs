using AIOps.Abstractions.Evaluation;
using AIOps.Contracts.AgentGateway;
using AIOps.Domain;
using AIOps.Infrastructure.EfCore;
using AIOps.Infrastructure.Evaluation;
using Microsoft.EntityFrameworkCore;

namespace AIOps.Infrastructure.Tests.Persistence;

public sealed class PostgresEvaluationDatasetStoreTests
{
    [Fact]
    public async Task SaveAndGetAsync_PersistsVersionedDataset()
    {
        var options = new DbContextOptionsBuilder<AIOpsDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=aios;Username=aios;Password=aiospw",
                npgsqlOptions => npgsqlOptions.UseVector())
            .Options;

        await using var context = new AIOpsDbContext(options);

        var store = new PostgresEvaluationDatasetStore(context);

        var metadata = new EvaluationDatasetMetadata(
            DatasetId: $"golden-incidents-{Guid.NewGuid():N}",
            Name: "Golden Incident Dataset",
            Version: "1.0.0",
            Description: "Phase 15.2 golden evaluation dataset",
            CreatedAt: DateTimeOffset.UtcNow);

        var ticket = new AgentTicketContext(
            Guid.NewGuid(),
            "EVAL-case-001",
            "Database unavailable",
            "The production database is unavailable.",
            "evaluation@example.com",
            TicketDomain.Unknown,
            Severity.P3,
            TicketStatus.New);

        var tool = new ToolManifestEntry(
            "Database.HealthCheck",
            "Check database health.",
            RiskLevel.Safe,
            false,
            "{}");

        var evaluationCase = new EvaluationCase(
            Id: "case-001",
            Ticket: ticket,
            AllowedTools: new[] { tool },
            ExpectedDomain: TicketDomain.Database,
            ExpectedSeverity: Severity.P1,
            ExpectedInitialOutcome: AgentRunOutcome.Escalated,
            ExpectedToolName: "Database.HealthCheck",
            ExpectedResolution: "Database restored",
            ExpectedApprovalRequired: false);

        var dataset = new EvaluationDataset(
            metadata.Name,
            metadata.Version,
            new[] { evaluationCase });

        await store.SaveAsync(metadata, dataset);

        var result = await store.GetAsync(
            metadata.DatasetId,
            "1.0.0");

        Assert.NotNull(result);
        Assert.Equal(metadata.DatasetId, result.Metadata.DatasetId);
        Assert.Equal("Golden Incident Dataset", result.Metadata.Name);
        Assert.Equal("1.0.0", result.Metadata.Version);
        Assert.Single(result.Dataset.Cases);

        var savedCase = result.Dataset.Cases[0];

        Assert.Equal("case-001", savedCase.Id);
        Assert.Equal(TicketDomain.Database, savedCase.ExpectedDomain);
        Assert.Equal(Severity.P1, savedCase.ExpectedSeverity);
        Assert.Equal(
            "Database.HealthCheck",
            savedCase.ExpectedToolName);
    }

    [Fact]
    public async Task ListAsync_FiltersByDatasetIdAndReturnsVersions()
    {
        var options = new DbContextOptionsBuilder<AIOpsDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=aios;Username=aios;Password=aiospw",
                npgsqlOptions => npgsqlOptions.UseVector())
            .Options;

        await using var context = new AIOpsDbContext(options);

        var store = new PostgresEvaluationDatasetStore(context);

        var ticket = new AgentTicketContext(
            Guid.NewGuid(),
            "EVAL-case-002",
            "CPU spike",
            "CPU usage is critically high.",
            "evaluation@example.com",
            TicketDomain.Unknown,
            Severity.P3,
            TicketStatus.New);

        var tool = new ToolManifestEntry(
            "Infrastructure.CpuCheck",
            "Check CPU health.",
            RiskLevel.Safe,
            false,
            "{}");

        var evaluationCase = new EvaluationCase(
            Id: "case-002",
            Ticket: ticket,
            AllowedTools: new[] { tool },
            ExpectedDomain: TicketDomain.Infrastructure,
            ExpectedSeverity: Severity.P1,
            ExpectedInitialOutcome: AgentRunOutcome.Resolved,
            ExpectedToolName: "Infrastructure.CpuCheck",
            ExpectedResolution: "CPU stabilized",
            ExpectedApprovalRequired: false);

        var cases = new[] { evaluationCase };

        var datasetId = $"golden-incidents-{Guid.NewGuid():N}";

        await store.SaveAsync(
            new EvaluationDatasetMetadata(
                datasetId,
                "Golden Incident Dataset",
                "1.0.0"),
            new EvaluationDataset(
                "Golden Incident Dataset",
                "1.0.0",
                cases));

        await store.SaveAsync(
            new EvaluationDatasetMetadata(
                datasetId,
                "Golden Incident Dataset",
                "2.0.0"),
            new EvaluationDataset(
                "Golden Incident Dataset",
                "2.0.0",
                cases));

        var otherDatasetId = $"other-dataset-{Guid.NewGuid():N}";

        await store.SaveAsync(
            new EvaluationDatasetMetadata(
                otherDatasetId,
                "Other Dataset",
                "1.0.0"),
            new EvaluationDataset(
                "Other Dataset",
                "1.0.0",
                cases));

        var result = await store.ListAsync(datasetId);

        Assert.Equal(2, result.Count);

        Assert.All(
            result,
            item => Assert.Equal(
                datasetId,
                item.Metadata.DatasetId));

        Assert.Contains(
            result,
            item => item.Metadata.Version == "1.0.0");

        Assert.Contains(
            result,
            item => item.Metadata.Version == "2.0.0");
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForUnknownDatasetVersion()
    {
        var options = new DbContextOptionsBuilder<AIOpsDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=aios;Username=aios;Password=aiospw",
                npgsqlOptions => npgsqlOptions.UseVector())
            .Options;

        await using var context = new AIOpsDbContext(options);

        var store = new PostgresEvaluationDatasetStore(context);

        var result = await store.GetAsync(
            "does-not-exist",
            "1.0.0");

        Assert.Null(result);
    }
}