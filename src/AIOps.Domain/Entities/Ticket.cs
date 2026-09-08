namespace AIOps.Domain.Entities;

/// <summary>
/// An IT support ticket. Durable per-ticket state is owned at runtime by the Orleans
/// Ticket grain; this type models the persisted/read-side shape.
/// </summary>
public sealed class Ticket
{
    public Guid Id { get; private set; }
    public string ExternalRef { get; private set; } = default!; // e.g. INC-000123
    public string Title { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public string ReporterEmail { get; private set; } = default!;
    public string? ReporterName { get; private set; }
    public TicketDomain Domain { get; private set; } = TicketDomain.Unknown;
    public Severity Severity { get; private set; } = Severity.P3;
    public TicketStatus Status { get; private set; } = TicketStatus.New;
    public TicketSource Source { get; private set; } = TicketSource.Manual;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    private Ticket() { } // persistence

#pragma warning disable CS8618 // guaranteed by guard clauses below
    public static Ticket Create(
        string title,
        string description,
        string reporterEmail,
        string? reporterName = null,
        TicketSource source = TicketSource.Manual,
        string? externalRef = null,
        Guid? id = null,
        DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new DomainInvariantViolationException("Ticket title is required.");
        if (title.Length > 200) throw new DomainInvariantViolationException("Ticket title must be <= 200 characters.");
        if (string.IsNullOrWhiteSpace(description)) throw new DomainInvariantViolationException("Ticket description is required.");
        if (description.Length > 4000) throw new DomainInvariantViolationException("Ticket description must be <= 4000 characters.");
        if (string.IsNullOrWhiteSpace(reporterEmail) || !reporterEmail.Contains('@'))
            throw new DomainInvariantViolationException("A valid reporter email is required.");

        var ts = now ?? DateTimeOffset.UtcNow;
        return new Ticket
        {
            Id = id ?? Guid.NewGuid(),
            ExternalRef = string.IsNullOrWhiteSpace(externalRef) ? $"INC-{Guid.NewGuid():N}"[..13].ToUpperInvariant() : externalRef,
            Title = title.Trim(),
            Description = description.Trim(),
            ReporterEmail = reporterEmail.Trim().ToLowerInvariant(),
            ReporterName = reporterName,
            Source = source,
            CreatedAt = ts,
            UpdatedAt = ts
        };
    }
#pragma warning restore CS8618

    /// <summary>Set triage results. Only valid while triaging/triaged.</summary>
    public void ApplyTriage(TicketDomain domain, Severity severity, DateTimeOffset now)
    {
        if (Status is not (TicketStatus.Triaging or TicketStatus.Triaged or TicketStatus.New))
            throw new DomainInvariantViolationException($"Cannot apply triage while ticket is {Status}.");
        Domain = domain;
        Severity = severity;
        UpdatedAt = now;
    }

    /// <summary>Mark resolved; only legal from Verifying (enforced via transitions elsewhere).</summary>
    public void MarkResolved(DateTimeOffset now)
    {
        if (Status != TicketStatus.Resolved)
            throw new DomainInvariantViolationException("Ticket status must be Resolved before MarkResolved.");
        ResolvedAt = now;
        UpdatedAt = now;
    }

    public override string ToString() => $"{ExternalRef} [{Status}] {Title}";
}
