from fastapi import FastAPI
from pydantic import BaseModel, Field

from agent_service.graph import build_graph


app = FastAPI(
    title="AIOps Agent Service",
    version="0.1.0",
)


class HealthResponse(BaseModel):
    status: str


class AgentRunRequest(BaseModel):
    correlation_id: str
    ticket_id: str
    title: str
    description: str
    reporter_email: str = ""
    domain: str = "Unknown"
    severity: str = "P3"


class ResumeAgentRunRequest(BaseModel):
    correlation_id: str
    ticket_id: str
    approval_granted: bool
    approval_decided_by: str | None = None
    tool_result_json: str | None = None
    tool_execution_succeeded: bool


class AgentRunResponse(BaseModel):
    correlation_id: str
    outcome: str
    triage: dict = Field(default_factory=dict)
    tool_proposal: dict | None = None
    trace: list[dict] = Field(default_factory=list)
    error: str | None = None


def _normalize_api_trace(
    trace: list[dict],
) -> list[dict]:
    """
    Translate the internal Python trace contract into
    the external API contract.

    Internal triage trace:

        agent = "triage"
        step  = "deterministic_triage"
        OR
        step  = "llm_triage"

    External API trace:

        agent = "TriageAgent"
        step  = "triage"

    InvestigationAgent also keeps an external stable
    API step name of "investigation".
    """

    normalized: list[dict] = []

    for entry in trace:
        item = dict(entry)

        if (
            item.get("agent") == "triage"
            and item.get("step")
            in {
                "deterministic_triage",
                "llm_triage",
            }
        ):
            item["agent"] = "TriageAgent"
            item["step"] = "triage"

        elif item.get("agent") == "InvestigationAgent":
            item["step"] = "investigation"

        normalized.append(item)

    return normalized


@app.get(
    "/health",
    response_model=HealthResponse,
)
async def health() -> HealthResponse:
    return HealthResponse(
        status="healthy",
    )


@app.post(
    "/api/v1/agent/runs",
    response_model=AgentRunResponse,
)
async def start_agent_run(
    request: AgentRunRequest,
) -> AgentRunResponse:
    graph = build_graph()

    result = graph.invoke(
        {
            "correlation_id": request.correlation_id,
            "ticket_id": request.ticket_id,
            "title": request.title,
            "description": request.description,
            "reporter_email": request.reporter_email,
        }
    )

    decision = result.get(
        "decision",
        "escalate",
    )

    if decision == "resolve":
        outcome = "Resolved"

    elif decision == "propose_tool":
        outcome = "AwaitingApproval"

    else:
        outcome = "Escalated"

    trace = _normalize_api_trace(
        result.get(
            "trace",
            [],
        )
    )

    return AgentRunResponse(
        correlation_id=request.correlation_id,
        outcome=outcome,
        triage={
            "domain": result.get(
                "domain",
                "Unknown",
            ),
            "severity": result.get(
                "severity",
                "P3",
            ),
            "confidence": result.get(
                "confidence",
                0.0,
            ),
        },
        tool_proposal=result.get(
            "tool_proposal",
        ),
        trace=trace,
        error=result.get(
            "error",
        ),
    )


@app.post(
    "/api/v1/agent/runs/resume",
    response_model=AgentRunResponse,
)
async def resume_agent_run(
    request: ResumeAgentRunRequest,
) -> AgentRunResponse:
    if (
        request.approval_granted
        and request.tool_execution_succeeded
    ):
        return AgentRunResponse(
            correlation_id=request.correlation_id,
            outcome="Resolved",
            triage={},
            tool_proposal=None,
            trace=[
                {
                    "agent": "ValidationAgent",
                    "step": "verify",
                    "model": "deterministic/bootstrap",
                    "prompt_version": "v0-bootstrap",
                    "prompt_tokens": 0,
                    "completion_tokens": 0,
                    "latency_ms": 0.0,
                    "summary": (
                        "Post-action verification "
                        "reported success."
                    ),
                }
            ],
            error=None,
        )

    return AgentRunResponse(
        correlation_id=request.correlation_id,
        outcome="Escalated",
        triage={},
        tool_proposal=None,
        trace=[
            {
                "agent": "ValidationAgent",
                "step": "verify",
                "model": "deterministic/bootstrap",
                "prompt_version": "v0-bootstrap",
                "prompt_tokens": 0,
                "completion_tokens": 0,
                "latency_ms": 0.0,
                "summary": (
                    "Approval was not granted or "
                    "tool execution did not succeed."
                ),
            }
        ],
        error=None,
    )