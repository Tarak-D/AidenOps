import pytest

from agent_service.agents.triage import (
    triage_ticket,
)
from agent_service.llm import (
    LlmResponse,
)


def test_triage_network_incident(
    monkeypatch,
) -> None:
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        "deterministic",
    )

    result, trace = triage_ticket(
        "VPN failure",
        "The corporate VPN is down.",
    )

    assert result.domain == "Network"
    assert result.severity == "P1"
    assert result.confidence == 0.85

    assert trace.agent == "TriageAgent"
    assert trace.step_name == "triage"


def test_triage_unknown_incident(
    monkeypatch,
) -> None:
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        "deterministic",
    )

    result, trace = triage_ticket(
        "Something strange",
        "The issue is not recognized.",
    )

    assert result.domain == "Unknown"
    assert result.severity == "P3"
    assert result.confidence == 0.35

    assert trace.agent == "TriageAgent"


def test_triage_defaults_to_deterministic(
    monkeypatch,
) -> None:
    monkeypatch.delenv(
        "AGENT_LLM_PROVIDER",
        raising=False,
    )

    result, trace = triage_ticket(
        "VPN keeps dropping",
        "The corporate VPN disconnects repeatedly.",
    )

    assert result.domain == "Network"
    assert result.severity == "P3"
    assert result.confidence == 0.85

    assert trace.agent == "TriageAgent"
    assert trace.model == (
        "deterministic/bootstrap"
    )


def test_triage_rejects_unknown_provider(
    monkeypatch,
) -> None:
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        "unknown-provider",
    )

    with pytest.raises(
        RuntimeError,
        match="LLM triage failed",
    ):
        triage_ticket(
            "VPN issue",
            "The VPN is unavailable.",
        )


@pytest.mark.parametrize(
    "provider",
    [
        "nvidia",
        "openrouter",
        "openai",
        "anthropic",
        "google",
        "azure_openai",
    ],
)
def test_triage_uses_configured_provider(
    monkeypatch,
    provider,
) -> None:
    class FakeLlmClient:
        def chat(
            self,
            *,
            system_prompt,
            user_prompt,
            temperature,
            max_tokens,
            response_format,
        ):
            assert (
                "TriageAgent"
                in system_prompt
            )
            assert (
                "VPN issue"
                in user_prompt
            )
            assert temperature == 0.0
            assert max_tokens == 512
            assert response_format == {
                "type": "json_object",
            }

            return LlmResponse(
                content=(
                    '{"domain":"Network",'
                    '"severity":"P2",'
                    '"confidence":0.94}'
                ),
                model=f"{provider}-test-model",
                prompt_tokens=100,
                completion_tokens=25,
                latency_ms=12.5,
            )

    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        provider,
    )

    monkeypatch.setattr(
        "agent_service.agents.triage.create_llm_client",
        lambda provider: FakeLlmClient(),
    )

    result, trace = triage_ticket(
        "VPN issue",
        "Users cannot connect to the corporate VPN.",
    )

    assert result.domain == "Network"
    assert result.severity == "P2"
    assert result.confidence == 0.94

    assert trace.agent == "TriageAgent"
    assert trace.model == (
        f"{provider}-test-model"
    )
    assert trace.prompt_version == (
        f"v1-{provider}-triage"
    )
    assert trace.prompt_tokens == 100
    assert trace.completion_tokens == 25
    assert trace.latency_ms == 12.5