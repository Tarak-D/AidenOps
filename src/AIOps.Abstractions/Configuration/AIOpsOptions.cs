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

/// <summary>ITSM provider selection and timeout configuration.</summary>
public sealed class ItsmIntegrationOptions
{
    public const string SectionName = "Integrations:Itsm";

    public string Provider { get; set; } = "Simulated";

    public int TimeoutSeconds { get; set; } = 30;

    public ServiceNowItsmOptions ServiceNow { get; set; } = new();

    public JiraServiceManagementItsmOptions JiraServiceManagement { get; set; } = new();

    public ZendeskItsmOptions Zendesk { get; set; } = new();
}

/// <summary>Server-side Cisco Secure Access VPN API configuration.</summary>
public sealed class CiscoSecureAccessOptions
{
    public const string SectionName = "Integrations:NetworkDiagnostics:CiscoSecureAccess";

    public string BaseUrl { get; set; } = "https://api.sse.cisco.com/";

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>Server-side Palo Alto GlobalProtect VPN API configuration.</summary>
public sealed class PaloAltoGlobalProtectOptions
{
    public const string SectionName =
        "Integrations:NetworkDiagnostics:PaloAltoGlobalProtect";

    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>Server-side Fortinet FortiGate VPN API configuration.</summary>
public sealed class FortinetVpnOptions
{
    public const string SectionName =
        "Integrations:NetworkDiagnostics:Fortinet";

    public string? BaseUrl { get; set; }

    public string? ApiToken { get; set; }

    public string Vdom { get; set; } = "root";

    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Server-side Cisco ThousandEyes network diagnostics API configuration.
/// ApiToken must be supplied through user-secrets, a secret store,
/// or environment configuration.
/// </summary>
public sealed class CiscoThousandEyesOptions
{
    public const string SectionName =
        "Integrations:NetworkDiagnostics:CiscoThousandEyes";

    public string BaseUrl { get; set; } =
        "https://api.thousandeyes.com/v7/";

    public string? ApiToken { get; set; }

    /// <summary>
    /// Optional ThousandEyes account-group identifier.
    /// </summary>
    public string? AccountGroupId { get; set; }

    /// <summary>
    /// Server-side ThousandEyes agent ID used for active tests.
    /// </summary>
    public string? AgentId { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Number of bounded result polling attempts after an instant test.
    /// </summary>
    public int ResultPollAttempts { get; set; } = 5;

    /// <summary>
    /// Delay between bounded result polling attempts.
    /// </summary>
    public int ResultPollIntervalMilliseconds { get; set; } = 1000;
}

// ADD THIS BLANK LINE

/// <summary>
/// Server-side ServiceNow OAuth client configuration. ClientSecret must be supplied
/// through user-secrets, a secret store, or environment configuration.
/// Server-side ServiceNow OAuth client configuration. ClientSecret must be supplied
/// through user-secrets, a secret store, or environment configuration.
/// </summary>
public sealed class ServiceNowItsmOptions
{
    public string? BaseUrl { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }
}

/// <summary>Server-side Jira Cloud credentials and project/workflow configuration.</summary>
public sealed class JiraServiceManagementItsmOptions
{
    public string? BaseUrl { get; set; }

    public string? Email { get; set; }

    public string? ApiToken { get; set; }

    public string? ProjectKey { get; set; }

    public string? IssueType { get; set; }

    public int? TimeoutSeconds { get; set; }

    /// <summary>Configured target status names for supported normalized states.</summary>
    public Dictionary<string, string> StateMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Open"] = "Open",
        ["InProgress"] = "In Progress",
        ["Resolved"] = "Resolved",
        ["Closed"] = "Closed"
    };
}

/// <summary>Server-side Zendesk API token configuration.</summary>
public sealed class ZendeskItsmOptions
{
    public string? BaseUrl { get; set; }

    public string? Email { get; set; }

    public string? ApiToken { get; set; }

    public int? TimeoutSeconds { get; set; }
}
