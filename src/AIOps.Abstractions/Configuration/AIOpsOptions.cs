namespace AIOps.Abstractions.Configuration;

/// <summary>
/// Root AI service options.
/// ApiKey is NEVER set in appsettings.json.
/// Use dotnet user-secrets or the AI__NvidiaNim__ApiKey environment variable.
/// </summary>
public sealed class AIOptions
{
    public const string SectionName = "AI";

    /// <summary>
    /// "Fake" (default, deterministic, offline)
    /// or "Http" (Python/LangGraph service).
    /// </summary>
    public string AgentGatewayMode { get; set; } = "Fake";

    public AgentServiceOptions AgentService { get; set; } = new();

    public NvidiaNimOptions NvidiaNim { get; set; } = new();
}

public sealed class AgentServiceOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:8000/";

    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class NvidiaNimOptions
{
    public string BaseUrl { get; set; } =
        "https://integrate.api.nvidia.com/v1";

    public string Model { get; set; } =
        "moonshotai/kimi-k3";

    public string ApiKey { get; set; } = "";
}

public sealed class ApprovalOptions
{
    public const string SectionName = "Approvals";

    public TimeSpan DefaultTtl { get; set; } =
        TimeSpan.FromHours(24);
}

public sealed class AgentPolicyOptions
{
    public const string SectionName = "AgentPolicy";

    public double TriageConfidenceThreshold { get; set; } = 0.6;

    public int MaxAttempts { get; set; } = 2;
}

/// <summary>Cloud provider selection and request timeout configuration.</summary>
public sealed class CloudIntegrationOptions
{
    public const string SectionName = "Integrations:Cloud";

    public string Provider { get; set; } = "Simulated";

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Optional AWS region. When empty, the AWS SDK uses its standard region
    /// configuration chain.
    /// </summary>
    public string? Region { get; set; }
}

/// <summary>Directory provider selection and request timeout configuration.</summary>
public sealed class DirectoryIntegrationOptions
{
    public const string SectionName = "Integrations:Directory";

    public string Provider { get; set; } = "Simulated";

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Optional non-secret Entra tenant identifier.</summary>
    public string? TenantId { get; set; }

    /// <summary>Optional managed identity client identifier.</summary>
    public string? ClientId { get; set; }
}

/// <summary>ITSM provider selection. Provider credentials are intentionally not modeled here.</summary>
public sealed class ItsmIntegrationOptions
{
    public const string SectionName = "Integrations:Itsm";

    public string Provider { get; set; } = "Simulated";

    public int TimeoutSeconds { get; set; } = 30;
}
