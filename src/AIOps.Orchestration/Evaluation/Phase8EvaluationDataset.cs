using AIOps.Abstractions.Evaluation;
using AIOps.Contracts.AgentGateway;
using AIOps.Domain;

namespace AIOps.Orchestration.Evaluation;

public static class Phase8EvaluationDataset
{
    public const string Name = "phase-8-incident-evaluation";
    public const string Version = "1.0";

    public static EvaluationDataset Create()
    {
        return new EvaluationDataset(
            Name,
            Version,
            new[]
            {
                CreateCpuSpikeCase(),
                CreateDiskFullCase(),
                CreateVpnFailureCase(),
                CreatePasswordCompromiseCase(),
                CreateDatabaseUnavailableCase(),
                CreateDeploymentFailureCase(),
                CreateNetworkOutageCase()
            });
    }

    private static EvaluationCase CreateCpuSpikeCase()
    {
        return CreateCase(
            "cpu-spike",
            "CPU spike on production server",
            "Production server CPU usage is critically high and the service is down.",
            TicketDomain.Infrastructure,
            Severity.P1,
            AgentRunOutcome.AwaitingApproval,
            "Aws.RestartEc2Instance",
            "Restart the affected production instance after human approval.",
            approvalRequired: true,
            RestartInstanceTool());
    }

    private static EvaluationCase CreateDiskFullCase()
    {
        return CreateCase(
            "disk-full",
            "Disk full on server",
            "A server disk is full and requires infrastructure remediation.",
            TicketDomain.Infrastructure,
            Severity.P3,
            AgentRunOutcome.AwaitingApproval,
            "Aws.RestartEc2Instance",
            "Use the infrastructure remediation path after human approval.",
            approvalRequired: true,
            RestartInstanceTool());
    }

    private static EvaluationCase CreateVpnFailureCase()
    {
        return CreateCase(
            "vpn-failure",
            "VPN failure",
            "The VPN is down and remote staff cannot connect to the corporate network.",
            TicketDomain.Network,
            Severity.P1,
            AgentRunOutcome.Resolved,
            "Network.RunVpnDiagnostics",
            "Run VPN diagnostics.",
            approvalRequired: false,
            VpnDiagnosticsTool());
    }

    private static EvaluationCase CreatePasswordCompromiseCase()
    {
        return CreateCase(
            "password-compromise",
            "Password compromise",
            "A user password may be compromised and should be reset.",
            TicketDomain.Identity,
            Severity.P3,
            AgentRunOutcome.AwaitingApproval,
            "Identity.ResetPassword",
            "Reset the compromised user password after human approval.",
            approvalRequired: true,
            ResetPasswordTool());
    }

    private static EvaluationCase CreateDatabaseUnavailableCase()
    {
        return CreateCase(
            "database-unavailable",
            "Database unavailable",
            "The production database is unavailable and requires investigation.",
            TicketDomain.Database,
            Severity.P1,
            AgentRunOutcome.Escalated,
            null,
            "Escalate because the deterministic gateway has no database remediation tool.",
            approvalRequired: false);
    }

    private static EvaluationCase CreateDeploymentFailureCase()
    {
        return CreateCase(
            "deployment-failure",
            "Deployment failure",
            "A production server deployment failed and the server requires remediation.",
            TicketDomain.Infrastructure,
            Severity.P1,
            AgentRunOutcome.AwaitingApproval,
            "Aws.RestartEc2Instance",
            "Restart the affected production instance after human approval.",
            approvalRequired: true,
            RestartInstanceTool());
    }

    private static EvaluationCase CreateNetworkOutageCase()
    {
        return CreateCase(
            "network-outage",
            "Network outage",
            "The office network is down and users cannot connect.",
            TicketDomain.Network,
            Severity.P1,
            AgentRunOutcome.Resolved,
            "Network.RunVpnDiagnostics",
            "Run network diagnostics.",
            approvalRequired: false,
            VpnDiagnosticsTool());
    }

    private static EvaluationCase CreateCase(
        string id,
        string title,
        string description,
        TicketDomain expectedDomain,
        Severity expectedSeverity,
        AgentRunOutcome expectedInitialOutcome,
        string? expectedToolName,
        string expectedResolution,
        bool approvalRequired,
        params ToolManifestEntry[] tools)
    {
        var ticket = new AgentTicketContext(
            Guid.NewGuid(),
            $"EVAL-{id}",
            title,
            description,
            "evaluation@example.com",
            TicketDomain.Unknown,
            Severity.P3,
            TicketStatus.New);

        return new EvaluationCase(
            id,
            ticket,
            tools,
            expectedDomain,
            expectedSeverity,
            expectedInitialOutcome,
            expectedToolName,
            expectedResolution,
            approvalRequired);
    }

    private static ToolManifestEntry RestartInstanceTool()
    {
        return new ToolManifestEntry(
            "Aws.RestartEc2Instance",
            "Restart an EC2 instance.",
            RiskLevel.Moderate,
            true,
            "{}");
    }

    private static ToolManifestEntry VpnDiagnosticsTool()
    {
        return new ToolManifestEntry(
            "Network.RunVpnDiagnostics",
            "Run VPN diagnostics.",
            RiskLevel.Safe,
            false,
            "{}");
    }

    private static ToolManifestEntry ResetPasswordTool()
    {
        return new ToolManifestEntry(
            "Identity.ResetPassword",
            "Reset a user password.",
            RiskLevel.Sensitive,
            true,
            "{}");
    }
}