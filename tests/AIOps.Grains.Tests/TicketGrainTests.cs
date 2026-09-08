using AIOps.Abstractions.Grains;
using AIOps.Domain;
using Orleans.TestingHost;
using Xunit;

namespace AIOps.Grains.Tests;

[Collection(GrainClusterCollection.Name)]
public sealed class TicketGrainTests
{
    private readonly IGrainFactory _factory;

    public TicketGrainTests(TicketGrainFixture fixture) => _factory = fixture.GrainFactory;

    private ITicketGrain GrainFor(Guid id) => _factory.GetGrain<ITicketGrain>(id);

    private static CreateTicketCommand Cmd() => new(
        "VPN keeps dropping", "Details here", "user@corp.example", null,
        TicketSource.Manual, "EXT-1");

    [Fact]
    public async Task Create_then_GetState_roundtrips()
    {
        var grain = GrainFor(Guid.NewGuid());
        var created = await grain.Create(Cmd());

        Assert.True(created.Exists);
        Assert.Equal(TicketStatus.New, created.Status);
        Assert.Equal("VPN keeps dropping", created.Title);
        Assert.Single(created.RecentActivity);

        var fetched = await grain.GetState();
        Assert.True(fetched.Exists);
    }

    [Fact]
    public async Task Double_create_is_rejected()
    {
        var grain = GrainFor(Guid.NewGuid());
        await grain.Create(Cmd());
        var ex = await Assert.ThrowsAsync<GrainRuleException>(() => grain.Create(Cmd()));
        Assert.Equal(GrainRuleException.AlreadyExists, ex.Code);
    }

    [Fact]
    public async Task Valid_transition_updates_status_and_records_activity()
    {
        var grain = GrainFor(Guid.NewGuid());
        await grain.Create(Cmd());
        await grain.TransitionTo(TicketStatus.Triaging, ActorKind.Human, "jane", "starting triage");

        var state = await grain.GetState();
        Assert.Equal(TicketStatus.Triaging, state.Status);
        Assert.Equal(2, state.RecentActivity.Count);
        Assert.Equal("StatusChanged", state.RecentActivity[^1].Kind);
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]          // skipping the pipeline
    [InlineData(TicketStatus.ActionExecuting)]
    public async Task Illegal_transition_from_New_is_rejected(TicketStatus to)
    {
        var grain = GrainFor(Guid.NewGuid());
        await grain.Create(Cmd());
        var ex = await Assert.ThrowsAsync<GrainRuleException>(() =>
            grain.TransitionTo(to, ActorKind.Human, "x", "trying to skip"));
        Assert.Equal(GrainRuleException.InvalidTransition, ex.Code);
        Assert.Equal(TicketStatus.New, (await grain.GetState()).Status);
    }
}
