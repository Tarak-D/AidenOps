from __future__ import annotations

import os

import pytest

from agent_service import graph
from agent_service.agents.triage import (
    triage_ticket,
)
from agent_service.graph import (
    investigation_node,
)


def test_deterministic_triage_network_p1(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        "deterministic",
    )

    result, trace = triage_ticket(
        "Production network outage. All users cannot connect."
    )

    assert result.domain == "Network"
    assert result.severity == "P1"
    assert result.confidence == 0.75

    assert trace.agent == "triage"
    assert trace.step_name == "deterministic_triage"
    assert trace.model == "deterministic/bootstrap"


def test_deterministic_triage_identity_p2(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        "deterministic",
    )

    result, trace = triage_ticket(
        "Authentication is slow and intermittent for multiple users."
    )

    assert result.domain == "Identity"
    assert result.severity == "P2"

    assert trace.agent == "triage"
    assert trace.step_name == "deterministic_triage"


def test_deterministic_triage_database_p3(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        "deterministic",
    )

    result, trace = triage_ticket(
        "A database query needs investigation."
    )

    assert result.domain == "Database"
    assert result.severity == "P3"


class FakeResponse:
    def __init__(self) -> None:
        self.content = (
            '{"domain":"Network",'
            '"severity":"P2",'
            '"confidence":0.91}'
        )

        self.model = "fake-model"
        self.prompt_tokens = 10
        self.completion_tokens = 20
        self.latency_ms = 12.5


class FakeClient:
    def __init__(
        self,
        provider: str,
    ) -> None:
        self.provider = provider

    def chat(
        self,
        *,
        system_prompt: str,
        user_prompt: str,
        temperature: float,
        max_tokens: int,
        response_format: dict,
    ) -> FakeResponse:
        assert system_prompt
        assert user_prompt

        assert temperature == 0.0
        assert max_tokens == 512

        if self.provider == "openrouter":
            assert response_format == {
                "type": "json_schema",
                "json_schema": {
                    "name": "aidenops_triage",
                    "strict": True,
                    "schema": {
                        "type": "object",
                        "properties": {
                            "domain": {
                                "type": "string",
                                "enum": [
                                    "Network",
                                    "Identity",
                                    "Database",
                                    "Infrastructure",
                                    "Unknown",
                                ],
                                "description": (
                                    "The incident domain."
                                ),
                            },
                            "severity": {
                                "type": "string",
                                "enum": [
                                    "P1",
                                    "P2",
                                    "P3",
                                ],
                                "description": (
                                    "The incident severity."
                                ),
                            },
                            "confidence": {
                                "type": "number",
                                "description": (
                                    "Classification confidence "
                                    "from 0.0 to 1.0."
                                ),
                            },
                        },
                        "required": [
                            "domain",
                            "severity",
                            "confidence",
                        ],
                        "additionalProperties": False,
                    },
                },
            }

        else:
            assert response_format == {
                "type": "json_object",
            }

        return FakeResponse()


@pytest.mark.parametrize(
    "provider",
    [
        "nvidia",
        "openrouter",
        "openai",
        "anthropic",
        "google",
        "azure",
        "azure_openai",
        "azure-openai",
    ],
)
def test_provider_triage(
    monkeypatch: pytest.MonkeyPatch,
    provider: str,
) -> None:
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        provider,
    )

    import agent_service.agents.triage as triage_module

    monkeypatch.setattr(
        triage_module,
        "create_llm_client",
        lambda provider: FakeClient(provider),
    )

    result, trace = triage_ticket(
        "Production network latency affecting users."
    )

    assert result.domain == "Network"
    assert result.severity == "P2"
    assert result.confidence == 0.91

    assert trace.agent == "triage"
    assert trace.step_name == "llm_triage"
    assert trace.model == "fake-model"
    assert trace.prompt_tokens == 10
    assert trace.completion_tokens == 20
    assert trace.latency_ms == 12.5


def test_provider_is_read_from_environment(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        "deterministic",
    )

    assert (
        os.getenv("AGENT_LLM_PROVIDER")
        == "deterministic"
    )


def test_investigation_injects_retrieved_knowledge_context() -> None:
    state = {
        "domain": "Network",
        "severity": "P1",
        "confidence": 0.75,
        "knowledge": [
            {
                "source": "VPN Runbook",
                "title": "VPN Authentication Failure",
                "content": (
                    "Verify VPN credentials and "
                    "check account lock status."
                ),
                "similarity": 0.91,
            },
            {
                "source": "Network Runbook",
                "title": "VPN Connectivity",
                "content": (
                    "Check tunnel status and "
                    "network connectivity."
                ),
                "similarity": 0.84,
            },
        ],
    }

    result = investigation_node(state)

    context = result["knowledge_context"]

    assert "[Knowledge 1]" in context
    assert "Source: VPN Runbook" in context
    assert "Title: VPN Authentication Failure" in context
    assert "Similarity: 0.9100" in context
    assert (
        "Verify VPN credentials and check account lock status."
        in context
    )

    assert "[Knowledge 2]" in context
    assert "Source: Network Runbook" in context
    assert "Title: VPN Connectivity" in context
    assert "Similarity: 0.8400" in context
    assert (
        "Check tunnel status and network connectivity."
        in context
    )

    assert result["investigation"]["knowledge_count"] == 2
    assert (
        result["investigation"]["knowledge_context"]
        == context
    )


def test_investigation_handles_empty_knowledge() -> None:
    state = {
        "domain": "Unknown",
        "severity": "P3",
        "confidence": 0.35,
        "knowledge": [],
    }

    result = investigation_node(state)

    assert result["knowledge_context"] == ""
    assert result["investigation"]["knowledge_count"] == 0
    assert result["investigation"]["knowledge_context"] == ""


def test_investigation_preserves_structured_knowledge() -> None:
    knowledge = [
        {
            "source": "Database Runbook",
            "title": "Slow SQL Query",
            "content": "Inspect query execution plan.",
            "similarity": 0.88,
        },
    ]

    state = {
        "domain": "Database",
        "severity": "P3",
        "confidence": 0.75,
        "knowledge": knowledge,
    }

    result = investigation_node(state)

    assert result["knowledge"] == knowledge


def test_graph_investigation_consumes_rag_context(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        "deterministic",
    )

    def fake_retrieve_knowledge(
        query: str,
        limit: int = 5,
    ) -> tuple[list[dict], object]:
        from agent_service.models.agent import AgentTrace

        return (
            [
                {
                    "source": "VPN Runbook",
                    "title": "VPN Authentication Failure",
                    "content": "Verify VPN credentials.",
                    "similarity": 0.91,
                }
            ],
            AgentTrace(
                agent="KnowledgeAgent",
                step_name="knowledge",
                model="none",
                prompt_version="v1-test",
                summary="Retrieved test knowledge.",
            ),
        )

    monkeypatch.setattr(
        graph,
        "retrieve_knowledge",
        fake_retrieve_knowledge,
    )

    workflow = graph.build_graph()

    result = workflow.invoke(
        {
            "correlation_id": "corr-001",
            "ticket_id": "ticket-001",
            "title": "VPN authentication failure",
            "description": (
                "Users cannot connect to the corporate VPN."
            ),
            "reporter_email": "user@example.com",
        }
    )

    assert result["knowledge"]
    assert result["knowledge"][0]["source"] == "VPN Runbook"

    assert result["knowledge_context"]
    assert "VPN Runbook" in result["knowledge_context"]
    assert "Verify VPN credentials." in result["knowledge_context"]

    assert result["investigation"]["knowledge_count"] == 1
    assert (
        result["investigation"]["recommendation"]
        == "resolve"
    )

    investigation_traces = [
        item
        for item in result["trace"]
        if item["agent"] == "InvestigationAgent"
    ]

    assert len(investigation_traces) == 1
    assert (
        investigation_traces[0]["step"]
        == "deterministic_investigation"
    )


# ============================================================
# Phase 11 — Batch 4
# Investigation-aware DecisionAgent tests
# ============================================================


def test_decision_uses_investigation_resolve_recommendation() -> None:
    state = {
        "domain": "Network",
        "severity": "P1",
        "confidence": 0.60,
        "knowledge": [
            {
                "source": "VPN Runbook",
                "title": "VPN Authentication Failure",
                "content": "Verify VPN credentials.",
                "similarity": 0.91,
            }
        ],
        "investigation": {
            "summary": "Evidence supports resolution.",
            "evidence": [
                "Verify VPN credentials.",
            ],
            "recommendation": "resolve",
            "knowledge_count": 1,
        },
    }

    result = graph.decision_node(state)

    assert result["decision"] == "resolve"
    assert result["tool_proposal"] is None

    decision_traces = [
        item
        for item in result["trace"]
        if (
            item["agent"] == "DecisionAgent"
            and item["step"] == "decision"
        )
    ]

    assert len(decision_traces) == 1
    assert (
        "investigation_recommendation=resolve"
        in decision_traces[0]["summary"]
    )
    assert (
        "knowledge_count=1"
        in decision_traces[0]["summary"]
    )


def test_decision_escalates_on_investigate_further() -> None:
    state = {
        "domain": "Network",
        "severity": "P2",
        "confidence": 0.90,
        "knowledge": [
            {
                "source": "VPN Runbook",
                "title": "VPN Authentication Failure",
                "content": (
                    "Insufficient evidence for resolution."
                ),
                "similarity": 0.61,
            }
        ],
        "investigation": {
            "summary": "More investigation is required.",
            "evidence": [
                "Insufficient evidence for resolution.",
            ],
            "recommendation": "investigate_further",
            "knowledge_count": 1,
        },
    }

    result = graph.decision_node(state)

    assert result["decision"] == "escalate"


def test_decision_escalates_on_investigation_escalate() -> None:
    state = {
        "domain": "Identity",
        "severity": "P1",
        "confidence": 0.90,
        "knowledge": [
            {
                "source": "Identity Runbook",
                "title": "Account Lockout",
                "content": "Security review is required.",
                "similarity": 0.94,
            }
        ],
        "investigation": {
            "summary": "Security review is required.",
            "evidence": [
                "Security review is required.",
            ],
            "recommendation": "escalate",
            "knowledge_count": 1,
        },
    }

    result = graph.decision_node(state)

    assert result["decision"] == "escalate"


def test_decision_preserves_confidence_fallback_without_knowledge() -> None:
    state = {
        "domain": "Network",
        "severity": "P1",
        "confidence": 0.75,
        "knowledge": [],
        "investigation": {
            "summary": (
                "No knowledge evidence was retrieved "
                "for the incident."
            ),
            "evidence": [],
            "recommendation": "investigate_further",
            "knowledge_count": 0,
        },
    }

    result = graph.decision_node(state)

    assert result["decision"] == "resolve"


def test_decision_low_confidence_escalates_without_knowledge() -> None:
    state = {
        "domain": "Unknown",
        "severity": "P3",
        "confidence": 0.35,
        "knowledge": [],
        "investigation": {
            "summary": (
                "No knowledge evidence was retrieved "
                "for the incident."
            ),
            "evidence": [],
            "recommendation": "investigate_further",
            "knowledge_count": 0,
        },
    }

    result = graph.decision_node(state)

    assert result["decision"] == "escalate"


def test_decision_unknown_investigation_recommendation_falls_back_safely() -> None:
    state = {
        "domain": "Network",
        "severity": "P2",
        "confidence": 0.80,
        "knowledge": [
            {
                "source": "Network Runbook",
                "title": "Connectivity",
                "content": "Check connectivity.",
                "similarity": 0.80,
            }
        ],
        "investigation": {
            "summary": "Invalid recommendation test.",
            "evidence": [],
            "recommendation": "unknown_value",
            "knowledge_count": 1,
        },
    }

    result = graph.decision_node(state)

    assert result["decision"] == "resolve"