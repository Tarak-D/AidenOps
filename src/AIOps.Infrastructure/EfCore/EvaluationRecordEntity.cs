using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIOps.Infrastructure.EfCore;

/// <summary>EF Core entity for evaluation/experiment runs.</summary>
[Table("evaluation_runs")]
public sealed class EvaluationRecordEntity
{
    [Key, Column("id")]
    public Guid Id { get; set; }

    [Column("experiment_id"), MaxLength(200)]
    public string ExperimentId { get; set; } = "";

    [Column("model_type"), MaxLength(50)]
    public string ModelType { get; set; } = "";

    [Column("model_name"), MaxLength(200)]
    public string ModelName { get; set; } = "";

    [Column("prompt_version"), MaxLength(200)]
    public string? PromptVersion { get; set; }

    [Column("dataset_name"), MaxLength(200)]
    public string DatasetName { get; set; } = "";

    [Column("dataset_version"), MaxLength(50)]
    public string DatasetVersion { get; set; } = "";

    [Column("config_json")]
    public string ConfigJson { get; set; } = "";

    [Column("sample_count")]
    public int SampleCount { get; set; }

    [Column("started_at")]
    public DateTimeOffset StartedAt { get; set; }

    [Column("finished_at")]
    public DateTimeOffset? FinishedAt { get; set; }

    [Column("metrics_json")]
    public string MetricsJson { get; set; } = "";

    [Column("notes")]
    public string? Notes { get; set; }
}