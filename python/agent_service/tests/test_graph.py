import os

import pytest

from agent_service.graph import build_graph


def test_graph_resolves_known_incident(monkeypatch):
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        "deterministic",
    )

    graph = build_graph()

    result = graph.invoke(
        {
            "correlation_id": (
                "00000000-0000-0000-0000-000000000001"
            ),
            "ticket_id": (
                "00000000-0000-0000-0000-000000000002"
            ),
            "title": "VPN failure",
            "description": "The corporate VPN is down.",
        }
    )

    assert result["domain"] == "Network"
    assert result["severity"] == "P1"
    assert result["confidence"] == 0.85

    assert result["knowledge"] == []

    assert result["investigation"]["domain"] == "Network"
    assert result["investigation"]["knowledge_count"] == 0

    assert result["decision"] == "resolve"

    assert len(result["trace"]) == 5

    assert result["trace"][0]["agent"] == "TriageAgent"
    assert result["trace"][1]["agent"] == "KnowledgeAgent"
    assert result["trace"][2]["agent"] == "InvestigationAgent"
    assert result["trace"][3]["agent"] == "DecisionAgent"
    assert result["trace"][4]["step"] == "resolve"


def test_graph_escalates_unknown_incident(monkeypatch):
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        "deterministic",
    )

    graph = build_graph()

    result = graph.invoke(
        {
            "correlation_id": (
                "00000000-0000-0000-0000-000000000003"
            ),
            "ticket_id": (
                "00000000-0000-0000-0000-000000000004"
            ),
            "title": "Something strange",
            "description": "The issue is not recognized.",
        }
    )

    assert result["domain"] == "Unknown"
    assert result["severity"] == "P3"
    assert result["confidence"] == 0.35

    assert result["decision"] == "escalate"

    assert len(result["trace"]) == 5

    assert result["trace"][0]["agent"] == "TriageAgent"
    assert result["trace"][1]["agent"] == "KnowledgeAgent"
    assert result["trace"][2]["agent"] == "InvestigationAgent"
    assert result["trace"][3]["agent"] == "DecisionAgent"
    assert result["trace"][4]["step"] == "escalate"


def test_graph_with_openrouter():
    if os.getenv("AGENT_LLM_PROVIDER", "").strip().lower() != "openrouter":
        pytest.skip(
            "Set AGENT_LLM_PROVIDER=openrouter to run the live OpenRouter test."
        )

    if not os.getenv("OPENROUTER_API_KEY", "").strip():
        pytest.skip(
            "Set OPENROUTER_API_KEY to run the live OpenRouter test."
        )

    if not os.getenv("AGENT_LLM_MODEL", "").strip():
        pytest.skip(
            "Set AGENT_LLM_MODEL to run the live OpenRouter test."
        )

    graph = build_graph()

    result = graph.invoke(
        {
            "correlation_id": (
                "00000000-0000-0000-0000-000000000005"
            ),
            "ticket_id": (
                "00000000-0000-0000-0000-000000000006"
            ),
            "title": "VPN outage in production",
            "description": (
                "Users cannot connect to the corporate VPN "
                "and are unable to work."
            ),
        }
    )

    assert result["domain"] == "Network"
    assert result["severity"] == "P1"
    assert result["confidence"] >= 0.50

    assert result["knowledge"] == []

    assert result["investigation"]["domain"] == "Network"
    assert result["investigation"]["severity"] == "P1"

    assert result["decision"] == "resolve"

    assert len(result["trace"]) == 5

    assert result["trace"][0]["agent"] == "TriageAgent"
    assert result["trace"][0]["model"] == os.environ["AGENT_LLM_MODEL"]
    assert result["trace"][0]["prompt_tokens"] > 0
    assert result["trace"][0]["completion_tokens"] > 0
    assert result["trace"][0]["latency_ms"] > 0

    assert result["trace"][1]["agent"] == "KnowledgeAgent"
    assert result["trace"][2]["agent"] == "InvestigationAgent"
    assert result["trace"][3]["agent"] == "DecisionAgent"
    assert result["trace"][4]["step"] == "resolve"