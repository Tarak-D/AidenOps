from agent_service.agents.triage import triage_ticket


def test_triage_network_incident():
    result, trace = triage_ticket(
        "VPN failure",
        "The corporate VPN is down.",
    )

    assert result.domain == "Network"
    assert result.severity == "P1"
    assert result.confidence == 0.85

    assert trace.agent == "TriageAgent"
    assert trace.step_name == "triage"


def test_triage_unknown_incident():
    result, trace = triage_ticket(
        "Something strange",
        "The issue is not recognized.",
    )

    assert result.domain == "Unknown"
    assert result.severity == "P3"
    assert result.confidence == 0.35
    assert trace.agent == "TriageAgent"