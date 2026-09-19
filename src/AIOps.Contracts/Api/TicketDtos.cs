using AIOps.Domain;

namespace AIOps.Contracts.Api;

public sealed record CreateTicketRequest(
    string Title,
    string Description,
    string ReporterEmail,
    string? ReporterName = null,
    TicketSource Source = TicketSource.Manual,
    string? ExternalRef = null);

public sealed record TransitionTicketRequest(
    TicketStatus ToStatus,
    string Reason);

public sealed record TicketDetailDto(
    Guid Id,
    string? ExternalRef,
    string Title,
    string Description,
    string ReporterEmail,
    string? ReporterName,
    TicketDomain Domain,
    Severity Severity,
    TicketStatus Status,
    TicketSource Source,
    Guid? AwaitingApprovalId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    IReadOnlyList<TimelineEntryDto> Activity);

public sealed record TicketSummaryDto(
    Guid Id,
    string ExternalRef,
    string Title,
    TicketDomain Domain,
    Severity Severity,
    TicketStatus Status,
    ActorKind? LastActor,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TimelineEntryDto(
    DateTimeOffset OccurredAt,
    ActorKind ActorKind,
    string ActorId,
    string Kind,
    string Summary);

public sealed record CreateApprovalRequest(
    string Justification);

public sealed record DecideApprovalRequest(
    bool Approve,
    string? Comment);

public sealed record ApprovalResponse(
    Guid Id,
    Guid ActionExecutionId,
    Guid TicketId,
    string RequestedBy,
    string Justification,
    ApprovalStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? DecisionComment);