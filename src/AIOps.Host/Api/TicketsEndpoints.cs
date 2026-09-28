using System.Security.Claims;
using AIOps.Abstractions.Grains;
using AIOps.Contracts.Api;
using AIOps.Contracts.AgentGateway;
using AIOps.Domain;
using AIOps.Host.Security;
using AIOps.Orchestration.Approvals;
using AIOps.Orchestration.Tickets;

namespace AIOps.Host.Api;

/// <summary>
/// Ticket lifecycle API. Transport-only: validation + status-code mapping lives here,
/// all business rules live in the domain/grain/application layers.
/// </summary>
public static class TicketsEndpoints
{
    public static IEndpointRouteBuilder MapTicketApi(
        this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/tickets")
            .WithTags("Tickets");

        var approvals = app
            .MapGroup("/api/v1/approvals")
            .WithTags("Approvals");

        group.MapPost(
            "/",
            async (
                CreateTicketRequest req,
                TicketService svc,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var errors = ValidateCreate(req);

                if (errors.Count > 0)
                    return Results.ValidationProblem(errors);

                var actor = user.Identity?.Name ?? "unknown";

                var created = await svc.CreateAsync(
                    req,
                    actor,
                    ct);

                return Results.Created(
                    $"/api/v1/tickets/{created.Id}",
                    created);
            })
            .RequireAuthorization(AuthPolicies.CanCreateTicket)
            .Produces<TicketDetailDto>(
                StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapGet(
            "/",
            async (
                TicketStatus? status,
                TicketDomain? domain,
                Severity? severity,
                Abstractions.Persistence.ITicketRepository repo,
                CancellationToken ct) =>
                Results.Ok(
                    await repo.ListAsync(
                        status,
                        domain,
                        severity,
                        ct: ct)))
            .RequireAuthorization(AuthPolicies.CanViewQueue)
            .Produces<IReadOnlyList<Abstractions.Persistence.TicketQueueRow>>();

        group.MapGet(
            "/{id:guid}",
            async (
                Guid id,
                TicketService svc,
                CancellationToken ct) =>
            {
                var dto = await svc.GetAsync(id, ct);

                return dto is null
                    ? TicketNotFound(id)
                    : Results.Ok(dto);
            })
            .RequireAuthorization(AuthPolicies.CanViewQueue)
            .Produces<TicketDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost(
            "/{id:guid}/transitions",
            async (
                Guid id,
                TransitionTicketRequest req,
                TicketService svc,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(req.Reason))
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["reason"] =
                            ["A transition reason is required."]
                        });
                }

                try
                {
                    var actor = user.Identity?.Name ?? "unknown";

                    var dto = await svc.TransitionAsync(
                        id,
                        req.ToStatus,
                        req.Reason,
                        actor,
                        ct);

                    return Results.Ok(dto);
                }
                catch (GrainRuleException ex)
                {
                    return MapRuleViolation(id, ex);
                }
            })
            .RequireAuthorization(AuthPolicies.CanCreateTicket)
            .Produces<TicketDetailDto>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        group.MapPost(
            "/{id:guid}/agent-runs",
            async (
                Guid id,
                AgentRunRequest req,
                AIOps.Orchestration.Orchestrator orchestrator,
                ILogger<Program> logger,
                CancellationToken ct) =>
            {
                var errors = ValidateAgentRun(id, req);
                if (errors.Count > 0)
                    return Results.ValidationProblem(errors);

                try
                {
                    return Results.Ok(
                        await orchestrator.StartAgentRunAsync(id, req, ct));
                }
                catch (DomainInvariantViolationException ex)
                {
                    return AgentRunInvariantProblem(ex);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Agent run failed for ticket {TicketId}.", id);
                    return Results.Problem(
                        title: "Agent run failed",
                        detail: "The agent run could not be completed.",
                        statusCode: StatusCodes.Status500InternalServerError);
                }
            })
            .RequireAuthorization(AuthPolicies.CanCreateTicket)
            .WithTags("Agent Runs")
            .Produces<AgentRunResult>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost(
            "/{id:guid}/agent-runs/resume",
            async (
                Guid id,
                ResumeAgentRunApiRequest req,
                AIOps.Orchestration.Orchestrator orchestrator,
                ILogger<Program> logger,
                CancellationToken ct) =>
            {
                if (req.ActionExecutionId == Guid.Empty)
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["actionExecutionId"] = ["A valid action execution ID is required."]
                        });
                }

                try
                {
                    return Results.Ok(
                        await orchestrator.ResumeAgentRunAsync(
                            id,
                            req.ActionExecutionId,
                            ct));
                }
                catch (DomainInvariantViolationException ex)
                {
                    return AgentRunInvariantProblem(ex);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(
                        ex,
                        "Agent run resume failed for ticket {TicketId}, action {ActionExecutionId}.",
                        id,
                        req.ActionExecutionId);
                    return Results.Problem(
                        title: "Agent run resume failed",
                        detail: "The agent run could not be resumed.",
                        statusCode: StatusCodes.Status500InternalServerError);
                }
            })
            .RequireAuthorization(AuthPolicies.CanApprove)
            .WithTags("Agent Runs")
            .Produces<AgentRunResult>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // -----------------------------------------------------------------
        // Human approval endpoints
        // -----------------------------------------------------------------

        approvals.MapPost(
            "/actions/{actionExecutionId:guid}",
            async (
                Guid actionExecutionId,
                CreateApprovalRequest req,
                ApprovalService svc,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(req.Justification))
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["justification"] =
                            ["A justification is required."]
                        });
                }

                var actor = user.Identity?.Name ?? "unknown";

                try
                {
                    var result = await svc.CreateAsync(
                        actionExecutionId,
                        actor,
                        req.Justification,
                        ct);

                    return Results.Created(
                        $"/api/v1/approvals/{result.Id}",
                        result);
                }
                catch (DomainInvariantViolationException ex)
                {
                    return Results.Conflict(
                        new
                        {
                            error = ex.Message
                        });
                }
            })
            .RequireAuthorization(AuthPolicies.CanApprove)
            .Produces<ApprovalResponse>(
                StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        approvals.MapGet(
            "/",
            async (
                AIOps.Orchestration.Orchestrator orchestrator,
                CancellationToken ct) =>
                Results.Ok(
                    await orchestrator.ListPendingApprovalsAsync(ct)))
            .RequireAuthorization(AuthPolicies.CanApprove)
            .Produces<IReadOnlyList<ApprovalResponse>>();

        approvals.MapGet(
            "/{approvalId:guid}",
            async (
                Guid approvalId,
                ApprovalService svc,
                CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(
                        await svc.GetAsync(
                            approvalId,
                            ct));
                }
                catch (DomainInvariantViolationException)
                {
                    return Results.NotFound();
                }
            })
            .RequireAuthorization(AuthPolicies.CanApprove)
            .Produces<ApprovalResponse>()
            .Produces(StatusCodes.Status404NotFound);

        approvals.MapPost(
            "/{approvalId:guid}/decision",
            async (
                Guid approvalId,
                DecideApprovalRequest req,
                AIOps.Orchestration.Orchestrator orchestrator,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var actor = user.Identity?.Name ?? "unknown";

                try
                {
                    var result = await orchestrator.DecideApprovalAsync(
                        approvalId,
                        req.Approve,
                        actor,
                        req.Comment,
                        ct);

                    return Results.Ok(result);
                }
                catch (DomainInvariantViolationException ex)
                {
                    return Results.Conflict(
                        new
                        {
                            error = ex.Message
                        });
                }
            })
            .RequireAuthorization(AuthPolicies.CanApprove)
            .Produces<ApprovalResponse>()
            .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    private static Dictionary<string, string[]> ValidateCreate(
        CreateTicketRequest req)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(req.Title) ||
            req.Title.Length > 200)
        {
            errors["title"] =
            [
                "Title is required (max 200 characters)."
            ];
        }

        if (string.IsNullOrWhiteSpace(req.Description))
        {
            errors["description"] =
            [
                "Description is required."
            ];
        }

        if (string.IsNullOrWhiteSpace(req.ReporterEmail) ||
            !req.ReporterEmail.Contains('@'))
        {
            errors["reporterEmail"] =
            [
                "A valid reporter email is required."
            ];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateAgentRun(
        Guid routeTicketId,
        AgentRunRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["request"] = ["An agent run request is required."];
            return errors;
        }

        if (request.CorrelationId == Guid.Empty)
            errors["correlationId"] = ["A valid correlation ID is required."];

        var ticket = request.Ticket;
        if (ticket is null)
        {
            errors["ticket"] = ["Ticket context is required."];
            return errors;
        }

        if (ticket.TicketId != routeTicketId)
            errors["ticket.ticketId"] = ["Ticket context must match the ticket route."];
        if (string.IsNullOrWhiteSpace(ticket.Title) || ticket.Title.Length > 200)
            errors["ticket.title"] = ["A ticket title is required (max 200 characters)."];
        if (string.IsNullOrWhiteSpace(ticket.Description))
            errors["ticket.description"] = ["A ticket description is required."];
        if (string.IsNullOrWhiteSpace(ticket.ReporterEmail) || !ticket.ReporterEmail.Contains('@'))
            errors["ticket.reporterEmail"] = ["A valid reporter email is required."];

        return errors;
    }

    private static IResult AgentRunInvariantProblem(
        DomainInvariantViolationException exception) =>
        exception.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
            ? Results.Problem(
                title: "Ticket or action not found",
                detail: "The requested ticket or action execution does not exist.",
                statusCode: StatusCodes.Status404NotFound)
            : Results.Problem(
                title: "Agent run rejected",
                detail: "The agent run cannot proceed in its current lifecycle state.",
                statusCode: StatusCodes.Status409Conflict);

    private static IResult TicketNotFound(Guid id) =>
        Results.Problem(
            title: "Ticket not found",
            detail: $"Ticket {id} does not exist.",
            statusCode: StatusCodes.Status404NotFound);

    private static IResult MapRuleViolation(
        Guid id,
        GrainRuleException ex) =>
        ex.Code switch
        {
            GrainRuleException.NotFound =>
                TicketNotFound(id),

            _ => Results.Problem(
                title: "State transition rejected",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = ex.Code
                })
        };
}
