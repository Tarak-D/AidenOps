namespace AIOps.Abstractions.Evaluation;

public sealed record EvaluationRunConfiguration(
    string ExperimentId,
    EvaluationModelMetadata Model,
    string DatasetName,
    string DatasetVersion,
    int? MaxCases = null,
    bool Deterministic = true,
    bool ExecuteExternalTools = false,
    string? Notes = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ExperimentId))
        {
            throw new ArgumentException(
                "Experiment id is required.",
                nameof(ExperimentId));
        }

        if (string.IsNullOrWhiteSpace(DatasetName))
        {
            throw new ArgumentException(
                "Dataset name is required.",
                nameof(DatasetName));
        }

        if (string.IsNullOrWhiteSpace(DatasetVersion))
        {
            throw new ArgumentException(
                "Dataset version is required.",
                nameof(DatasetVersion));
        }

        if (MaxCases is <= 0)
        {
            throw new ArgumentException(
                "Max cases must be greater than zero when specified.",
                nameof(MaxCases));
        }

        Model.Validate();
    }
}