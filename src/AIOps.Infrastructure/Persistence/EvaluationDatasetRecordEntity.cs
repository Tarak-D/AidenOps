using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIOps.Infrastructure.Persistence;

[Table("evaluation_datasets")]
public sealed class EvaluationDatasetRecordEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Required]
    [Column("dataset_id")]
    [MaxLength(100)]
    public string DatasetId { get; set; } = string.Empty;

    [Required]
    [Column("name")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Column("version")]
    [MaxLength(50)]
    public string Version { get; set; } = string.Empty;

    [Column("description")]
    [MaxLength(2000)]
    public string? Description { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Required]
    [Column("cases_json")]
    public string CasesJson { get; set; } = string.Empty;
}