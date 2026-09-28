import json

import pytest

from agent_service.agents import tool_proposal


def tool_entry(name: str = "Directory.ResetPassword") -> dict:
    return {
        "name": name,
        "description": "Reset the reporter password.",
        "risk": "Sensitive",
        "requires_approval": True,
        "input_schema_json": (
            '{"type":"object","properties":'
            '{"userPrincipalName":{"type":"string"}},'
            '"required":["userPrincipalName"],'
            '"additionalProperties":false}'
        ),
    }


def test_deterministic_proposal_uses_only_available_matching_tool(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("AGENT_LLM_PROVIDER", "deterministic")

    proposal, trace = tool_proposal.propose_tool(
        title="Password reset requested",
        description="The reporter is locked out and forgot the password.",
        reporter_email="user@example.com",
        external_ref="INC-1",
        domain="Identity",
        severity="P2",
        investigation={"recommendation": "investigate_further"},
        knowledge_context="",
        allowed_tools=[tool_entry()],
    )

    assert proposal is not None
    assert proposal["tool_name"] == "Directory.ResetPassword"
    assert proposal["arguments"] == {"userPrincipalName": "user@example.com"}
    assert trace.agent == "ToolProposalAgent"
    assert trace.step_name == "tool_proposal"


def test_deterministic_proposal_returns_none_without_matching_allowlisted_tool(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("AGENT_LLM_PROVIDER", "deterministic")

    proposal, _ = tool_proposal.propose_tool(
        title="Password reset requested",
        description="The reporter is locked out.",
        reporter_email="user@example.com",
        external_ref="INC-1",
        domain="Identity",
        severity="P2",
        investigation={},
        knowledge_context="",
        allowed_tools=[tool_entry("Network.RunVpnDiagnostics")],
    )

    assert proposal is None


def test_tool_proposal_skips_when_investigation_already_resolved(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("AGENT_LLM_PROVIDER", "openai")

    proposal, trace = tool_proposal.propose_tool(
        title="Password reset requested",
        description="The reporter is locked out.",
        reporter_email="user@example.com",
        external_ref="INC-1",
        domain="Identity",
        severity="P2",
        investigation={"recommendation": "resolve"},
        knowledge_context="",
        allowed_tools=[tool_entry()],
    )

    assert proposal is None
    assert "recommended resolve" in trace.summary


class FakeResponse:
    content = json.dumps(
        {
            "tool_name": "Directory.ResetPassword",
            "arguments": {"userPrincipalName": "user@example.com"},
            "confidence": 0.91,
            "justification": "The user is locked out.",
        }
    )
    model = "fake-provider-model"
    prompt_tokens = 123
    completion_tokens = 23
    latency_ms = 12.0


class FakeClient:
    def __init__(self) -> None:
        self.user_prompt = ""

    def chat(self, *, system_prompt, user_prompt, **kwargs):
        self.user_prompt = user_prompt
        return FakeResponse()


def test_llm_proposal_is_structured_and_receives_server_tool_manifest(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("AGENT_LLM_PROVIDER", "openai")
    fake_client = FakeClient()
    monkeypatch.setattr(
        tool_proposal,
        "create_llm_client",
        lambda provider: fake_client,
    )

    proposal, trace = tool_proposal.propose_tool(
        title="Password reset requested",
        description="The reporter is locked out.",
        reporter_email="user@example.com",
        external_ref="INC-1",
        domain="Identity",
        severity="P2",
        investigation={"recommendation": "investigate_further"},
        knowledge_context="Identity runbook context",
        allowed_tools=[tool_entry()],
    )

    assert proposal is not None
    assert proposal["confidence"] == 0.91
    assert proposal["arguments"] == {"userPrincipalName": "user@example.com"}
    assert trace.model == "fake-provider-model"
    prompt = json.loads(fake_client.user_prompt)
    assert prompt["allowed_tools"][0]["risk"] == "Sensitive"
    assert "input_schema_json" in prompt["allowed_tools"][0]


def test_llm_proposal_rejects_tool_outside_allowlist(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("AGENT_LLM_PROVIDER", "openai")
    fake_client = FakeClient()
    monkeypatch.setattr(
        tool_proposal,
        "create_llm_client",
        lambda provider: fake_client,
    )
    FakeResponse.content = json.dumps(
        {
            "tool_name": "Unknown.DangerousTool",
            "arguments": {},
            "confidence": 0.99,
            "justification": "Use it.",
        }
    )

    with pytest.raises(RuntimeError, match="outside the allowlist"):
        tool_proposal.propose_tool(
            title="Unrecognized",
            description="Need help",
            reporter_email="user@example.com",
            external_ref="INC-1",
            domain="Unknown",
            severity="P3",
            investigation={},
            knowledge_context="",
            allowed_tools=[tool_entry()],
        )
