using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
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
            {"correlation_id":"00000000-0000-0000-0000-000000000003","outcome":"Escalated","triage":{},"tool_proposal":null,"trace":[{"agent":"ValidationAgent","step":"resume","model":"deterministic/server-result","prompt_version":"v1-server-authoritative-resume","prompt_tokens":0,"completion_tokens":0,"latency_ms":0.0,"summary":"Action rejected."}]}
            """);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://agent.test/") };
        var gateway = new PythonAgentGateway(client, NullLogger<PythonAgentGateway>.Instance);
        var actionId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var ticketId = Guid.Parse("00000000-0000-0000-0000-000000000002");

        var result = await gateway.ResumeRunAsync(new ResumeAgentRunRequest(
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

        Assert.Equal(AgentRunOutcome.Escalated, result.Outcome);

        using var payload = JsonDocument.Parse(handler.RequestBody!);
        var root = payload.RootElement;
        Assert.Equal(actionId.ToString(), root.GetProperty("action_execution_id").GetString());
        Assert.Equal("Rejected", root.GetProperty("approval_status").GetString());
        Assert.Equal("Rejected", root.GetProperty("action_status").GetString());
        Assert.Equal("reviewer@example.com", root.GetProperty("approval_decided_by").GetString());
        Assert.Equal("persisted rejection", root.GetProperty("tool_execution_error").GetString());
    }

    [Fact]
    public async Task StartRun_returns_failure_for_http_errors()
    {
        var handler = new CapturingHandler(
            """{"detail":"unavailable"}""",
            HttpStatusCode.ServiceUnavailable);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://agent.test/")
        };
        var gateway = new PythonAgentGateway(
            client,
            NullLogger<PythonAgentGateway>.Instance);

        var result = await gateway.StartRunAsync(CreateStartRequest());

        Assert.Equal(AgentRunOutcome.Failed, result.Outcome);
        Assert.Contains("HTTP 503", result.Error);
    }

    [Fact]
    public async Task StartRun_returns_failure_for_invalid_responses()
    {
        var invalidResponses = new[]
        {
            "not-json",
            """{"correlation_id":"00000000-0000-0000-0000-000000000001","outcome":"Resolved"}""",
            """
            {
              "correlation_id":"00000000-0000-0000-0000-000000000099",
              "outcome":"Resolved",
              "triage":{"domain":"Network","severity":"P1","confidence":0.75},
              "trace":[]
            }
            """,
            """
            {
              "correlation_id":"00000000-0000-0000-0000-000000000001",
              "outcome":"Resolved",
              "triage":{"domain":"not-a-domain","severity":"P3","confidence":0.35},
              "trace":[]
            }
            """
        };

        foreach (var responseJson in invalidResponses)
        {
            using var client = new HttpClient(
                new CapturingHandler(responseJson))
            {
                BaseAddress = new Uri("http://agent.test/")
            };
            var gateway = new PythonAgentGateway(
                client,
                NullLogger<PythonAgentGateway>.Instance);

            var result = await gateway.StartRunAsync(CreateStartRequest());

            Assert.Equal(AgentRunOutcome.Failed, result.Outcome);
            Assert.Null(result.ResolutionSummary);
        }
    }

    [Fact]
    public async Task StartRun_rejects_outcomes_not_emitted_by_the_python_endpoint()
    {
        foreach (var outcome in new[]
                 {
                     "Unknown",
                     "AwaitingApproval",
                     "Failed"
                 })
        {
            var result = await StartRunWithResponseAsync(
                CreateResponseJson(outcome));

            Assert.Equal(AgentRunOutcome.Failed, result.Outcome);
        }
    }

    [Fact]
    public async Task StartRun_rejects_invalid_severity_values()
    {
        foreach (var severity in new[] { "invalid", "99", "" })
        {
            var triageJson =
                $$"""{"domain":"Network","severity":{{JsonSerializer.Serialize(severity)}},"confidence":0.75}""";
            var result = await StartRunWithResponseAsync(
                CreateResponseJson(triageJson: triageJson));

            Assert.Equal(AgentRunOutcome.Failed, result.Outcome);
        }
    }

    [Fact]
    public async Task StartRun_rejects_confidence_outside_finite_unit_interval()
    {
        foreach (var confidence in new[]
                 {
                     "-0.01",
                     "1.01",
                     "NaN",
                     "Infinity",
                     "-Infinity"
                 })
        {
            var triageJson =
                $$"""{"domain":"Network","severity":"P1","confidence":{{confidence}}}""";
            var result = await StartRunWithResponseAsync(
                CreateResponseJson(triageJson: triageJson));

            Assert.Equal(AgentRunOutcome.Failed, result.Outcome);
        }

        var zero = await StartRunWithResponseAsync(
            CreateResponseJson(confidence: "0"));
        var one = await StartRunWithResponseAsync(
            CreateResponseJson(confidence: "1"));
        Assert.Equal(AgentRunOutcome.Resolved, zero.Outcome);
        Assert.Equal(AgentRunOutcome.Resolved, one.Outcome);
    }

    [Fact]
    public async Task StartRun_rejects_missing_required_triage_fields()
    {
        var incompleteTriage = new[]
        {
            """{"severity":"P1","confidence":0.75}""",
            """{"domain":"Network","confidence":0.75}""",
            """{"domain":"Network","severity":"P1"}""",
            """{"domain":null,"severity":"P1","confidence":0.75}""",
            """{"domain":"Network","severity":null,"confidence":0.75}""",
            """{"domain":"Network","severity":"P1","confidence":null}"""
        };

        foreach (var triageJson in incompleteTriage)
        {
            var result = await StartRunWithResponseAsync(
                CreateResponseJson(triageJson: triageJson));

            Assert.Equal(AgentRunOutcome.Failed, result.Outcome);
        }
    }

    [Fact]
    public async Task StartRun_rejects_missing_required_proposal_fields()
    {
        var incompleteProposals = new[]
        {
            CreateProposalJson(omittedField: "tool_name"),
            CreateProposalJson(omittedField: "arguments_json"),
            CreateProposalJson(omittedField: "confidence"),
            CreateProposalJson(omittedField: "justification"),
            CreateProposalJson(toolName: null),
            CreateProposalJson(argumentsJson: null),
            CreateProposalJson(confidence: null),
            CreateProposalJson(justification: null)
        };

        foreach (var proposalJson in incompleteProposals)
        {
            var result = await StartRunWithResponseAsync(
                CreateResponseJson(
                    outcome: "ProposalCreated",
                    proposalJson: proposalJson));

            Assert.Equal(AgentRunOutcome.Failed, result.Outcome);
        }
    }

    [Fact]
    public void ResponseValidation_rejects_non_finite_triage_and_proposal_confidence()
    {
        foreach (var nonFinite in new[]
                 {
                     double.NaN,
                     double.PositiveInfinity,
                     double.NegativeInfinity
                 })
        {
            var triageError = ValidateResponseDirectly(
                triageConfidence: nonFinite,
                proposalConfidence: null);
            Assert.NotNull(triageError);

            var proposalError = ValidateResponseDirectly(
                triageConfidence: 0.75,
                proposalConfidence: nonFinite);
            Assert.NotNull(proposalError);
        }
    }

    [Fact]
    public async Task StartRun_rejects_invalid_trace_fields_and_usage()
    {
        var validTrace =
            """[{"agent":"TriageAgent","step":"triage","model":"deterministic/bootstrap","prompt_version":"v1","prompt_tokens":0,"completion_tokens":0,"latency_ms":0.0,"summary":"Deterministic."}]""";
        var invalidTraces = new[]
        {
            validTrace.Replace("\"agent\":\"TriageAgent\"", "\"agent\":\"\"", StringComparison.Ordinal),
            validTrace.Replace("\"step\":\"triage\"", "\"step\":\"\"", StringComparison.Ordinal),
            validTrace.Replace("\"model\":\"deterministic/bootstrap\"", "\"model\":\"\"", StringComparison.Ordinal),
            validTrace.Replace("\"prompt_version\":\"v1\"", "\"prompt_version\":\"\"", StringComparison.Ordinal),
            validTrace.Replace("\"summary\":\"Deterministic.\"", "\"summary\":\"\"", StringComparison.Ordinal),
            validTrace.Replace("\"prompt_tokens\":0", "\"prompt_tokens\":-1", StringComparison.Ordinal),
            validTrace.Replace("\"completion_tokens\":0", "\"completion_tokens\":-1", StringComparison.Ordinal),
            validTrace.Replace("\"latency_ms\":0.0", "\"latency_ms\":-0.1", StringComparison.Ordinal),
            validTrace.Replace("\"latency_ms\":0.0", "\"latency_ms\":NaN", StringComparison.Ordinal),
            validTrace.Replace("\"latency_ms\":0.0", "\"latency_ms\":Infinity", StringComparison.Ordinal)
        };

        foreach (var traceJson in invalidTraces)
        {
            var result = await StartRunWithResponseAsync(
                CreateResponseJson(traceJson: traceJson));

            Assert.Equal(AgentRunOutcome.Failed, result.Outcome);
        }
    }

    [Fact]
    public async Task StartRun_rejects_invalid_tool_proposal_fields()
    {
        var invalidProposals = new[]
        {
            CreateProposalJson(toolName: ""),
            CreateProposalJson(argumentsJson: "{"),
            CreateProposalJson(argumentsJson: "[]"),
            CreateProposalJson(argumentsJson: "\"scalar\""),
            CreateProposalJson(confidence: -0.01),
            CreateProposalJson(confidence: 1.01),
            CreateProposalJson(includeJustification: false),
            CreateProposalJson(justification: "")
        };

        foreach (var proposalJson in invalidProposals)
        {
            var result = await StartRunWithResponseAsync(
                CreateResponseJson(
                    outcome: "ProposalCreated",
                    proposalJson: proposalJson));

            Assert.Equal(AgentRunOutcome.Failed, result.Outcome);
        }
    }

    [Fact]
    public async Task StartRun_enforces_python_outcome_and_proposal_consistency()
    {
        var validProposal = CreateProposalJson();
        var inconsistentResponses = new[]
        {
            CreateResponseJson(outcome: "ProposalCreated"),
            CreateResponseJson(
                outcome: "Resolved",
                proposalJson: validProposal),
            CreateResponseJson(
                outcome: "Escalated",
                proposalJson: validProposal)
        };

        foreach (var responseJson in inconsistentResponses)
        {
            var result = await StartRunWithResponseAsync(responseJson);
            Assert.Equal(AgentRunOutcome.Failed, result.Outcome);
        }
    }

    [Fact]
    public async Task ResumeRun_rejects_tool_proposals_not_returned_by_the_resume_endpoint()
    {
        var responseJson = CreateResponseJson(
            outcome: "Escalated",
            triageJson: "{}",
            traceJson: """[{"agent":"ValidationAgent","step":"resume","model":"deterministic/server-result","prompt_version":"v1","prompt_tokens":0,"completion_tokens":0,"latency_ms":0.0,"summary":"Rejected."}]""",
            proposalJson: CreateProposalJson(),
            correlationId: "00000000-0000-0000-0000-000000000003");
        var handler = new CapturingHandler(responseJson);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://agent.test/")
        };
        var gateway = new PythonAgentGateway(
            client,
            NullLogger<PythonAgentGateway>.Instance);

        var result = await gateway.ResumeRunAsync(CreateResumeRequest());

        Assert.Equal(AgentRunOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task StartRun_propagates_cancellation()
    {
        using var handler = new CancellationBlockingHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://agent.test/")
        };
        var gateway = new PythonAgentGateway(
            client,
            NullLogger<PythonAgentGateway>.Instance);
        using var cancellation = new CancellationTokenSource();

        var pending = gateway.StartRunAsync(
            CreateStartRequest(),
            cancellation.Token);
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await pending);
    }

    [Fact]
    public async Task ResumeRun_propagates_cancellation()
    {
        using var handler = new CancellationBlockingHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://agent.test/")
        };
        var gateway = new PythonAgentGateway(
            client,
            NullLogger<PythonAgentGateway>.Instance);
        using var cancellation = new CancellationTokenSource();

        var pending = gateway.ResumeRunAsync(
            CreateResumeRequest(),
            cancellation.Token);
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await pending);
    }

    [Fact]
    public async Task StartRun_returns_failure_when_service_is_unavailable()
    {
        using var client = new HttpClient(new UnavailableHandler())
        {
            BaseAddress = new Uri("http://agent.test/")
        };
        var gateway = new PythonAgentGateway(
            client,
            NullLogger<PythonAgentGateway>.Instance);

        var result = await gateway.StartRunAsync(CreateStartRequest());

        Assert.Equal(AgentRunOutcome.Failed, result.Outcome);
        Assert.Null(result.ResolutionSummary);
    }

    private static AgentRunRequest CreateStartRequest() =>
        new(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            new AgentTicketContext(
                Guid.Parse("00000000-0000-0000-0000-000000000002"),
                "INC-42",
                "VPN failure",
                "The corporate VPN is down.",
                "user@example.com",
                TicketDomain.Unknown,
                Severity.P3,
                TicketStatus.New),
            [],
            0.6,
            2);

    private static ResumeAgentRunRequest CreateResumeRequest() =>
        new(
            Guid.Parse("00000000-0000-0000-0000-000000000003"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            false,
            "reviewer@example.com",
            "persisted rejection",
            false);

    private static async Task<AgentRunResult> StartRunWithResponseAsync(
        string responseJson)
    {
        using var client = new HttpClient(
            new CapturingHandler(responseJson))
        {
            BaseAddress = new Uri("http://agent.test/")
        };
        var gateway = new PythonAgentGateway(
            client,
            NullLogger<PythonAgentGateway>.Instance);

        return await gateway.StartRunAsync(CreateStartRequest());
    }

    private static string CreateResponseJson(
        string outcome = "Resolved",
        string? triageJson = null,
        string? traceJson = null,
        string? proposalJson = null,
        string confidence = "0.75",
        string correlationId = "00000000-0000-0000-0000-000000000001")
    {
        triageJson ??=
            $$"""{"domain":"Network","severity":"P1","confidence":{{confidence}}}""";
        traceJson ??= "[]";
        proposalJson ??= "null";
        return $$"""
            {
              "correlation_id":{{JsonSerializer.Serialize(correlationId)}},
              "outcome":{{JsonSerializer.Serialize(outcome)}},
              "triage":{{triageJson}},
              "tool_proposal":{{proposalJson}},
              "trace":{{traceJson}},
              "error":null
            }
            """;
    }

    private static string CreateProposalJson(
        string? toolName = "Example.Tool",
        string? argumentsJson = "{}",
        double? confidence = 0.5,
        string? justification = "Test proposal.",
        bool includeJustification = true,
        string? omittedField = null)
    {
        var proposal = new Dictionary<string, object?>
        {
            ["tool_name"] = toolName,
            ["arguments_json"] = argumentsJson,
            ["confidence"] = confidence
        };
        if (includeJustification)
        {
            proposal["justification"] = justification;
        }

        if (omittedField is not null)
        {
            proposal.Remove(omittedField);
        }

        return JsonSerializer.Serialize(proposal);
    }

    private static string? ValidateResponseDirectly(
        double triageConfidence,
        double? proposalConfidence)
    {
        const BindingFlags privateInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags privateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        var gatewayType = typeof(PythonAgentGateway);
        var responseType = gatewayType.GetNestedType(
            "PythonAgentRunResponse",
            BindingFlags.NonPublic)!;
        var triageType = gatewayType.GetNestedType(
            "PythonTriageResponse",
            BindingFlags.NonPublic)!;
        var traceType = gatewayType.GetNestedType(
            "PythonTraceResponse",
            BindingFlags.NonPublic)!;
        var response = Activator.CreateInstance(responseType, nonPublic: true)!;
        var triage = Activator.CreateInstance(triageType, nonPublic: true)!;

        responseType.GetProperty("CorrelationId", privateInstance)!
            .SetValue(response, "00000000-0000-0000-0000-000000000001");
        responseType.GetProperty("Outcome", privateInstance)!
            .SetValue(response, proposalConfidence.HasValue
                ? "ProposalCreated"
                : "Resolved");
        triageType.GetProperty("Domain", privateInstance)!
            .SetValue(triage, "Network");
        triageType.GetProperty("Severity", privateInstance)!
            .SetValue(triage, "P1");
        triageType.GetProperty("Confidence", privateInstance)!
            .SetValue(triage, triageConfidence);
        responseType.GetProperty("Triage", privateInstance)!
            .SetValue(response, triage);

        var traceList = (System.Collections.IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(traceType))!;
        responseType.GetProperty("Trace", privateInstance)!
            .SetValue(response, traceList);

        if (proposalConfidence.HasValue)
        {
            var proposalType = gatewayType.GetNestedType(
                "PythonToolProposalResponse",
                BindingFlags.NonPublic)!;
            var proposal = Activator.CreateInstance(
                proposalType,
                nonPublic: true)!;
            proposalType.GetProperty("ToolName", privateInstance)!
                .SetValue(proposal, "Example.Tool");
            proposalType.GetProperty("ArgumentsJson", privateInstance)!
                .SetValue(proposal, "{}");
            proposalType.GetProperty("Confidence", privateInstance)!
                .SetValue(proposal, proposalConfidence.Value);
            proposalType.GetProperty("Justification", privateInstance)!
                .SetValue(proposal, "Test proposal.");
            responseType.GetProperty("ToolProposal", privateInstance)!
                .SetValue(response, proposal);
        }

        var validateResponse = gatewayType.GetMethod(
            "ValidateResponse",
            privateStatic)!;
        return (string?)validateResponse.Invoke(
            null,
            [
                response,
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                false
            ]);
    }

    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode =
            HttpStatusCode.OK;

        public CapturingHandler(
            string responseJson,
            HttpStatusCode statusCode) : this(responseJson)
        {
            _statusCode = statusCode;
        }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class UnavailableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Service unavailable.");
    }

    private sealed class CancellationBlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource RequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("The canceled request unexpectedly completed.");
        }
    }
}
