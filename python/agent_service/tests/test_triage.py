from __future__ import annotations
import os
import pytest

from agent_service.agents.triage import triage_ticket

def test_deterministic_triage_network_p1(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("AGENT_LLM_PROVIDER", "deterministic")

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
    monkeypatch.setenv("AGENT_LLM_PROVIDER", "deterministic")

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
    monkeypatch.setenv("AGENT_LLM_PROVIDER", "deterministic")

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
    def __init__(self, provider: str) -> None:
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
                                "description": "The incident domain.",
                            },
                            "severity": {
                                "type": "string",
                                "enum": ["P1", "P2", "P3"],
                                "description": "The incident severity.",
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
            assert response_format == {"type": "json_object"}

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
    monkeypatch.setenv("AGENT_LLM_PROVIDER", provider)

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


def test_provider_triage_with_title_and_description(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("AGENT_LLM_PROVIDER", "openrouter")

    import agent_service.agents.triage as triage_module

    monkeypatch.setattr(
        triage_module,
        "create_llm_client",
        lambda provider: FakeClient(provider),
    )

    result, trace = triage_ticket(
        title="VPN failure",
        description="The corporate VPN is down.",
    )

    assert result.domain == "Network"
    assert result.severity == "P2"
    assert result.confidence == 0.91

    assert trace.agent == "triage"
    assert trace.step_name == "llm_triage"


def test_deterministic_triage_with_title_and_description(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("AGENT_LLM_PROVIDER", "deterministic")

    result, trace = triage_ticket(
        title="VPN failure",
        description="The corporate VPN is down.",
    )

    assert result.domain == "Network"
    assert result.severity == "P1"
    assert result.confidence == 0.75

    assert trace.agent == "triage"
    assert trace.step_name == "deterministic_triage"


def test_provider_is_read_from_environment(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("AGENT_LLM_PROVIDER", "deterministic")

    assert os.getenv("AGENT_LLM_PROVIDER") == "deterministic"


@pytest.mark.parametrize(
    "content",
    [
        # Missing confidence.
        '{"domain":"Network","severity":"P2"}',

        # Missing severity.
        '{"domain":"Network","confidence":0.9}',

        # Missing domain.
        '{"severity":"P2","confidence":0.9}',

        # Confidence above the allowed range.
        '{"domain":"Network","severity":"P2","confidence":1.2}',

        # Confidence below the allowed range.
        '{"domain":"Network","severity":"P2","confidence":-0.1}',

        # Unexpected field.
        (
            '{"domain":"Network","severity":"P2",'
            '"confidence":0.9,"extra":"value"}'
        ),
    ],
)
def test_provider_triage_rejects_invalid_model_output(
    monkeypatch: pytest.MonkeyPatch,
    content: str,
) -> None:
    monkeypatch.setenv("AGENT_LLM_PROVIDER", "openai")

    import agent_service.agents.triage as triage_module

    class InvalidResponse(FakeResponse):
        def __init__(self) -> None:
            super().__init__()
            self.content = content

    class InvalidClient(FakeClient):
        def chat(
            self,
            *,
            system_prompt: str,
            user_prompt: str,
            temperature: float,
            max_tokens: int,
            response_format: dict,
        ) -> FakeResponse:
            return InvalidResponse()

    monkeypatch.setattr(
        triage_module,
        "create_llm_client",
        lambda provider: InvalidClient(provider),
    )

    with pytest.raises(RuntimeError, match="LLM triage failed"):
        triage_ticket("VPN outage")


def test_provider_triage_suppresses_provider_exception_details(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("AGENT_LLM_PROVIDER", "openai")

    import agent_service.agents.triage as triage_module

    class FailingClient:
        def chat(self, **kwargs):
            raise RuntimeError("request failed with secret-token-value")

    monkeypatch.setattr(
        triage_module,
        "create_llm_client",
        lambda provider: FailingClient(),
    )

    with pytest.raises(RuntimeError, match="LLM triage failed") as error:
        triage_ticket("VPN outage")

    assert error.value.__cause__ is None
    assert "secret-token-value" not in str(error.value)
