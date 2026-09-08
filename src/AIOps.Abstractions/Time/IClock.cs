namespace AIOps.Abstractions.Time;

/// <summary>Testable time source. Never call DateTime.UtcNow in domain/orchestration code.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
