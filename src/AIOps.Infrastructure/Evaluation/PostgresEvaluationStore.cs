using AIOps.Abstractions.Evaluation;
using AIOps.Infrastructure.EfCore;
using Microsoft.EntityFrameworkCore;

namespace AIOps.Infrastructure.Evaluation;

/// <summary>PostgreSQL implementation of the evaluation store.</summary>
public sealed class PostgresEvaluationStore : IEvaluationStore
{
    private readonly AIOpsDbContext _context;

    public PostgresEvaluationStore(AIOpsDbContext context) => _context = context;

    public async Task RecordRunAsync(ExperimentRunRecord run, CancellationToken ct = default)
    {
        var entity = new EvaluationRecordEntity
        {
            Id = run.Id,
            ExperimentId = run.ExperimentId,
            ModelType = run.ModelType,
            ModelName = run.ModelName,
            PromptVersion = run.PromptVersion,
            DatasetName = run.DatasetName,
            DatasetVersion = run.DatasetVersion,
            ConfigJson = run.ConfigJson,
            SampleCount = run.SampleCount,
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            MetricsJson = run.MetricsJson,
            Notes = run.Notes
        };

        _context.EvaluationRuns.Add(entity);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ExperimentRunRecord>> ListRunsAsync(
        string? experimentId = null, string? datasetName = null, CancellationToken ct = default)
    {
        var query = _context.EvaluationRuns.AsQueryable();
        if (experimentId is not null) query = query.Where(e => e.ExperimentId == experimentId);
        if (datasetName is not null) query = query.Where(e => e.DatasetName == datasetName);

        return await query
            .OrderByDescending(e => e.StartedAt)
            .Select(e => new ExperimentRunRecord(
                e.Id,
                e.ExperimentId,
                e.ModelType,
                e.ModelName,
                e.PromptVersion,
                e.DatasetName,
                e.DatasetVersion,
                e.ConfigJson,
                e.SampleCount,
                e.StartedAt,
                e.FinishedAt,
                e.MetricsJson,
                e.Notes))
            .ToListAsync(ct);
    }
}