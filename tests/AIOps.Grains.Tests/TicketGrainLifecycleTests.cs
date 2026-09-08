using AIOps.Abstractions.Grains;
using AIOps.Domain;
using Orleans.TestingHost;
using Xunit;

namespace AIOps.Grains.Tests;

[Collection(GrainClusterCollection.Name)]
public sealed class TicketGrainLifecycleTests
{
    private readonly IGrainFactory _factory;
    public TicketGrainLifecycleTests(TicketGrainFixture fixture) => _factory = fixture.GrainFactory;

    private ITicketGrain NewGrain() => _factory.GetGrain<ITicketGrain>(Guid.NewGuid());

    /// <summary>Walks the grain to the given status along a valid path.</summary>
    private static async Task RunToAsync(ITicketGrain grain, TicketStatus target)
    {
        if (!(await grain.GetState()).Exists)
            await grain.Create(new CreateTicketCommand("t", "d", "a@b.c", null, TicketSource.Manual, null));
        var path = new Dictionary<TicketStatus, TicketStatus[]>
        {
            [TicketStatus.Triaging] = [TicketStatus.Triaging],
            [TicketStatus.Triaged] = [TicketStatus.Triaging, TicketStatus.Triaged],
            [TicketStatus.Investigating] = [TicketStatus.Triaging, TicketStatus.Triaged, TicketStatus.Investigating],
            [TicketStatus.ResolutionProposed] = [TicketStatus.Triaging, TicketStatus.Triaged, TicketStatus.Investigating, TicketStatus.ResolutionProposed],
            [TicketStatus.AwaitingApproval] = [TicketStatus.Triaging, TicketStatus.Triaged, TicketStatus.Investigating, TicketStatus.ResolutionProposed, TicketStatus.AwaitingApproval],
            [TicketStatus.ActionExecuting] = [TicketStatus.Triaging, TicketStatus.Triaged, TicketStatus.Investigating, TicketStatus.ResolutionProposed, TicketStatus.ActionExecuting],
            [TicketStatus.Verifying] = [TicketStatus.Triaging, TicketStatus.Triaged, TicketStatus.Investigating, TicketStatus.ResolutionProposed, TicketStatus.ActionExecuting, TicketStatus.Verifying],
            [TicketStatus.Resolved] = [TicketStatus.Triaging, TicketStatus.Triaged, TicketStatus.Investigating, TicketStatus.ResolutionProposed, TicketStatus.ActionExecuting, TicketStatus.Verifying, TicketStatus.Resolved],
        };
        foreach (var step in path[target])
            await grain.TransitionTo(step, ActorKind.Human, "test", "step");
    }

    [Fact]
    public async Task Terminal_Resolved_rejects_all_further_transitions_and_records_resolution_time()
    {
        var grain = NewGrain();
        await RunToAsync(grain, TicketStatus.Resolved);

        var state = await grain.GetState();
        Assert.Equal(TicketStatus.Resolved, state.Status);
        Assert.NotNull(state.ResolvedAt);

        var ex = await Assert.ThrowsAsync<GrainRuleException>(() =>
            grain.TransitionTo(TicketStatus.Triaging, ActorKind.Human, "a", "reopen"));
        Assert.Equal(GrainRuleException.InvalidTransition, ex.Code);
    }

    [Fact]
    public async Task Operations_on_missing_ticket_fail_with_NotFound()
    {
        var grain = NewGrain();
        var ex = await Assert.ThrowsAsync<GrainRuleException>(() =>
            grain.TransitionTo(TicketStatus.Triaging, ActorKind.Human, "a", "r"));
        Assert.Equal(GrainRuleException.NotFound, ex.Code);
        Assert.False((await grain.GetState()).Exists);
    }

    [Fact]
    public async Task Empty_reason_is_rejected()
    {
        var grain = NewGrain();
        await grain.Create(new CreateTicketCommand("t", "d", "a@b.c", null, TicketSource.Manual, null));
        var ex = await Assert.ThrowsAsync<GrainRuleException>(() =>
            grain.TransitionTo(TicketStatus.Triaging, ActorKind.Human, "a", "  "));
        Assert.Equal(GrainRuleException.InvalidState, ex.Code);
    }

    [Fact]
    public async Task Triage_updates_domain_and_severity_but_not_after_investigating()
    {
        var grain = NewGrain();
        await grain.Create(new CreateTicketCommand("t", "d", "a@b.c", null, TicketSource.Manual, null));
        await grain.ApplyTriage(TicketDomain.Network, Severity.P2, "triage-agent");
        var state = await grain.GetState();
        Assert.Equal(TicketDomain.Network, state.Domain);
        Assert.Equal(Severity.P2, state.Severity);

        await RunToAsync(grain, TicketStatus.Investigating);
        var ex = await Assert.ThrowsAsync<GrainRuleException>(() =>
            grain.ApplyTriage(TicketDomain.Infrastructure, Severity.P1, "late-agent"));
        Assert.Equal(GrainRuleException.InvalidState, ex.Code);
    }

    [Fact]
    public async Task Approval_bookkeeping_roundtrip_and_mismatch_rejection()
    {
        var grain = NewGrain();
        await RunToAsync(grain, TicketStatus.AwaitingApproval);

        var approvalId = Guid.NewGuid();
        await grain.SetAwaitingApproval(approvalId);
        Assert.Equal(approvalId, (await grain.GetState()).AwaitingApprovalId);

        var mismatch = await Assert.ThrowsAsync<GrainRuleException>(() =>
            grain.ApprovalResolved(Guid.NewGuid(), ApprovalStatus.Approved, "boss"));
        Assert.Equal(GrainRuleException.InvalidState, mismatch.Code);

        await grain.ApprovalResolved(approvalId, ApprovalStatus.Approved, "boss@corp.example");
        Assert.Null((await grain.GetState()).AwaitingApprovalId);
        Assert.Contains((await grain.GetState()).RecentActivity,
            a => a.Kind == "ApprovalResolved");
    }
}
