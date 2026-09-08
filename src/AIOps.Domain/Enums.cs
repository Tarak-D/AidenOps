namespace AIOps.Domain;

/// <summary>IT support domain a ticket belongs to.</summary>
public enum TicketDomain
{
    Unknown = 0,
    Network = 1,
    Identity = 2,
    Database = 3,
    Infrastructure = 4
}

/// <summary>Ticket severity. P1 = most critical.</summary>
public enum Severity
{
    P4 = 0,
    P3 = 1,
    P2 = 2,
    P1 = 3
}

/// <summary>
/// Business lifecycle of a ticket. Transitions are validated by
/// <see cref="TicketStatusTransitions"/> and enforced by the Ticket grain (single writer).
/// </summary>
public enum TicketStatus
{
    New = 0,
    Triaging = 1,
    Triaged = 2,
    Investigating = 3,
    KnowledgeRetrieval = 4,
    ResolutionProposed = 5,
    AwaitingApproval = 6,
    ActionExecuting = 7,
    Verifying = 8,
    Resolved = 9,
    Escalating = 10,
    Escalated = 11,
    Failed = 12
}

public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Expired = 3
}

/// <summary>Risk classification of a tool action. Sensitive actions ALWAYS require human approval.</summary>
public enum RiskLevel
{
    Safe = 0,
    Moderate = 1,
    Sensitive = 2
}

public enum ActionStatus
{
    Proposed = 0,
    AwaitingApproval = 1,
    Approved = 2,
    Executing = 3,
    Succeeded = 4,
    Failed = 5,
    Rejected = 6,
    Cancelled = 7
}

/// <summary>Who performed an activity. Used for provenance display and audit.</summary>
public enum ActorKind
{
    System = 0,
    Agent = 1,
    Human = 2
}

public enum TicketSource
{
    Manual = 0,
    ApiIngestion = 1,
    BatchIngestion = 2,
    SeededDemo = 3
}

public enum UserRole
{
    Viewer = 0,
    Engineer = 1,
    Approver = 2,
    Admin = 3
}
