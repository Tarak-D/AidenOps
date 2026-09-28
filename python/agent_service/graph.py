from typing import TypedDict

from langgraph.graph import END, START, StateGraph

from agent_service.agents.triage import triage_ticket
from agent_service.agents.investigation import investigate_ticket
from agent_service.agents.tool_proposal import propose_tool
from agent_service.retrieval.service import retrieve_knowledge


class AgentState(TypedDict, total=False):
    correlation_id: str
    ticket_id: str
    title: str
    description: str
    reporter_email: str
    external_ref: str
    allowed_tools: list[dict]
    triage_confidence_threshold: float

    domain: str
    severity: str
    confidence: float

    knowledge: list[dict]
    knowledge_context: str
    investigation: dict

    decision: str
    tool_proposal: dict | None

    trace: list[dict]


class ResumeAgentState(TypedDict, total=False):
    correlation_id: str
    ticket_id: str
    action_execution_id: str
    action_status: str
    approval_status: str | None
    approval_decided_by: str | None
    tool_result_json: str | None
    tool_execution_error: str | None
    outcome: str
    error: str | None
    trace: list[dict]


def _append_trace(
    state: AgentState,
    trace: dict,
) -> list[dict]:
    existing = state.get("trace", [])

    return [
        *existing,
        trace,
    ]


def _build_knowledge_context(
    knowledge: list[dict],
) -> str:
    if not knowledge:
        return ""

    sections: list[str] = []

    for index, item in enumerate(knowledge, start=1):
        source = str(
            item.get("source")
            or "Unknown source"
        )

        title = str(
            item.get("title")
            or "Untitled"
        )

        content = str(
            item.get("content")
            or ""
        )

        similarity = item.get("similarity")

        if similarity is None:
            similarity_text = "unknown"
        else:
            similarity_text = f"{float(similarity):.4f}"

        sections.append(
            "\n".join(
                [
                    f"[Knowledge {index}]",
                    f"Source: {source}",
                    f"Title: {title}",
                    f"Similarity: {similarity_text}",
                    f"Content: {content}",
                ]
            )
        )

    return "\n\n".join(sections)


def triage_node(
    state: AgentState,
) -> AgentState:
    result, trace = triage_ticket(
        title=state["title"],
        description=state["description"],
    )

    return {
        **state,
        "domain": result.domain,
        "severity": result.severity,
        "confidence": result.confidence,
        "trace": _append_trace(
            state,
            {
                "agent": trace.agent,
                "step": trace.step_name,
                "model": trace.model,
                "prompt_version": trace.prompt_version,
                "prompt_tokens": trace.prompt_tokens,
                "completion_tokens": trace.completion_tokens,
                "latency_ms": trace.latency_ms,
                "summary": trace.summary,
            },
        ),
    }


def knowledge_node(
    state: AgentState,
) -> AgentState:
    knowledge, trace = retrieve_knowledge(
        query=(
            f"{state['title']} "
            f"{state['description']}"
        ),
    )

    return {
        **state,
        "knowledge": knowledge,
        "trace": _append_trace(
            state,
            {
                "agent": trace.agent,
                "step": trace.step_name,
                "model": trace.model,
                "prompt_version": trace.prompt_version,
                "prompt_tokens": trace.prompt_tokens,
                "completion_tokens": trace.completion_tokens,
                "latency_ms": trace.latency_ms,
                "summary": trace.summary,
            },
        ),
    }


def investigation_node(
    state: AgentState,
) -> AgentState:
    knowledge = state.get(
        "knowledge",
        [],
    )

    knowledge_context = state.get(
        "knowledge_context",
        "",
    )

    if not knowledge_context:
        knowledge_context = _build_knowledge_context(
            knowledge,
        )

    investigation, trace = investigate_ticket(
        title=state.get("title", ""),
        description=state.get("description", ""),
        domain=state.get("domain", "Unknown"),
        severity=state.get("severity", "P3"),
        confidence=state.get("confidence", 0.0),
        knowledge_context=knowledge_context,
        knowledge_count=len(knowledge),
    )

    return {
        **state,
        "knowledge_context": knowledge_context,
        "investigation": investigation,
        "trace": _append_trace(
            state,
            {
                "agent": trace.agent,
                "step": trace.step_name,
                "model": trace.model,
                "prompt_version": trace.prompt_version,
                "prompt_tokens": trace.prompt_tokens,
                "completion_tokens": trace.completion_tokens,
                "latency_ms": trace.latency_ms,
                "summary": trace.summary,
            },
        ),
    }


def tool_proposal_node(
    state: AgentState,
) -> AgentState:
    confidence = state.get("confidence", 0.0)
    threshold = state.get("triage_confidence_threshold", 0.6)
    if confidence < threshold:
        proposal = None
        trace_summary = (
            f"No tool proposal because triage confidence {confidence:.2f} "
            f"is below threshold {threshold:.2f}."
        )
        trace_agent = {
            "agent": "ToolProposalAgent",
            "step": "tool_proposal",
            "model": "deterministic/policy",
            "prompt_version": "v1-triage-confidence-policy",
            "prompt_tokens": 0,
            "completion_tokens": 0,
            "latency_ms": 0.0,
            "summary": trace_summary,
        }
        return {
            **state,
            "tool_proposal": proposal,
            "trace": _append_trace(state, trace_agent),
        }

    proposal, trace = propose_tool(
        title=state.get("title", ""),
        description=state.get("description", ""),
        reporter_email=state.get("reporter_email", ""),
        external_ref=state.get("external_ref", ""),
        domain=state.get("domain", "Unknown"),
        severity=state.get("severity", "P3"),
        investigation=state.get("investigation", {}),
        knowledge_context=state.get("knowledge_context", ""),
        allowed_tools=state.get("allowed_tools", []),
    )

    return {
        **state,
        "tool_proposal": proposal,
        "trace": _append_trace(
            state,
            {
                "agent": trace.agent,
                "step": trace.step_name,
                "model": trace.model,
                "prompt_version": trace.prompt_version,
                "prompt_tokens": trace.prompt_tokens,
                "completion_tokens": trace.completion_tokens,
                "latency_ms": trace.latency_ms,
                "summary": trace.summary,
            },
        ),
    }


def decision_node(
    state: AgentState,
) -> AgentState:
    confidence = state.get(
        "confidence",
        0.0,
    )

    investigation = state.get(
        "investigation",
        {},
    )

    recommendation = investigation.get(
        "recommendation",
    )
    tool_proposal = state.get("tool_proposal")

    knowledge_count = investigation.get(
        "knowledge_count",
        len(
            state.get(
                "knowledge",
                [],
            )
        ),
    )

    # Investigation-aware decision:
    #
    # When actual knowledge evidence exists,
    # InvestigationAgent becomes the primary
    # reasoning signal.
    #
    # When no knowledge was retrieved, preserve
    # the existing confidence-based fallback.
    if tool_proposal is not None:
        decision = "propose_tool"
    elif knowledge_count > 0:
        if recommendation == "resolve":
            decision = "resolve"

        elif recommendation in {
            "escalate",
            "investigate_further",
        }:
            decision = "escalate"

        else:
            # Unknown or malformed recommendation:
            # fail safely using the existing confidence
            # fallback.
            if confidence < 0.50:
                decision = "escalate"
            else:
                decision = "resolve"

    else:
        # Preserve existing behavior when RAG returned
        # no evidence.
        if confidence < 0.50:
            decision = "escalate"
        else:
            decision = "resolve"

    return {
        **state,
        "decision": decision,
        "tool_proposal": tool_proposal,
        "trace": _append_trace(
            state,
            {
                "agent": "DecisionAgent",
                "step": "decision",
                "model": "deterministic/bootstrap",
                "prompt_version": "v1-investigation-aware",
                "prompt_tokens": 0,
                "completion_tokens": 0,
                "latency_ms": 0.0,
                "summary": (
                    f"Decision={decision}; "
                    f"investigation_recommendation="
                    f"{recommendation}; "
                    f"knowledge_count="
                    f"{knowledge_count}; "
                    f"confidence="
                    f"{confidence:.2f}"
                ),
            },
        ),
    }


def route_decision(
    state: AgentState,
) -> str:
    decision = state.get(
        "decision",
        "escalate",
    )

    if decision == "resolve":
        return "resolve"

    if decision == "propose_tool":
        return "propose_tool"

    return "escalate"


def resolve_node(
    state: AgentState,
) -> AgentState:
    return {
        **state,
        "trace": _append_trace(
            state,
            {
                "agent": "DecisionAgent",
                "step": "resolve",
                "model": "deterministic/bootstrap",
                "prompt_version": "v0-bootstrap",
                "prompt_tokens": 0,
                "completion_tokens": 0,
                "latency_ms": 0.0,
                "summary": "Ticket resolved by bootstrap workflow.",
            },
        ),
    }


def escalate_node(
    state: AgentState,
) -> AgentState:
    return {
        **state,
        "trace": _append_trace(
            state,
            {
                "agent": "DecisionAgent",
                "step": "escalate",
                "model": "deterministic/bootstrap",
                "prompt_version": "v0-bootstrap",
                "prompt_tokens": 0,
                "completion_tokens": 0,
                "latency_ms": 0.0,
                "summary": "Ticket escalated by bootstrap workflow.",
            },
        ),
    }


def build_graph():
    graph = StateGraph(AgentState)

    graph.add_node(
        "triage",
        triage_node,
    )

    graph.add_node(
        "knowledge",
        knowledge_node,
    )

    graph.add_node(
        "investigation",
        investigation_node,
    )

    graph.add_node(
        "tool_proposal",
        tool_proposal_node,
    )

    graph.add_node(
        "decision",
        decision_node,
    )

    graph.add_node(
        "resolve",
        resolve_node,
    )

    graph.add_node(
        "escalate",
        escalate_node,
    )

    graph.add_edge(
        START,
        "triage",
    )

    graph.add_edge(
        "triage",
        "knowledge",
    )

    graph.add_edge(
        "knowledge",
        "investigation",
    )

    graph.add_edge(
        "investigation",
        "tool_proposal",
    )

    graph.add_edge(
        "tool_proposal",
        "decision",
    )

    graph.add_conditional_edges(
        "decision",
        route_decision,
        {
            "resolve": "resolve",
            "escalate": "escalate",
            "propose_tool": END,
        },
    )

    graph.add_edge(
        "resolve",
        END,
    )

    graph.add_edge(
        "escalate",
        END,
    )

    return graph.compile()


def resume_action_node(state: ResumeAgentState) -> ResumeAgentState:
    """Continue the workflow using the action result supplied by .NET."""
    action_status = state.get("action_status", "")
    succeeded = action_status == "Succeeded"
    result = state.get("tool_result_json") or "No result was persisted."
    error = state.get("tool_execution_error")
    if not succeeded and not error:
        error = result

    return {
        **state,
        "outcome": "Resolved" if succeeded else "Escalated",
        "error": None if succeeded else error,
        "trace": [{
            "agent": "ValidationAgent",
            "step": "resume",
            "model": "deterministic/server-result",
            "prompt_version": "v1-server-authoritative-resume",
            "prompt_tokens": 0,
            "completion_tokens": 0,
            "latency_ms": 0.0,
            "summary": (
                f"Action {state.get('action_execution_id', '')} "
                f"finished with persisted status {action_status}. "
                f"Approval status: {state.get('approval_status') or 'NotRequired'}. "
                f"Result: {result}"
            ),
        }],
    }


def build_resume_graph():
    graph = StateGraph(ResumeAgentState)
    graph.add_node("resume_action", resume_action_node)
    graph.add_edge(START, "resume_action")
    graph.add_edge("resume_action", END)
    return graph.compile()
