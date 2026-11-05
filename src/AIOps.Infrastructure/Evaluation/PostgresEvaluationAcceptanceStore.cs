using System.Data.Common;
using AIOps.Abstractions.Evaluation;
using AIOps.Infrastructure.EfCore;
using Microsoft.EntityFrameworkCore;

namespace AIOps.Infrastructure.Evaluation;

public sealed class PostgresEvaluationAcceptanceStore(
    AIOpsDbContext context) : IEvaluationAcceptanceStore
{
    public async Task RecordAsync(
        EvaluationAcceptanceRecord result,
        CancellationToken cancellationToken = default)
    {
        var entity = new EvaluationAcceptanceRecordEntity
        {
            Id = result.Id,
            EvaluatedAt = result.EvaluatedAt,
            BaselineExperimentId = result.BaselineExperimentId,
            CandidateExperimentId = result.CandidateExperimentId,
            BaselineRunId = result.BaselineRunId,
            CandidateRunId = result.CandidateRunId,
            PolicyName = result.PolicyName,
            PolicyJson = result.PolicyJson,
            Status = result.Status.ToString(),
            IssuesJson = result.IssuesJson,
            MetricsJson = result.MetricsJson
        };

        context.EvaluationAcceptanceResults.Add(entity);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new EvaluationAcceptancePersistenceException(
                "The evaluation acceptance result could not be persisted.",
                exception);
        }
        catch (DbException exception)
        {
            throw new EvaluationAcceptancePersistenceException(
                "The evaluation acceptance result could not be persisted.",
                exception);
        }
    }

    public async Task<EvaluationAcceptanceRecord?> GetAsync(
        Guid resultId,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.EvaluationAcceptanceResults
            .AsNoTracking()
            .SingleOrDefaultAsync(
                result => result.Id == resultId,
                cancellationToken);

        if (entity is null)
        {
            return null;
        }

        if (!Enum.TryParse<EvaluationAcceptanceStatus>(
                entity.Status,
                ignoreCase: false,
                out var status) ||
            !Enum.IsDefined(status))
        {
            throw new EvaluationAcceptancePersistenceException(
                "The persisted evaluation acceptance status is invalid.");
        }

        return new EvaluationAcceptanceRecord(
            entity.Id,
            entity.EvaluatedAt,
            entity.BaselineExperimentId,
            entity.CandidateExperimentId,
            entity.BaselineRunId,
            entity.CandidateRunId,
            entity.PolicyName,
            entity.PolicyJson,
            status,
            entity.IssuesJson,
            entity.MetricsJson);
    }
}

public sealed class UnavailableEvaluationAcceptanceStore
    : IEvaluationAcceptanceStore
{
    public Task RecordAsync(
        EvaluationAcceptanceRecord result,
        CancellationToken cancellationToken = default)
    {
        throw new EvaluationAcceptancePersistenceException(
            "Evaluation acceptance persistence is unavailable because PostgreSQL is not configured.");
    }

    public Task<EvaluationAcceptanceRecord?> GetAsync(
        Guid resultId,
        CancellationToken cancellationToken = default)
    {
        throw new EvaluationAcceptancePersistenceException(
            "Evaluation acceptance persistence is unavailable because PostgreSQL is not configured.");
    }
}
