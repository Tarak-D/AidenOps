import json

from fastapi.testclient import TestClient

from agent_service.main import app
from agent_service.models.agent import AgentTrace
from agent_service import graph


client = TestClient(app)


def fake_retrieve_knowledge(
    query: str,
    limit: int = 5,
) -> tuple[list[dict], AgentTrace]:
    return [], AgentTrace(
        agent="KnowledgeAgent",
        step_name="knowledge",
        model="none",
        prompt_version="v1-test",
        summary="Test knowledge retrieval boundary.",
    )


def test_health():
    response = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {
        "status": "healthy"
    }


def test_agent_run_network_uses_langgraph(
    monkeypatch,
):
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        "deterministic",
    )

    monkeypatch.setattr(
        graph,
        "retrieve_knowledge",
        fake_retrieve_knowledge,
    )

    response = client.post(
        "/api/v1/agent/runs",
        json={
            "correlation_id": (
                "00000000-0000-0000-0000-000000000001"
            ),
            "ticket_id": (
                "00000000-0000-0000-0000-000000000002"
            ),
            "title": "VPN failure",
            "description": "The corporate VPN is down.",
            "reporter_email": "test@example.com",
            "domain": "Unknown",
            "severity": "P3",
        },
    )

    assert response.status_code == 200

    body = response.json()

    assert body["correlation_id"] == (
        "00000000-0000-0000-0000-000000000001"
    )

    assert body["outcome"] == "Resolved"

    assert body["triage"]["domain"] == "Network"
    assert body["triage"]["severity"] == "P1"
    assert body["triage"]["confidence"] == 0.75

    assert body["tool_proposal"] is None
    assert body["error"] is None

    assert len(body["trace"]) == 6

    assert body["trace"][0]["agent"] == "TriageAgent"
    assert body["trace"][0]["step"] == "triage"

    assert body["trace"][1]["agent"] == "KnowledgeAgent"
    assert body["trace"][1]["step"] == "knowledge"

    assert body["trace"][2]["agent"] == "InvestigationAgent"
    assert body["trace"][2]["step"] == "investigation"

    assert body["trace"][3]["agent"] == "ToolProposalAgent"
    assert body["trace"][3]["step"] == "tool_proposal"

    assert body["trace"][4]["agent"] == "DecisionAgent"
    assert body["trace"][4]["step"] == "decision"
    assert body["trace"][5]["step"] == "resolve"


def test_agent_run_unknown_escalates(
    monkeypatch,
):
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        "deterministic",
    )

    monkeypatch.setattr(
        graph,
        "retrieve_knowledge",
        fake_retrieve_knowledge,
    )

    response = client.post(
        "/api/v1/agent/runs",
        json={
            "correlation_id": (
                "00000000-0000-0000-0000-000000000003"
            ),
            "ticket_id": (
                "00000000-0000-0000-0000-000000000004"
            ),
            "title": "Something strange",
            "description": "The issue is not recognized.",
        },
    )

    assert response.status_code == 200

    body = response.json()

    assert body["outcome"] == "Escalated"
    assert body["triage"]["domain"] == "Unknown"
    assert body["triage"]["severity"] == "P3"
    assert body["triage"]["confidence"] == 0.35

    assert body["tool_proposal"] is None
    assert body["error"] is None

    assert len(body["trace"]) == 6

    assert body["trace"][0]["agent"] == "TriageAgent"
    assert body["trace"][1]["agent"] == "KnowledgeAgent"
    assert body["trace"][2]["agent"] == "InvestigationAgent"
    assert body["trace"][3]["agent"] == "ToolProposalAgent"
    assert body["trace"][4]["agent"] == "DecisionAgent"
    assert body["trace"][5]["step"] == "escalate"


def test_agent_run_returns_allowlisted_proposal_without_execution(
    monkeypatch,
):
    monkeypatch.setenv("AGENT_LLM_PROVIDER", "deterministic")
    monkeypatch.setattr(graph, "retrieve_knowledge", fake_retrieve_knowledge)

    response = client.post(
        "/api/v1/agent/runs",
        json={
            "correlation_id": "00000000-0000-0000-0000-000000000009",
            "ticket_id": "00000000-0000-0000-0000-000000000010",
            "external_ref": "INC-10",
            "title": "VPN is unavailable",
            "description": "The reporter cannot connect.",
            "reporter_email": "user@example.com",
            "allowed_tools": [
                {
                    "name": "Network.RunVpnDiagnostics",
                    "description": "Runs read-only VPN diagnostics.",
                    "risk": "Safe",
                    "requires_approval": False,
                    "input_schema_json": (
                        '{"type":"object","properties":'
                        '{"userOrDeviceId":{"type":"string"}},'
                        '"required":["userOrDeviceId"],'
                        '"additionalProperties":false}'
                    ),
                }
            ],
        },
    )

    assert response.status_code == 200
    body = response.json()
    assert body["outcome"] == "ProposalCreated"
    assert body["tool_proposal"]["tool_name"] == "Network.RunVpnDiagnostics"
    assert json.loads(body["tool_proposal"]["arguments_json"]) == {
        "userOrDeviceId": "user@example.com"
    }
    assert any(item["agent"] == "ToolProposalAgent" for item in body["trace"])


def test_agent_run_skips_proposal_below_server_threshold(
    monkeypatch,
):
    monkeypatch.setenv("AGENT_LLM_PROVIDER", "deterministic")
    monkeypatch.setattr(graph, "retrieve_knowledge", fake_retrieve_knowledge)

    response = client.post(
        "/api/v1/agent/runs",
        json={
            "correlation_id": "00000000-0000-0000-0000-000000000011",
            "ticket_id": "00000000-0000-0000-0000-000000000012",
            "title": "VPN is unavailable",
            "description": "The reporter cannot connect.",
            "reporter_email": "user@example.com",
            "triage_confidence_threshold": 0.8,
            "allowed_tools": [
                {
                    "name": "Network.RunVpnDiagnostics",
                    "description": "Runs read-only VPN diagnostics.",
                    "risk": "Safe",
                    "requires_approval": False,
                    "input_schema_json": "{}",
                }
            ],
        },
    )

    assert response.status_code == 200
    body = response.json()
    assert body["tool_proposal"] is None
    assert body["trace"][3]["model"] == "deterministic/policy"


def test_agent_run_resume_success():
    response = client.post(
        "/api/v1/agent/runs/resume",
        json={
            "correlation_id": (
                "00000000-0000-0000-0000-000000000005"
            ),
            "ticket_id": (
                "00000000-0000-0000-0000-000000000006"
            ),
            "approval_granted": True,
            "approval_decided_by": "test-user",
            "tool_result_json": "{\"success\":true}",
            "tool_execution_succeeded": True,
        },
    )

    assert response.status_code == 200

    body = response.json()

    assert body["outcome"] == "Resolved"
    assert body["trace"][0]["agent"] == "ValidationAgent"
    assert body["trace"][0]["step"] == "verify"


def test_agent_run_resume_failure():
    response = client.post(
        "/api/v1/agent/runs/resume",
        json={
            "correlation_id": (
                "00000000-0000-0000-0000-000000000007"
            ),
            "ticket_id": (
                "00000000-0000-0000-0000-000000000008"
            ),
            "approval_granted": False,
            "approval_decided_by": "test-user",
            "tool_result_json": "{\"success\":false}",
            "tool_execution_succeeded": False,
        },
    )

    assert response.status_code == 200

    body = response.json()

    assert body["outcome"] == "Escalated"
    assert body["trace"][0]["agent"] == "ValidationAgent"
    assert body["trace"][0]["step"] == "verify"
