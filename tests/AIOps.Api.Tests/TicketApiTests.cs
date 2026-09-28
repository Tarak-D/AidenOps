using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using AIOps.Abstractions;
using AIOps.Abstractions.Agents;
using AIOps.Abstractions.Audit;
using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Persistence;
using AIOps.Abstractions.Tools;
using AIOps.Contracts.Api;
using AIOps.Contracts.AgentGateway;
using AIOps.Domain;
using AIOps.Domain.Entities;
using AIOps.Infrastructure.Integrations;
using AIOps.Orchestration.Approvals;
using AIOps.Tools;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace AIOps.Api.Tests;

public sealed class TicketApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _fixture;
    private readonly HttpClient _client;
    private readonly RecordingAgentGateway _gateway = new();

    public TicketApiTests(WebApplicationFactory<Program> factory)
    {
        _fixture = factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Test");
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAgentGateway>();
                services.AddSingleton<IAgentGateway>(_gateway);
                var actionState = new InMemoryActionApprovalStore();
                services.RemoveAll<IActionExecutionStore>();
                services.AddSingleton<IActionExecutionStore>(actionState);
                services.RemoveAll<IApprovalStore>();
                services.AddSingleton<IApprovalStore>(actionState);
                services.RemoveAll<IApprovalValidator>();
                services.AddSingleton<IApprovalValidator>(actionState);
                services.RemoveAll<IAuditStore>();
                services.AddSingleton<IAuditStore, AIOps.Infrastructure.Audit.InMemoryAuditStore>();
            });
        });

        // TestEnvironment avoids HTTPS redirect/config requirements; no secrets involved.
        _client = _fixture.CreateDefaultClient();
        _client.DefaultRequestHeaders.Add("X-Dev-User", "engineer");
    }

    private static object NewTicket(
        string title = "Outlook crashes",
        string desc = "details",
        string email = "user@corp.example") => new
    {
        title,
        description = desc,
        reporterEmail = email
    };

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Create_then_get_roundtrip()
    {
        var create = await _client.PostAsJsonAsync(
            "/api/v1/tickets",
            NewTicket());

        Assert.Equal(
            HttpStatusCode.Created,
            create.StatusCode);

        var dto =
            await create.Content.ReadFromJsonAsync<TicketDetailDto>(Json);

        Assert.NotNull(dto);
        Assert.Equal(
            TicketStatus.New,
            dto!.Status);

        var get = await _client.GetAsync(
            $"/api/v1/tickets/{dto.Id}");

        Assert.Equal(
            HttpStatusCode.OK,
            get.StatusCode);

        var fetched =
            await get.Content.ReadFromJsonAsync<TicketDetailDto>(Json);

        Assert.Equal(
            dto.Id,
            fetched!.Id);
    }

    [Fact]
    public async Task Create_with_missing_fields_returns_400()
    {
        var res = await _client.PostAsJsonAsync(
            "/api/v1/tickets",
            new
            {
                title = "",
                description = "",
                reporterEmail = "not-an-email"
            });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            res.StatusCode);

        var problem =
            await res.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json);

        Assert.NotNull(problem);
        Assert.True(problem!.Errors.ContainsKey("title"));
        Assert.True(problem.Errors.ContainsKey("reporterEmail"));
    }

    [Fact]
    public async Task Get_unknown_ticket_returns_404_problem()
    {
        var res = await _client.GetAsync(
            $"/api/v1/tickets/{Guid.NewGuid()}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            res.StatusCode);

        var body = await res.Content.ReadAsStringAsync();

        Assert.Contains(
            "does not exist",
            body);
    }

    [Fact]
    public async Task Valid_transition_returns_200_with_new_status()
    {
        var created =
            await (await _client.PostAsJsonAsync(
                "/api/v1/tickets",
                NewTicket()))
            .Content.ReadFromJsonAsync<TicketDetailDto>(Json);

        var res = await _client.PostAsJsonAsync(
            $"/api/v1/tickets/{created!.Id}/transitions",
            new
            {
                toStatus = TicketStatus.Triaging,
                reason = "starting triage"
            });

        Assert.Equal(
            HttpStatusCode.OK,
            res.StatusCode);

        var dto =
            await res.Content.ReadFromJsonAsync<TicketDetailDto>(Json);

        Assert.Equal(
            TicketStatus.Triaging,
            dto!.Status);
    }

    [Fact]
    public async Task Illegal_transition_returns_409_with_rule_code()
    {
        var created =
            await (await _client.PostAsJsonAsync(
                "/api/v1/tickets",
                NewTicket()))
            .Content.ReadFromJsonAsync<TicketDetailDto>(Json);

        var res = await _client.PostAsJsonAsync(
            $"/api/v1/tickets/{created!.Id}/transitions",
            new
            {
                toStatus = TicketStatus.Resolved,
                reason = "shortcut"
            });

        Assert.Equal(
            HttpStatusCode.Conflict,
            res.StatusCode);

        Assert.Contains(
            "InvalidTransition",
            await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Transition_without_reason_returns_400()
    {
        var created =
            await (await _client.PostAsJsonAsync(
                "/api/v1/tickets",
                NewTicket()))
            .Content.ReadFromJsonAsync<TicketDetailDto>(Json);

        var res = await _client.PostAsJsonAsync(
            $"/api/v1/tickets/{created!.Id}/transitions",
            new
            {
                toStatus = TicketStatus.Triaging,
                reason = ""
            });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            res.StatusCode);
    }

    [Fact]
    public async Task Start_agent_run_returns_orchestrator_result_for_valid_request()
    {
        var ticket = await CreateTicketAsync("Unrecognized issue", "A request without a known category.");
        var before = _gateway.StartRequests.Count;
        var request = new AgentRunRequest(
            Guid.NewGuid(),
            new AgentTicketContext(
                ticket.Id,
                ticket.ExternalRef ?? "",
                ticket.Title,
                ticket.Description,
                ticket.ReporterEmail,
                ticket.Domain,
                ticket.Severity,
                ticket.Status),
            [],
            0.6,
            1);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/tickets/{ticket.Id}/agent-runs",
            request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<AgentRunResult>(Json);
        Assert.NotNull(result);
        Assert.Equal(AgentRunOutcome.Escalated, result!.Outcome);
        Assert.Equal(before + 1, _gateway.StartRequests.Count);
    }

    [Fact]
    public async Task Start_agent_run_with_invalid_input_returns_validation_problem()
    {
        var ticket = await CreateTicketAsync("Agent run input", "A valid description.");
        var before = _gateway.StartRequests.Count;
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/tickets/{ticket.Id}/agent-runs",
            new
            {
                correlationId = Guid.Empty,
                ticket = new
                {
                    ticketId = ticket.Id,
                    externalRef = "",
                    title = "",
                    description = "",
                    reporterEmail = "invalid",
                    domain = ticket.Domain,
                    severity = ticket.Severity,
                    status = ticket.Status
                },
                allowedTools = Array.Empty<object>(),
                triageConfidenceThreshold = 0.6,
                maxAttempts = 1
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json);
        Assert.NotNull(problem);
        Assert.Contains("correlationId", problem!.Errors.Keys);
        Assert.Equal(before, _gateway.StartRequests.Count);
    }

    [Fact]
    public async Task Resume_agent_run_uses_persisted_state_and_ignores_forged_fields()
    {
        var ticket = await CreateTicketAsync("Resume lifecycle", "Validate persisted action state.");
        var action = ActionExecution.Propose(
            ticket.Id,
            "Test.ApprovalRequired",
            "{}",
            RiskLevel.Moderate,
            "test-agent",
            "A controlled test action.",
            DateTimeOffset.UtcNow);

        using (var scope = _fixture.Services.CreateScope())
        {
            var actions = scope.ServiceProvider.GetRequiredService<IActionExecutionStore>();
            await actions.AddAsync(action);
            var approvals = scope.ServiceProvider.GetRequiredService<ApprovalService>();
            var approval = await approvals.CreateAsync(
                action.Id,
                "test-agent",
                "Approval required for test action.");
            await approvals.DecideAsync(
                approval.Id,
                true,
                "approver-api-test",
                "Approved for API verification.");
            action = (await actions.GetAsync(action.Id))!;
            action.BeginExecution();
            action.MarkSucceeded("Persisted completion result", DateTimeOffset.UtcNow);
            await actions.UpdateAsync(action);
        }

        var before = _gateway.ResumeRequests.Count;
        var response = await PostAsApproverAsync(
            $"/api/v1/tickets/{ticket.Id}/agent-runs/resume",
            new
            {
                actionExecutionId = action.Id,
                approvalGranted = false,
                approvalDecidedBy = "forged-caller",
                toolResultJson = "forged-result",
                toolExecutionSucceeded = false,
                toolExecutionError = "forged-error"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(_gateway.ResumeRequests.Skip(before));
        Assert.Equal(action.Id, sent.ActionExecutionId);
        Assert.Equal(ticket.Id, sent.TicketId);
        Assert.True(sent.ApprovalGranted);
        Assert.Equal("approver-api-test", sent.ApprovalDecidedBy);
        Assert.True(sent.ToolExecutionSucceeded);
        Assert.Equal("Approved", sent.ApprovalStatus);
        Assert.Equal("Succeeded", sent.ActionStatus);
        Assert.Contains("Persisted completion result", sent.ToolResultJson);
        Assert.DoesNotContain("forged-result", sent.ToolResultJson);
    }

    [Fact]
    public async Task Resume_missing_action_returns_not_found_without_gateway_call()
    {
        var ticket = await CreateTicketAsync("Resume missing action", "A valid ticket.");
        var before = _gateway.ResumeRequests.Count;

        var response = await PostAsApproverAsync(
            $"/api/v1/tickets/{ticket.Id}/agent-runs/resume",
            new ResumeAgentRunApiRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, _gateway.ResumeRequests.Count);
    }

    [Fact]
    public async Task Resume_invalid_action_state_returns_conflict_without_execution_or_gateway_call()
    {
        var ticket = await CreateTicketAsync("Resume invalid action", "A valid ticket.");
        var action = ActionExecution.Propose(
            ticket.Id,
            "Test.Safe",
            "{}",
            RiskLevel.Safe,
            "test-agent",
            null,
            DateTimeOffset.UtcNow);
        using (var scope = _fixture.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IActionExecutionStore>().AddAsync(action);
        }

        var before = _gateway.ResumeRequests.Count;
        var response = await PostAsApproverAsync(
            $"/api/v1/tickets/{ticket.Id}/agent-runs/resume",
            new
            {
                actionExecutionId = action.Id,
                approvalGranted = true,
                toolResultJson = "forged-success",
                toolExecutionSucceeded = true
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, _gateway.ResumeRequests.Count);
        using var verifyScope = _fixture.Services.CreateScope();
        var persisted = await verifyScope.ServiceProvider
            .GetRequiredService<IActionExecutionStore>()
            .GetAsync(action.Id);
        Assert.Equal(ActionStatus.Proposed, persisted!.Status);
    }

    private async Task<TicketDetailDto> CreateTicketAsync(string title, string description)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/tickets",
            NewTicket(title, description));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TicketDetailDto>(Json))!;
    }

    private Task<HttpResponseMessage> PostAsApproverAsync(string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: Json)
        };
        request.Headers.Add("X-Dev-User", "approver");
        return _client.SendAsync(request);
    }

    private sealed class RecordingAgentGateway : IAgentGateway
    {
        public ConcurrentQueue<AgentRunRequest> StartRequests { get; } = new();
        public ConcurrentQueue<ResumeAgentRunRequest> ResumeRequests { get; } = new();

        public Task<AgentRunResult> StartRunAsync(AgentRunRequest request, CancellationToken ct = default)
        {
            StartRequests.Enqueue(request);
            return Task.FromResult(new AgentRunResult(
                AgentRunOutcome.Escalated,
                0.9,
                TicketDomain.Unknown,
                Severity.P3,
                null,
                "Test gateway escalation.",
                null,
                Array.Empty<StepTrace>(),
                null));
        }

        public Task<AgentRunResult> ResumeRunAsync(ResumeAgentRunRequest request, CancellationToken ct = default)
        {
            ResumeRequests.Enqueue(request);
            return Task.FromResult(new AgentRunResult(
                request.ActionStatus == "Succeeded" ? AgentRunOutcome.Resolved : AgentRunOutcome.Escalated,
                null,
                null,
                null,
                null,
                request.ActionStatus == "Succeeded" ? null : request.ToolExecutionError,
                request.ActionStatus == "Succeeded" ? "Persisted action succeeded." : null,
                Array.Empty<StepTrace>(),
                null));
        }
    }

    private sealed class InMemoryActionApprovalStore : IActionExecutionStore, IApprovalStore, IApprovalValidator
    {
        private readonly ConcurrentDictionary<Guid, ActionExecution> _actions = new();
        private readonly ConcurrentDictionary<Guid, ApprovalRequest> _approvals = new();

        public Task AddAsync(ActionExecution actionExecution, CancellationToken ct = default)
        {
            _actions[actionExecution.Id] = actionExecution;
            return Task.CompletedTask;
        }

        Task<ActionExecution?> IActionExecutionStore.GetAsync(Guid actionExecutionId, CancellationToken ct)
        {
            _actions.TryGetValue(actionExecutionId, out var action);
            return Task.FromResult(action);
        }

        public Task UpdateAsync(ActionExecution actionExecution, CancellationToken ct = default)
        {
            _actions[actionExecution.Id] = actionExecution;
            return Task.CompletedTask;
        }

        public Task AddAsync(ApprovalRequest approval, CancellationToken ct = default)
        {
            _approvals[approval.Id] = approval;
            return Task.CompletedTask;
        }

        Task<ApprovalRequest?> IApprovalStore.GetAsync(Guid approvalId, CancellationToken ct)
        {
            _approvals.TryGetValue(approvalId, out var approval);
            return Task.FromResult(approval);
        }

        public Task<ApprovalRequest?> GetForActionExecutionAsync(Guid actionExecutionId, CancellationToken ct = default) =>
            Task.FromResult(_approvals.Values.SingleOrDefault(x => x.ActionExecutionId == actionExecutionId));

        public Task UpdateAsync(ApprovalRequest approval, CancellationToken ct = default)
        {
            _approvals[approval.Id] = approval;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ApprovalRequest>> ListPendingAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ApprovalRequest>>(
                _approvals.Values.Where(x => x.Status == ApprovalStatus.Pending).ToArray());

        public Task<ApprovalRequest?> GetApprovalForActionExecutionAsync(Guid actionExecutionId, CancellationToken ct = default) =>
            GetForActionExecutionAsync(actionExecutionId, ct);
    }

    [Fact]
public async Task Knowledge_search_without_query_returns_400()
{
    var res = await _client.GetAsync(
        "/api/v1/knowledge/search");

    Assert.Equal(
        HttpStatusCode.BadRequest,
        res.StatusCode);

    var body =
        await res.Content.ReadAsStringAsync();

    Assert.Contains(
        "Query parameter 'q' is required.",
        body);
}

[Fact]
public async Task Knowledge_search_with_invalid_limit_returns_400()
{
    var res = await _client.GetAsync(
        "/api/v1/knowledge/search?q=VPN&limit=0");

    Assert.Equal(
        HttpStatusCode.BadRequest,
        res.StatusCode);

    var body =
        await res.Content.ReadAsStringAsync();

    Assert.Contains(
        "between 1 and 50",
        body);
}

    [Fact]
    public void ToolSystem_IsRegisteredInHost()
    {
        Assert.NotNull(
            _fixture.Services.GetRequiredService<ToolExecutor>());

        var toolRegistry =
            _fixture.Services.GetRequiredService<IToolRegistry>();

        var instanceStatusTool =
            toolRegistry.Get("Cloud.GetInstanceStatus");

        Assert.NotNull(instanceStatusTool);
        Assert.IsType<GetInstanceStatusTool>(instanceStatusTool);
        Assert.Equal(
            "Cloud.GetInstanceStatus",
            instanceStatusTool.Name);

        var restartTool =
            toolRegistry.Get("Cloud.RestartInstance");

        Assert.NotNull(restartTool);
        Assert.IsType<RestartInstanceTool>(restartTool);
        Assert.Equal(
            RiskLevel.Moderate,
            restartTool.Risk);
        Assert.True(
            restartTool.RequiresApproval);

        var resetPasswordTool =
            toolRegistry.Get("Directory.ResetPassword");

        Assert.NotNull(resetPasswordTool);
        Assert.IsType<ResetPasswordTool>(resetPasswordTool);
        Assert.Equal(
            RiskLevel.Sensitive,
            resetPasswordTool.Risk);
        Assert.True(
            resetPasswordTool.RequiresApproval);

        var grantGroupAccessTool =
            toolRegistry.Get("Directory.GrantGroupAccess");

        Assert.NotNull(grantGroupAccessTool);
        Assert.IsType<GrantGroupAccessTool>(grantGroupAccessTool);
        Assert.Equal(
            RiskLevel.Sensitive,
            grantGroupAccessTool.Risk);
        Assert.True(
            grantGroupAccessTool.RequiresApproval);

        var updateTicketTool =
            toolRegistry.Get("ITSM.UpdateTicket");

        Assert.NotNull(updateTicketTool);
        Assert.IsType<UpdateTicketTool>(updateTicketTool);
        Assert.Equal(
            RiskLevel.Moderate,
            updateTicketTool.Risk);
        Assert.True(
            updateTicketTool.RequiresApproval);

        var vpnDiagnosticsTool =
            toolRegistry.Get("Network.RunVpnDiagnostics");

        Assert.NotNull(vpnDiagnosticsTool);
        Assert.IsType<RunVpnDiagnosticsTool>(vpnDiagnosticsTool);
        Assert.Equal(
            RiskLevel.Safe,
            vpnDiagnosticsTool.Risk);
        Assert.False(
            vpnDiagnosticsTool.RequiresApproval);
    }
}
