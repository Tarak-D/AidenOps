from __future__ import annotations

import pytest

from agent_service.agents import investigation


def test_deterministic_investigation_uses_rag_context(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        "deterministic",
    )

    result, trace = investigation.investigate_ticket(
        title="VPN authentication failure",
        description="Users cannot connect to the corporate VPN.",
        domain="Network",
        severity="P1",
        confidence=0.75,
        knowledge_context=(
            "[Knowledge 1]\n"
            "Source: VPN Runbook\n"
            "Title: VPN Authentication Failure\n"
            "Similarity: 0.9100\n"
            "Content: Verify VPN credentials."
        ),
        knowledge_count=1,
    )

    assert result["knowledge_count"] == 1
    assert result["recommendation"] == "resolve"
    assert result["evidence"] == [
        "Source: VPN Runbook",
        "Title: VPN Authentication Failure",
        "Content: Verify VPN credentials.",
    ]

    assert trace.agent == "InvestigationAgent"
    assert trace.step_name == "deterministic_investigation"
    assert trace.model == "deterministic/bootstrap"


def test_deterministic_investigation_without_knowledge(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        "deterministic",
    )

    result, trace = investigation.investigate_ticket(
        title="Unknown incident",
        description="Something unexpected happened.",
        domain="Unknown",
        severity="P3",
        confidence=0.35,
        knowledge_context="",
        knowledge_count=0,
    )

    assert result["knowledge_count"] == 0
    assert result["recommendation"] == "investigate_further"
    assert result["evidence"] == []

    assert trace.agent == "InvestigationAgent"
    assert trace.step_name == "deterministic_investigation"


class FakeResponse:
    content = (
        '{"summary":"VPN credentials should be verified.",'
        '"evidence":["VPN Runbook confirms credential verification."],'
        '"recommendation":"resolve",'
        '"knowledge_count":1}'
    )
    model = "fake-investigation-model"
    prompt_tokens = 40
    completion_tokens = 25
    latency_ms = 14.5


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
        assert "VPN authentication failure" in user_prompt
        assert "Verify VPN credentials." in user_prompt
        assert temperature == 0.0
        assert max_tokens == 768

        if self.provider == "openrouter":
            assert response_format == {
                "type": "json_schema",
                "json_schema": {
                    "name": "aidenops_investigation",
                    "strict": True,
                    "schema": {
                        "type": "object",
                        "properties": {
                            "summary": {
                                "type": "string",
                                "description": (
                                    "Concise investigation summary."
                                ),
                            },
                            "evidence": {
                                "type": "array",
                                "items": {
                                    "type": "string",
                                },
                                "description": (
                                    "Evidence grounded in the "
                                    "incident or retrieved knowledge."
                                ),
                            },
                            "recommendation": {
                                "type": "string",
                                "enum": [
                                    "resolve",
                                    "escalate",
                                    "investigate_further",
                                ],
                                "description": (
                                    "Investigation recommendation."
                                ),
                            },
                            "knowledge_count": {
                                "type": "integer",
                                "minimum": 0,
                                "description": (
                                    "Number of supplied knowledge "
                                    "results."
                                ),
                            },
                        },
                        "required": [
                            "summary",
                            "evidence",
                            "recommendation",
                            "knowledge_count",
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
def test_provider_investigation(
    monkeypatch: pytest.MonkeyPatch,
    provider: str,
) -> None:
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        provider,
    )

    monkeypatch.setattr(
        investigation,
        "create_llm_client",
        lambda provider: FakeClient(provider),
    )

    result, trace = investigation.investigate_ticket(
        title="VPN authentication failure",
        description="Users cannot connect to the corporate VPN.",
        domain="Network",
        severity="P1",
        confidence=0.75,
        knowledge_context=(
            "[Knowledge 1]\n"
            "Source: VPN Runbook\n"
            "Title: VPN Authentication Failure\n"
            "Similarity: 0.9100\n"
            "Content: Verify VPN credentials."
        ),
        knowledge_count=1,
    )

    assert result["summary"] == (
        "VPN credentials should be verified."
    )
    assert result["evidence"] == [
        "VPN Runbook confirms credential verification."
    ]
    assert result["recommendation"] == "resolve"
    assert result["knowledge_count"] == 1

    assert trace.agent == "InvestigationAgent"
    assert trace.step_name == "llm_investigation"
    assert trace.model == "fake-investigation-model"
    assert trace.prompt_tokens == 40
    assert trace.completion_tokens == 25
    assert trace.latency_ms == 14.5


def test_provider_investigation_rejects_mismatched_knowledge_count(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        "openrouter",
    )

    class BadClient(FakeClient):
        def chat(
            self,
            *,
            system_prompt: str,
            user_prompt: str,
            temperature: float,
            max_tokens: int,
            response_format: dict,
        ) -> FakeResponse:
            return type(
                "BadResponse",
                (),
                {
                    "content": (
                        '{"summary":"Test",'
                        '"evidence":["Evidence"],'
                        '"recommendation":"resolve",'
                        '"knowledge_count":2}'
                    ),
                    "model": "fake",
                    "prompt_tokens": 1,
                    "completion_tokens": 1,
                    "latency_ms": 1.0,
                },
            )()

    monkeypatch.setattr(
        investigation,
        "create_llm_client",
        lambda provider: BadClient(provider),
    )

    with pytest.raises(RuntimeError, match="LLM investigation failed"):
        investigation.investigate_ticket(
            title="VPN authentication failure",
            description="Users cannot connect.",
            domain="Network",
            severity="P1",
            confidence=0.75,
            knowledge_context="VPN Runbook evidence.",
            knowledge_count=1,
        )