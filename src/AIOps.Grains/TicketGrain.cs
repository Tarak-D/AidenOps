using AIOps.Abstractions.Grains;
using AIOps.Abstractions.Time;
using AIOps.Domain;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Storage;

namespace AIOps.Grains;

/// <summary>
/// Virtual actor owning exactly one ticket (grain identity = ticket id). Because Orleans
/// guarantees single-threaded activation per grain, every state change goes through the
/// authoritative <see cref="TicketStatusTransitions"/> table exactly once, in order.
/// Durable state via the "ticketStore" named provider (memory in dev; ADO.NET/PostgreSQL
/// in deployment — see ADR-0003).
/// </summary>
public sealed class TicketGrain : Grain, ITicketGrain
{
    private const int MaxActivityEntries = 100;

    private readonly IPersistentState<TicketState> _state;
    private readonly IClock _clock;
    private readonly ILogger<TicketGrain> _logger;

    public TicketGrain(
        [PersistentState("ticket", "ticketStore")] IPersistentState<TicketState> state,
        IClock clock,
        ILogger<TicketGrain> logger)
    {
        _state = state;
        _clock = clock;
        _logger = logger;
    }

    private Guid TicketId => this.GetPrimaryKey();

    public Task<TicketState> GetState() => Task.FromResult(_state.State);

    public async Task<TicketState> Create(CreateTicketCommand command)
    {
        if (_state.State.Exists)
            throw new GrainRuleException(GrainRuleException.AlreadyExists,
                $"Ticket {TicketId} already exists.");

        var now = _clock.UtcNow;
        _state.State = new TicketState
        {
            Exists = true,
            ExternalRef = command.ExternalRef,
            Title = command.Title?.Trim() ?? "",
            Description = command.Description?.Trim() ?? "",
            ReporterEmail = command.ReporterEmail?.Trim() ?? "",
            ReporterName = command.ReporterName,
            Domain = TicketDomain.Unknown,
            Severity = Severity.P3,
            Status = TicketStatus.New,
            Source = command.Source,
            CreatedAt = now,
            UpdatedAt = now,
            RecentActivity =
            [ new ActivityEntry
              {
                  OccurredAt = now, ActorKind = ActorKind.System,
                  ActorId = "ticket-grain", Kind = "Created",
                  Summary = $"Ticket created (source: {command.Source})."
              } ]
        };

        await _state.WriteStateAsync();
        _logger.LogInformation("Ticket {TicketId} created (externalRef={ExternalRef})",
            TicketId, command.ExternalRef);
        return _state.State;
    }

    public async Task TransitionTo(TicketStatus next, ActorKind actorKind, string actorId, string reason)
    {
        EnsureExists();
        if (string.IsNullOrWhiteSpace(reason))
            throw new GrainRuleException(GrainRuleException.InvalidState, "A transition reason is required.");

        var from = _state.State.Status;
        if (!TicketStatusTransitions.CanTransition(from, next))
            throw new GrainRuleException(GrainRuleException.InvalidTransition,
                $"Illegal ticket status transition: {from} -> {next}.");

        var now = _clock.UtcNow;
        _state.State.Status = next;
        _state.State.UpdatedAt = now;
        if (next == TicketStatus.Resolved)
            _state.State.ResolvedAt = now;  // resolution record

        AddActivity(actorKind, actorId, "StatusChanged", $"{from} -> {next}. Reason: {reason}");
        await _state.WriteStateAsync();

        _logger.LogInformation("Ticket {TicketId} transitioned {From}->{To} by {Actor} ({ActorId})",
            TicketId, from, next, actorKind, actorId);
    }

    public async Task ApplyTriage(TicketDomain domain, Severity severity, string actorId)
    {
        EnsureExists();
        var status = _state.State.Status;
        if (status is not (TicketStatus.New or TicketStatus.Triaging or TicketStatus.Triaged))
            throw new GrainRuleException(GrainRuleException.InvalidState,
                $"Triage cannot be applied in status {status}.");

        _state.State.Domain = domain;
        _state.State.Severity = severity;
        _state.State.UpdatedAt = _clock.UtcNow;
        AddActivity(ActorKind.Agent, actorId, "Triaged", $"domain={domain}, severity={severity}");
        await _state.WriteStateAsync();
    }

    public async Task AppendActivity(ActorKind actorKind, string actorId, string kind, string summary)
    {
        EnsureExists();
        _state.State.UpdatedAt = _clock.UtcNow;
        AddActivity(actorKind, actorId, kind, summary);
        await _state.WriteStateAsync();
    }

    public async Task SetAwaitingApproval(Guid approvalId)
    {
        EnsureExists();
        if (_state.State.Status != TicketStatus.AwaitingApproval)
            throw new GrainRuleException(GrainRuleException.InvalidState,
                $"Cannot enter approval-wait state from {_state.State.Status}.");

        _state.State.AwaitingApprovalId = approvalId;
        _state.State.UpdatedAt = _clock.UtcNow;
        AddActivity(ActorKind.System, "orchestrator", "ApprovalRequested",
            $"Awaiting approval {approvalId}.");
        await _state.WriteStateAsync();
    }

    public async Task ApprovalResolved(Guid approvalId, ApprovalStatus outcome, string decidedBy)
    {
        EnsureExists();
        if (_state.State.AwaitingApprovalId != approvalId)
            throw new GrainRuleException(GrainRuleException.InvalidState,
                $"Ticket is not awaiting approval {approvalId}.");

        _state.State.AwaitingApprovalId = null;
        _state.State.UpdatedAt = _clock.UtcNow;
        AddActivity(ActorKind.Human, decidedBy, "ApprovalResolved",
            $"Approval {approvalId} -> {outcome}.");
        await _state.WriteStateAsync();
    }

    private void EnsureExists()
    {
        if (!_state.State.Exists)
            throw new GrainRuleException(GrainRuleException.NotFound,
                $"Ticket {TicketId} does not exist.");
    }

    private void AddActivity(ActorKind actorKind, string actorId, string kind, string summary)
    {
        var list = _state.State.RecentActivity;
        list.Add(new ActivityEntry
        {
            OccurredAt = _clock.UtcNow, ActorKind = actorKind,
            ActorId = actorId, Kind = kind, Summary = summary
        });
        // Bound the timeline: keep the most recent entries.
        if (list.Count > MaxActivityEntries)
            list.RemoveRange(0, list.Count - MaxActivityEntries);
    }
}
