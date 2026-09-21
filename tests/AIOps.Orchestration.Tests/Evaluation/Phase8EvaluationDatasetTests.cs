using AIOps.Domain;
using AIOps.Orchestration.Evaluation;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class Phase8EvaluationDatasetTests
{
    [Fact]
    public void Dataset_contains_all_phase8_incident_scenarios()
    {
        var dataset = Phase8EvaluationDataset.Create();

        Assert.Equal(
            Phase8EvaluationDataset.Name,
            dataset.Name);

        Assert.Equal(
            Phase8EvaluationDataset.Version,
            dataset.Version);

        Assert.Equal(7, dataset.Cases.Count);

        Assert.Equal(
            new[]
            {
                "cpu-spike",
                "disk-full",
                "vpn-failure",
                "password-compromise",
                "database-unavailable",
                "deployment-failure",
                "network-outage"
            },
            dataset.Cases.Select(c => c.Id));
    }

    [Fact]
    public void Dataset_defines_expected_domain_and_severity_for_every_case()
    {
        var dataset = Phase8EvaluationDataset.Create();

        Assert.All(
            dataset.Cases,
            evaluationCase =>
            {
                Assert.NotEqual(
                    TicketDomain.Unknown,
                    evaluationCase.ExpectedDomain);

                Assert.True(
                    Enum.IsDefined(evaluationCase.ExpectedSeverity));
            });
    }

    [Fact]
    public void Dataset_expected_tools_match_allowed_tools()
    {
        var dataset = Phase8EvaluationDataset.Create();

        foreach (var evaluationCase in dataset.Cases)
        {
            if (evaluationCase.ExpectedToolName is null)
            {
                Assert.Empty(evaluationCase.AllowedTools);
                continue;
            }

            Assert.Contains(
                evaluationCase.AllowedTools,
                tool => tool.Name == evaluationCase.ExpectedToolName);
        }
    }
}