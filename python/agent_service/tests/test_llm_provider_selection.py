from agent_service.agents import triage


class FakeResponse:
    content = (
        '{"domain":"Network",'
        '"severity":"P2",'
        '"confidence":0.91}'
    )
    model = "test-provider-model"
    prompt_tokens = 12
    completion_tokens = 8
    latency_ms = 15.0


class FakeClient:
    def __init__(self):
        self.provider = None

    def chat(
        self,
        *,
        system_prompt,
        user_prompt,
        temperature,
        max_tokens,
        response_format,
    ):
        return FakeResponse()


def test_explicit_provider_overrides_environment(
    monkeypatch,
):
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        "deterministic",
    )

    captured = {}

    def fake_create_llm_client(provider):
        captured["provider"] = provider
        return FakeClient()

    monkeypatch.setattr(
        triage,
        "create_llm_client",
        fake_create_llm_client,
    )

    result, trace = triage.triage_ticket(
        title="VPN outage",
        description="Corporate VPN is down.",
        provider="openai",
    )

    assert captured["provider"] == "openai"
    assert result.domain == "Network"
    assert result.severity == "P2"
    assert result.confidence == 0.91
    assert trace.model == "test-provider-model"


def test_environment_provider_remains_default(
    monkeypatch,
):
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        "openrouter",
    )

    captured = {}

    def fake_create_llm_client(provider):
        captured["provider"] = provider
        return FakeClient()

    monkeypatch.setattr(
        triage,
        "create_llm_client",
        fake_create_llm_client,
    )

    result, _ = triage.triage_ticket(
        title="VPN outage",
        description="Corporate VPN is down.",
    )

    assert captured["provider"] == "openrouter"
    assert result.domain == "Network"


def test_explicit_deterministic_provider_still_works(
    monkeypatch,
):
    monkeypatch.setenv(
        "AGENT_LLM_PROVIDER",
        "openai",
    )

    result, trace = triage.triage_ticket(
        title="VPN outage",
        description="Corporate VPN is down.",
        provider="deterministic",
    )

    assert result.domain == "Network"
    assert result.severity == "P1"
    assert result.confidence == 0.75
    assert trace.model == "deterministic/bootstrap"