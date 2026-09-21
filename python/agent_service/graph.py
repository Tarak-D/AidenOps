from typing import TypedDict

from langgraph.graph import END, START, StateGraph

from agent_service.agents.triage import triage_ticket
from agent_service.retrieval.service import retrieve_knowledge


class AgentState(TypedDict, total=False):
    correlation_id: str
    ticket_id: str
    title: str
    description: str
    reporter_email: str

    domain: str
    severity: str
    confidence: float

    knowledge: list[dict]
    investigation: dict

    decision: str
    tool_proposal: dict | None

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
    investigation = {
        "domain": state.get("domain", "Unknown"),
        "severity": state.get("severity", "P3"),
        "confidence": state.get("confidence", 0.0),
        "knowledge_count": len(
            state.get("knowledge", [])
        ),
    }

    return {
        **state,
        "investigation": investigation,
        "trace": _append_trace(
            state,
            {
                "agent": "InvestigationAgent",
                "step": "investigation",
                "model": "deterministic/bootstrap",
                "prompt_version": "v0-bootstrap",
                "prompt_tokens": 0,
                "completion_tokens": 0,
                "latency_ms": 0.0,
                "summary": (
                    "Deterministic investigation completed "
                    f"for domain={state.get('domain', 'Unknown')}"
                ),
            },
        ),
    }


def decision_node(
    state: AgentState,
) -> AgentState:
    confidence = state.get("confidence", 0.0)

    if confidence < 0.50:
        decision = "escalate"
    else:
        decision = "resolve"

    return {
        **state,
        "decision": decision,
        "tool_proposal": None,
        "trace": _append_trace(
            state,
            {
                "agent": "DecisionAgent",
                "step": "decision",
                "model": "deterministic/bootstrap",
                "prompt_version": "v0-bootstrap",
                "prompt_tokens": 0,
                "completion_tokens": 0,
                "latency_ms": 0.0,
                "summary": (
                    f"Decision={decision} "
                    f"with confidence={confidence:.2f}"
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
        "decision",
    )

    graph.add_conditional_edges(
        "decision",
        route_decision,
        {
            "resolve": "resolve",
            "escalate": "escalate",
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