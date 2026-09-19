using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Tools;
using AIOps.Domain;
using Xunit;

namespace AIOps.Tools.Tests;

public sealed class UpdateTicketToolTests
{
    private sealed class FakeItsmConnector : IItsmConnector
    {
        public bool WasCalled { get; private set; }
        public string? LastExternalRef { get; private set; }
        public string? LastNote { get; private set; }
        public string? LastState { get; private set; }

        public string ResultToReturn { get; set; } =
            """{"status":"updated"}""";

        public Exception? ExceptionToThrow { get; set; }

        public Task<string> UpdateTicketAsync(
            string externalRef,
            string note,
            string? state = null,
            CancellationToken ct = default)
        {
            WasCalled = true;
            LastExternalRef = externalRef;
            LastNote = note;
            LastState = state;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(ResultToReturn);
        }
    }

    private static ToolExecutionContext CreateContext(Guid? approvalId = null)
    {
        return new ToolExecutionContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "test-agent",
            approvalId);
    }

    [Fact]
    public void Metadata_IsModerateAndRequiresApproval()
    {
        var connector = new FakeItsmConnector();
        var sut = new UpdateTicketTool(connector);

        Assert.Equal("ITSM.UpdateTicket", sut.Name);
        Assert.Equal(RiskLevel.Moderate, sut.Risk);
        Assert.True(sut.RequiresApproval);
    }

    [Fact]
    public async Task WithoutApproval_IsRejected()
    {
        var connector = new FakeItsmConnector();
        var sut = new UpdateTicketTool(connector);

        var result = await sut.ExecuteAsync(
            """{"externalRef":"INC-123","note":"Investigating"}""",
            CreateContext());

        Assert.False(result.Success);
        Assert.Equal("ApprovalRequired", result.Error);
        Assert.False(connector.WasCalled);
    }

    [Fact]
    public async Task WithApproval_UpdatesTicket()
    {
        var connector = new FakeItsmConnector
        {
            ResultToReturn =
                """{"externalRef":"INC-123","status":"updated"}"""
        };

        var sut = new UpdateTicketTool(connector);

        var result = await sut.ExecuteAsync(
            """{"externalRef":"INC-123","note":"Resolved","state":"Resolved"}""",
            CreateContext(Guid.NewGuid()));

        Assert.True(result.Success);
        Assert.Contains("updated successfully", result.Summary);
        Assert.Equal(
            """{"externalRef":"INC-123","status":"updated"}""",
            result.DetailsJson);

        Assert.True(connector.WasCalled);
        Assert.Equal("INC-123", connector.LastExternalRef);
        Assert.Equal("Resolved", connector.LastNote);
        Assert.Equal("Resolved", connector.LastState);
    }

    [Fact]
    public async Task MissingExternalRef_IsRejected()
    {
        var connector = new FakeItsmConnector();
        var sut = new UpdateTicketTool(connector);

        var result = await sut.ExecuteAsync(
            """{"note":"test"}""",
            CreateContext(Guid.NewGuid()));

        Assert.False(result.Success);
        Assert.Equal("InvalidArguments", result.Error);
        Assert.False(connector.WasCalled);
    }

    [Fact]
    public async Task MissingNote_IsRejected()
    {
        var connector = new FakeItsmConnector();
        var sut = new UpdateTicketTool(connector);

        var result = await sut.ExecuteAsync(
            """{"externalRef":"INC-123"}""",
            CreateContext(Guid.NewGuid()));

        Assert.False(result.Success);
        Assert.Equal("InvalidArguments", result.Error);
        Assert.False(connector.WasCalled);
    }

    [Fact]
    public async Task InvalidJson_IsRejected()
    {
        var connector = new FakeItsmConnector();
        var sut = new UpdateTicketTool(connector);

        var result = await sut.ExecuteAsync(
            """{"externalRef":"INC-123" """,
            CreateContext(Guid.NewGuid()));

        Assert.False(result.Success);
        Assert.Equal("InvalidArguments", result.Error);
        Assert.False(connector.WasCalled);
    }

    [Fact]
    public async Task ProviderFailure_ReturnsFailedToolResult()
    {
        var connector = new FakeItsmConnector
        {
            ExceptionToThrow =
                new InvalidOperationException("ITSM unavailable.")
        };

        var sut = new UpdateTicketTool(connector);

        var result = await sut.ExecuteAsync(
            """{"externalRef":"INC-123","note":"test"}""",
            CreateContext(Guid.NewGuid()));

        Assert.False(result.Success);
        Assert.Equal("ITSM unavailable.", result.Error);
        Assert.True(connector.WasCalled);
    }
}