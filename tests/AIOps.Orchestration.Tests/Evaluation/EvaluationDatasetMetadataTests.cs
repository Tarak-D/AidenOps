using AIOps.Abstractions.Evaluation;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class EvaluationDatasetMetadataTests
{
    [Fact]
    public void Validate_AllowsValidMetadata()
    {
        var metadata = new EvaluationDatasetMetadata(
            DatasetId: "phase-15-triage",
            Name: "Phase 15 Triage Golden Dataset",
            Version: "1.0",
            Description: "Golden dataset for triage evaluation.",
            CreatedAt: DateTimeOffset.UtcNow);

        metadata.Validate();
    }

    [Fact]
    public void Validate_AllowsDifferentVersionsForSameDataset()
    {
        var versionOne = new EvaluationDatasetMetadata(
            DatasetId: "phase-15-triage",
            Name: "Phase 15 Triage Golden Dataset",
            Version: "1.0");

        var versionTwo = new EvaluationDatasetMetadata(
            DatasetId: "phase-15-triage",
            Name: "Phase 15 Triage Golden Dataset",
            Version: "1.1");

        versionOne.Validate();
        versionTwo.Validate();

        Assert.NotEqual(versionOne.Version, versionTwo.Version);
        Assert.Equal(versionOne.DatasetId, versionTwo.DatasetId);
    }

    [Fact]
    public void Validate_RejectsMissingDatasetId()
    {
        var metadata = new EvaluationDatasetMetadata(
            DatasetId: "",
            Name: "Test Dataset",
            Version: "1.0");

        Assert.Throws<ArgumentException>(
            () => metadata.Validate());
    }

    [Fact]
    public void Validate_RejectsMissingName()
    {
        var metadata = new EvaluationDatasetMetadata(
            DatasetId: "test",
            Name: "",
            Version: "1.0");

        Assert.Throws<ArgumentException>(
            () => metadata.Validate());
    }

    [Fact]
    public void Validate_RejectsMissingVersion()
    {
        var metadata = new EvaluationDatasetMetadata(
            DatasetId: "test",
            Name: "Test Dataset",
            Version: "");

        Assert.Throws<ArgumentException>(
            () => metadata.Validate());
    }
}