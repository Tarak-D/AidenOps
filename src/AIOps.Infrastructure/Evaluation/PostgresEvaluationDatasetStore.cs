using System.Text.Json;
using AIOps.Abstractions.Evaluation;
using AIOps.Infrastructure.EfCore;
using AIOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIOps.Infrastructure.Evaluation;

/// <summary>
/// PostgreSQL implementation of versioned evaluation dataset persistence.
/// </summary>
public sealed class PostgresEvaluationDatasetStore : IEvaluationDatasetStore
{
    private readonly AIOpsDbContext _context;

    public PostgresEvaluationDatasetStore(AIOpsDbContext context)
    {
        _context = context;
    }

    public async Task SaveAsync(
        EvaluationDatasetMetadata metadata,
        EvaluationDataset dataset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(dataset);

        metadata.Validate();

        var entity = new EvaluationDatasetRecordEntity
        {
            Id = Guid.NewGuid(),
            DatasetId = metadata.DatasetId,
            Name = metadata.Name,
            Version = metadata.Version,
            Description = metadata.Description,
            CreatedAt = metadata.CreatedAt ?? DateTimeOffset.UtcNow,
            CasesJson = JsonSerializer.Serialize(dataset.Cases)
        };

        _context.EvaluationDatasets.Add(entity);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<EvaluationDatasetRecord?> GetAsync(
        string datasetId,
        string version,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(datasetId))
        {
            throw new ArgumentException(
                "Dataset id is required.",
                nameof(datasetId));
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException(
                "Dataset version is required.",
                nameof(version));
        }

        var entity = await _context.EvaluationDatasets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                e => e.DatasetId == datasetId &&
                     e.Version == version,
                cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var cases = JsonSerializer.Deserialize<IReadOnlyList<EvaluationCase>>(
            entity.CasesJson) ?? [];

        var metadata = new EvaluationDatasetMetadata(
            entity.DatasetId,
            entity.Name,
            entity.Version,
            entity.Description,
            entity.CreatedAt);

        var dataset = new EvaluationDataset(
            entity.Name,
            entity.Version,
            cases);

        return new EvaluationDatasetRecord(
            metadata,
            dataset);
    }

    public async Task<IReadOnlyList<EvaluationDatasetRecord>> ListAsync(
        string? datasetId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.EvaluationDatasets
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(datasetId))
        {
            query = query.Where(e => e.DatasetId == datasetId);
        }

        var entities = await query
            .OrderBy(e => e.DatasetId)
            .ThenByDescending(e => e.Version)
            .ToListAsync(cancellationToken);

        return entities
            .Select(entity =>
            {
                var cases =
                    JsonSerializer.Deserialize<IReadOnlyList<EvaluationCase>>(
                        entity.CasesJson) ?? [];

                var metadata = new EvaluationDatasetMetadata(
                    entity.DatasetId,
                    entity.Name,
                    entity.Version,
                    entity.Description,
                    entity.CreatedAt);

                var dataset = new EvaluationDataset(
                    entity.Name,
                    entity.Version,
                    cases);

                return new EvaluationDatasetRecord(
                    metadata,
                    dataset);
            })
            .ToList();
    }
}