from agent_service.models.agent import AgentTrace, TriageResult


def triage_ticket(
    title: str,
    description: str,
) -> tuple[TriageResult, AgentTrace]:
    """
    Deterministic Phase 9 bootstrap triage.

    This is intentionally simple.
    The real model-backed triage agent will replace this implementation
    after the service-to-.NET contract is established.
    """

    text = f"{title} {description}".lower()

    if any(
        word in text
        for word in ("vpn", "network", "wifi")
    ):
        domain = "Network"
    elif any(
        word in text
        for word in ("password", "login", "access", "locked")
    ):
        domain = "Identity"
    elif any(
        word in text
        for word in ("database", "sql", "query")
    ):
        domain = "Database"
    elif any(
        word in text
        for word in ("server", "disk", "cpu", "ec2")
    ):
        domain = "Infrastructure"
    else:
        domain = "Unknown"

    if any(
        word in text
        for word in ("down", "outage", "production")
    ):
        severity = "P1"
    elif any(
        phrase in text
        for phrase in ("urgent", "cannot work")
    ):
        severity = "P2"
    else:
        severity = "P3"

    confidence = 0.85 if domain != "Unknown" else 0.35

    result = TriageResult(
        domain=domain,
        severity=severity,
        confidence=confidence,
    )

    trace = AgentTrace(
        agent="TriageAgent",
        step_name="triage",
        model="deterministic/bootstrap",
        prompt_version="v0-bootstrap",
        summary=(
            f"Bootstrap triage: "
            f"domain={domain}, severity={severity}"
        ),
    )

    return result, trace