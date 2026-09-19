using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Tools;
using AIOps.Contracts.Api;
using AIOps.Domain;
using AIOps.Infrastructure.Integrations;
using AIOps.Tools;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIOps.Api.Tests;

public sealed class TicketApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _fixture;
    private readonly HttpClient _client;

    public TicketApiTests(WebApplicationFactory<Program> factory)
    {
        _fixture = factory.WithWebHostBuilder(b =>
            b.UseEnvironment("Test"));

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
    public void ToolSystem_IsRegisteredInHost()
    {
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