namespace AIOps.Abstractions.Evaluation;

public sealed record EvaluationDatasetMetadata(
    string DatasetId,
    string Name,
    string Version,
    string? Description = null,
    DateTimeOffset? CreatedAt = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DatasetId))
        {
            throw new ArgumentException(
                "Dataset id is required.",
                nameof(DatasetId));
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException(
                "Dataset name is required.",
                nameof(Name));
        }

        if (string.IsNullOrWhiteSpace(Version))
        {
            throw new ArgumentException(
                "Dataset version is required.",
                nameof(Version));
        }
    }
}