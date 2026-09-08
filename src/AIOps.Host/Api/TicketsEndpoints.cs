using System.Security.Claims;
using AIOps.Abstractions.Grains;
using AIOps.Contracts.Api;
using AIOps.Domain;
using AIOps.Host.Security;
using AIOps.Orchestration.Tickets;

namespace AIOps.Host.Api;

/// <summary>
/// Ticket lifecycle API. Transport-only: validation + status-code mapping lives here,
/// all business rules live in the domain/grain/application layers.
/// </summary>
public static class TicketsEndpoints
{
    public static IEndpointRouteBuilder MapTicketApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tickets").WithTags("Tickets");

        group.MapPost("/", async (CreateTicketRequest req, TicketService svc, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var errors = ValidateCreate(req);
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var actor = user.Identity?.Name ?? "unknown";
            var created = await svc.CreateAsync(req, actor, ct);
            return Results.Created($"/api/v1/tickets/{created.Id}", created);
        })
        .RequireAuthorization(AuthPolicies.CanCreateTicket)
        .Produces<TicketDetailDto>(StatusCodes.Status201Created)
        .ProducesValidationProblem();

        group.MapGet("/", async (TicketStatus? status, TicketDomain? domain, Severity? severity,
                Abstractions.Persistence.ITicketRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.ListAsync(status, domain, severity, ct: ct)))
        .RequireAuthorization(AuthPolicies.CanViewQueue)
        .Produces<IReadOnlyList<Abstractions.Persistence.TicketQueueRow>>();

        group.MapGet("/{id:guid}", async (Guid id, TicketService svc, CancellationToken ct) =>
        {
            var dto = await svc.GetAsync(id, ct);
            return dto is null ? TicketNotFound(id) : Results.Ok(dto);
        })
        .RequireAuthorization(AuthPolicies.CanViewQueue)
        .Produces<TicketDetailDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/transitions", async (Guid id, TransitionTicketRequest req,
                TicketService svc, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Reason))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["reason"] = ["A transition reason is required."]
                });

            try
            {
                var actor = user.Identity?.Name ?? "unknown";
                var dto = await svc.TransitionAsync(id, req.ToStatus, req.Reason, actor, ct);
                return Results.Ok(dto);
            }
            catch (GrainRuleException ex) { return MapRuleViolation(id, ex); }
        })
        .RequireAuthorization(AuthPolicies.CanCreateTicket)
        .Produces<TicketDetailDto>()
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesValidationProblem();

        return app;
    }

    private static Dictionary<string, string[]> ValidateCreate(CreateTicketRequest req)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Length > 200)
            errors["title"] = ["Title is required (max 200 characters)."];
        if (string.IsNullOrWhiteSpace(req.Description))
            errors["description"] = ["Description is required."];
        if (string.IsNullOrWhiteSpace(req.ReporterEmail) || !req.ReporterEmail.Contains('@'))
            errors["reporterEmail"] = ["A valid reporter email is required."];
        return errors;
    }

    private static IResult TicketNotFound(Guid id) => Results.Problem(
        title: "Ticket not found", detail: $"Ticket {id} does not exist.",
        statusCode: StatusCodes.Status404NotFound);

    private static IResult MapRuleViolation(Guid id, GrainRuleException ex) => ex.Code switch
    {
        GrainRuleException.NotFound => TicketNotFound(id),
        _ => Results.Problem(title: "State transition rejected", detail: ex.Message,
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = ex.Code })
    };
}
