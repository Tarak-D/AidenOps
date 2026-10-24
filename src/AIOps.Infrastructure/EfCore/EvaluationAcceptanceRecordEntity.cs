using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIOps.Infrastructure.EfCore;

[Table("evaluation_acceptance_results")]
public sealed class EvaluationAcceptanceRecordEntity
{
    [Key, Column("id")]
    public Guid Id { get; set; }

    [Column("evaluated_at")]
    public DateTimeOffset EvaluatedAt { get; set; }

    [Column("baseline_experiment_id"), MaxLength(200)]
    public string BaselineExperimentId { get; set; } = "";

    [Column("candidate_experiment_id"), MaxLength(200)]
    public string CandidateExperimentId { get; set; } = "";

    [Column("baseline_run_id")]
    public Guid? BaselineRunId { get; set; }

    [Column("candidate_run_id")]
    public Guid? CandidateRunId { get; set; }

    [Column("policy_name"), MaxLength(200)]
    public string PolicyName { get; set; } = "";

    [Column("policy_json")]
    public string PolicyJson { get; set; } = "";

    [Column("status"), MaxLength(20)]
    public string Status { get; set; } = "";

    [Column("issues_json")]
    public string IssuesJson { get; set; } = "";

    [Column("metrics_json")]
    public string MetricsJson { get; set; } = "";
}
