namespace AIOps.Domain;

/// <summary>Raised when a domain invariant or state-machine rule is violated.</summary>
public sealed class DomainInvariantViolationException : Exception
{
    public DomainInvariantViolationException(string message) : base(message) { }
}
