namespace AIOps.Abstractions.Configuration;

/// <summary>Root AI service options. ApiKey is NEVER set in appsettings.json —
/// use `dotnet user-secrets` or the AI__NvidiaNim__ApiKey environment variable.</summary>
public sealed class AIOptions
{
    public const string SectionName = "AI";
    /// <summary>"Fake" (default, deterministic, offline) or "Http" (Python/LangGraph service).</summary>
    public string AgentGatewayMode { get; set; } = "Fake";
    public NvidiaNimOptions NvidiaNim { get; set; } = new();
}

public sealed class NvidiaNimOptions
{
    public string BaseUrl { get; set; } = "https://integrate.api.nvidia.com/v1";
    public string Model { get; set; } = "moonshotai/kimi-k3";
    public string ApiKey { get; set; } = ""; // env/user-secrets only — never commit
}

public sealed class ApprovalOptions
{
    public const string SectionName = "Approvals";
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromHours(24);
}

public sealed class AgentPolicyOptions
{
    public const string SectionName = "AgentPolicy";
    public double TriageConfidenceThreshold { get; set; } = 0.6;
    public int MaxAttempts { get; set; } = 2;
}
