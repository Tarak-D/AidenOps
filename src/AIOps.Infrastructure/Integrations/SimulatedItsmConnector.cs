using AIOps.Abstractions.Integrations;

namespace AIOps.Infrastructure.Integrations;

public sealed class SimulatedItsmConnector : IItsmProviderConnector
{
    public string ProviderName => "Simulated";
    public string DisplayName => "Simulated";
    public string Mode => "Simulated";
    public bool IsProduction => false;

    public Task<string> GetConnectionStatusAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult("Simulated");
    }

    public Task<string> UpdateTicketAsync(
        string externalRef,
        string note,
        string? state = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(note);

        var escapedRef = externalRef.Replace("\"", "\\\"");
        var escapedNote = note.Replace("\"", "\\\"");
        var escapedState = state?.Replace("\"", "\\\"");

        return Task.FromResult(
            $$"""{"externalRef":"{{escapedRef}}","note":"{{escapedNote}}","state":{{(escapedState is null ? "null" : $"\"{escapedState}\"")}},"status":"updated","provider":"simulated"}""");
    }
}
