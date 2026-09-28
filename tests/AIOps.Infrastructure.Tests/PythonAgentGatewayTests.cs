using System.Net;
using System.Text;
using System.Text.Json;
using AIOps.Contracts.AgentGateway;
using AIOps.Domain;
using AIOps.Infrastructure.Agents;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIOps.Infrastructure.Tests;

public sealed class PythonAgentGatewayTests
{
    [Fact]
    public async Task StartRun_sends_server_tool_manifest_and_maps_proposal()
    {
        var handler = new CapturingHandler("""
            {
              "correlation_id":"00000000-0000-0000-0000-000000000001",
              "outcome":"ProposalCreated",
              "triage":{"domain":"Identity","severity":"P2","confidence":0.9},
              "tool_proposal":{"tool_name":"Directory.ResetPassword","arguments_json":"{\"userPrincipalName\":\"user@example.com\"}","confidence":0.88,"justification":"Password reset requested."},
              "trace":[],
              "error":null
            }
            """);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://agent.test/")
        };
        var gateway = new PythonAgentGateway(
            client,
            NullLogger<PythonAgentGateway>.Instance);
        var request = new AgentRunRequest(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            new AgentTicketContext(
                Guid.Parse("00000000-0000-0000-0000-000000000002"),
                "INC-42",
                "Reset password",
                "User cannot sign in",
                "user@example.com",
                TicketDomain.Identity,
                Severity.P2,
                TicketStatus.New),
            [new ToolManifestEntry(
                "Directory.ResetPassword",
                "Resets a directory password.",
                RiskLevel.Sensitive,
                true,
                "{\"type\":\"object\",\"properties\":{\"userPrincipalName\":{\"type\":\"string\"}},\"required\":[\"userPrincipalName\"],\"additionalProperties\":false}")],
            0.6,
            2);

        var result = await gateway.StartRunAsync(request);

        Assert.Equal(AgentRunOutcome.ProposalCreated, result.Outcome);
        Assert.Equal("Directory.ResetPassword", result.Proposal!.ToolName);
        Assert.Contains("userPrincipalName", result.Proposal.ArgumentsJson);

        using var payload = JsonDocument.Parse(handler.RequestBody!);
        var tool = payload.RootElement.GetProperty("allowed_tools")[0];
        Assert.Equal("Directory.ResetPassword", tool.GetProperty("name").GetString());
        Assert.Equal("Sensitive", tool.GetProperty("risk").GetString());
        Assert.True(tool.GetProperty("requires_approval").GetBoolean());
        Assert.Contains("userPrincipalName", tool.GetProperty("input_schema_json").GetString());
        Assert.Equal(0.6, payload.RootElement.GetProperty("triage_confidence_threshold").GetDouble());
        Assert.Equal(2, payload.RootElement.GetProperty("max_attempts").GetInt32());
    }

    [Fact]
    public async Task ResumeRun_sends_server_derived_action_state()
    {
        var handler = new CapturingHandler("""
            {"correlation_id":"00000000-0000-0000-0000-000000000001","outcome":"Escalated","trace":[]}
            """);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://agent.test/") };
        var gateway = new PythonAgentGateway(client, NullLogger<PythonAgentGateway>.Instance);
        var actionId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var ticketId = Guid.Parse("00000000-0000-0000-0000-000000000002");

        await gateway.ResumeRunAsync(new ResumeAgentRunRequest(
            actionId,
            ticketId,
            false,
            "reviewer@example.com",
            "persisted rejection",
            false,
            actionId,
            "Rejected",
            "Rejected",
            "persisted rejection"));

        using var payload = JsonDocument.Parse(handler.RequestBody!);
        var root = payload.RootElement;
        Assert.Equal(actionId.ToString(), root.GetProperty("action_execution_id").GetString());
        Assert.Equal("Rejected", root.GetProperty("approval_status").GetString());
        Assert.Equal("Rejected", root.GetProperty("action_status").GetString());
        Assert.Equal("reviewer@example.com", root.GetProperty("approval_decided_by").GetString());
        Assert.Equal("persisted rejection", root.GetProperty("tool_execution_error").GetString());
    }

    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
