using AIOps.Host.Api;
using AIOps.Host.Components;
using AIOps.Host.Hubs;
using AIOps.Host.Security;
using AIOps.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using MudBlazor.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Structured logging (console; Seq sink is a documented option for later phases).
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "AIOps.Host")
    .WriteTo.Console());

// --- Orleans silo (co-hosted, local dev clustering + in-memory grain storage) ---
builder.UseOrleans(silo =>
{
    silo.UseLocalhostClustering();
    silo.AddMemoryGrainStorage("ticketStore"); // ADR-0003: ADO.NET/PostgreSQL provider in Phase 3
});

// --- Blazor + MudBlazor ---
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

// --- SignalR (TicketHub stub; events land in Phase 9) ---
builder.Services.AddSignalR();

// --- Security boundaries from day one ---
builder.Services.AddAIOpsSecurity(builder.Configuration);

// --- Platform seams (in-memory dev implementations) ---
builder.Services.AddAIOpsInfrastructure(builder.Configuration);

// --- Application services (ticket lifecycle orchestration) ---
builder.Services.AddScoped<AIOps.Orchestration.Tickets.TicketService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseAuthorization();

app.MapStaticAssets();
app.MapHub<TicketHub>("/hubs/tickets");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// --- Phase 1 operational endpoints ---
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "AIOps.Host", utc = DateTimeOffset.UtcNow }))
    .WithTags("Ops").AllowAnonymous();

app.MapGet("/api/v1/meta/selfcheck", (
    [FromServices] AIOps.Abstractions.Agents.IAgentGateway gateway,
    [FromServices] AIOps.Abstractions.Audit.IAuditStore audit,
    [FromServices] AIOps.Abstractions.Persistence.ITicketRepository tickets,
    [FromServices] AIOps.Abstractions.Time.IClock clock) => Results.Ok(new
    {
        agentGateway = gateway.GetType().Name,
        auditStore = audit.GetType().Name,
        ticketRepository = tickets.GetType().Name,
        clock = clock.GetType().Name,
        orleans = "co-hosted silo, memory grain storage (ticketStore)"
    }))
    .WithTags("Meta").AllowAnonymous();

// Echoes non-secret config only. Never returns secret values — only whether they are set.
app.MapGet("/api/v1/meta/config", (IConfiguration config) => Results.Ok(new
{
    agentGatewayMode = config["AI:AgentGatewayMode"] ?? "Fake",
    nimBaseUrl = config["AI:NvidiaNim:BaseUrl"],
    nimModel = config["AI:NvidiaNim:Model"],
    nimApiKeyConfigured = !string.IsNullOrEmpty(config["AI:NvidiaNim:ApiKey"]) // value never returned
}))
    .WithTags("Meta").AllowAnonymous();

// --- Phase 2: ticket lifecycle API ---
app.MapTicketApi();

app.Run();

// Exposed for WebApplicationFactory-based API tests.
public partial class Program;
