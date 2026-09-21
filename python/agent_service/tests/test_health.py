from fastapi.testclient import TestClient

from agent_service.main import app


client = TestClient(app)


def test_health():
    response = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {
        "status": "healthy"
    }


def test_agent_run_network_uses_langgraph():
    response = client.post(
        "/api/v1/agent/runs",
        json={
            "correlation_id": "00000000-0000-0000-0000-000000000001",
            "ticket_id": "00000000-0000-0000-0000-000000000002",
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
    assert body["triage"]["confidence"] == 0.85

    assert body["tool_proposal"] is None
    assert body["error"] is None

    assert len(body["trace"]) == 5

    assert body["trace"][0]["agent"] == "TriageAgent"
    assert body["trace"][0]["step"] == "triage"

    assert body["trace"][1]["agent"] == "KnowledgeAgent"
    assert body["trace"][1]["step"] == "knowledge"

    assert body["trace"][2]["agent"] == "InvestigationAgent"
    assert body["trace"][2]["step"] == "investigation"

    assert body["trace"][3]["agent"] == "DecisionAgent"
    assert body["trace"][3]["step"] == "decision"

    assert body["trace"][4]["step"] == "resolve"


def test_agent_run_unknown_escalates():
    response = client.post(
        "/api/v1/agent/runs",
        json={
            "correlation_id": "00000000-0000-0000-0000-000000000003",
            "ticket_id": "00000000-0000-0000-0000-000000000004",
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

    assert len(body["trace"]) == 5

    assert body["trace"][0]["agent"] == "TriageAgent"
    assert body["trace"][1]["agent"] == "KnowledgeAgent"
    assert body["trace"][2]["agent"] == "InvestigationAgent"
    assert body["trace"][3]["agent"] == "DecisionAgent"
    assert body["trace"][4]["step"] == "escalate"
def test_agent_run_resume_success():
    response = client.post(
        "/api/v1/agent/runs/resume",
        json={
            "correlation_id": (
                "00000000-0000-0000-000000000005"
            ),
            "ticket_id": (
                "00000000-0000-0000-000000000006"
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
                "00000000-0000-0000-000000000007"
            ),
            "ticket_id": (
                "00000000-0000-0000-000000000008"
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