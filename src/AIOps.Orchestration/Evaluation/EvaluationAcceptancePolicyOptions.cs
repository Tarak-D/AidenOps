using AIOps.Abstractions.Evaluation;

namespace AIOps.Orchestration.Evaluation;

public sealed class EvaluationAcceptancePolicyOptions
{
    public const string SectionName = "EvaluationAcceptance";

    public string? Name { get; set; }

    public List<EvaluationAcceptanceMetricRuleOptions>? RequiredMetrics { get; set; } = [];

    public EvaluationAcceptancePolicy CreatePolicy()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 200)
        {
            throw new EvaluationAcceptancePolicyConfigurationException(
                "A valid server-side evaluation acceptance policy is not configured.");
        }

        if (RequiredMetrics is null)
        {
            throw new EvaluationAcceptancePolicyConfigurationException(
                "A valid server-side evaluation acceptance policy is not configured.");
        }

        var rules = new List<EvaluationMetricAcceptanceRule>(
            RequiredMetrics.Count);

        foreach (var rule in RequiredMetrics)
        {
            if (rule is null ||
                !Enum.TryParse<EvaluationMetricDirection>(
                    rule.Direction,
                    ignoreCase: true,
                    out var direction) ||
                !Enum.IsDefined(direction) ||
                rule.AllowedRegression is null)
            {
                throw new EvaluationAcceptancePolicyConfigurationException(
                    "A valid server-side evaluation acceptance policy is not configured.");
            }

            rules.Add(new EvaluationMetricAcceptanceRule(
                rule.MetricName!,
                direction,
                rule.AllowedRegression.Value));
        }

        try
        {
            return new EvaluationAcceptancePolicy(Name, rules)
                .CreateValidatedSnapshot();
        }
        catch (ArgumentException exception)
        {
            throw new EvaluationAcceptancePolicyConfigurationException(
                "A valid server-side evaluation acceptance policy is not configured.",
                exception);
        }
    }
}

public sealed class EvaluationAcceptanceMetricRuleOptions
{
    public string? MetricName { get; set; }

    public string? Direction { get; set; }

    public decimal? AllowedRegression { get; set; }
}

public sealed class EvaluationAcceptancePolicyConfigurationException
    : Exception
{
    public EvaluationAcceptancePolicyConfigurationException(string message)
        : base(message)
    {
    }

    public EvaluationAcceptancePolicyConfigurationException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
