from agent_service.agents import triage
from agent_service.evaluation.llm_evaluator import (
    LLMEvaluationExample,
    LLMEvaluator,
)


class FakeResponse:
    def __init__(
        self,
        *,
        domain: str,
        severity: str,
        confidence: float,
        model: str = "test-llm-model",
        prompt_tokens: int = 40,
        completion_tokens: int = 15,
        latency_ms: float = 25.0,
    ):
        self.content = (
            "{"
            f'"domain":"{domain}",'
            f'"severity":"{severity}",'
            f'"confidence":{confidence}'
            "}"
        )
        self.model = model
        self.prompt_tokens = prompt_tokens
        self.completion_tokens = completion_tokens
        self.latency_ms = latency_ms


class FakeClient:
    def __init__(self, response: FakeResponse):
        self.response = response

    def chat(
        self,
        *,
        system_prompt,
        user_prompt,
        temperature,
        max_tokens,
        response_format,
    ):
        return self.response


def test_llm_evaluator_runs_multiple_cases(monkeypatch):
    responses = [
        FakeResponse(
            domain="Network",
            severity="P1",
            confidence=0.94,
            latency_ms=20.0,
        ),
        FakeResponse(
            domain="Identity",
            severity="P3",
            confidence=0.88,
            latency_ms=30.0,
        ),
        FakeResponse(
            domain="Database",
            severity="P2",
            confidence=0.91,
            latency_ms=40.0,
        ),
    ]

    created_clients = []

    def fake_create_llm_client(provider):
        assert provider == "nvidia"

        client = FakeClient(
            responses[len(created_clients)]
        )

        created_clients.append(client)

        return client

    monkeypatch.setattr(
        triage,
        "create_llm_client",
        fake_create_llm_client,
    )

    evaluator = LLMEvaluator("nvidia")

    examples = [
        LLMEvaluationExample(
            case_id="network-1",
            title="VPN outage",
            description="Corporate VPN is down.",
            expected_domain="Network",
            expected_severity="P1",
        ),
        LLMEvaluationExample(
            case_id="identity-1",
            title="Password issue",
            description="User password needs to be reset.",
            expected_domain="Identity",
            expected_severity="P3",
        ),
        LLMEvaluationExample(
            case_id="database-1",
            title="Database problem",
            description="Production database is slow.",
            expected_domain="Database",
            expected_severity="P2",
        ),
    ]

    predictions = evaluator.evaluate(examples)

    assert len(predictions) == 3
    assert len(created_clients) == 3

    assert all(
        prediction.provider == "nvidia"
        for prediction in predictions
    )

    assert all(
        prediction.triage_correct
        for prediction in predictions
    )

    assert predictions[0].domain == "Network"
    assert predictions[1].domain == "Identity"
    assert predictions[2].domain == "Database"

    assert predictions[0].prompt_tokens == 40
    assert predictions[0].completion_tokens == 15
    assert predictions[0].latency_ms == 20.0


def test_llm_evaluator_records_incorrect_prediction(
    monkeypatch,
):
    def fake_create_llm_client(provider):
        return FakeClient(
            FakeResponse(
                domain="Infrastructure",
                severity="P1",
                confidence=0.82,
            )
        )

    monkeypatch.setattr(
        triage,
        "create_llm_client",
        fake_create_llm_client,
    )

    evaluator = LLMEvaluator("openrouter")

    example = LLMEvaluationExample(
        case_id="database-incorrect",
        title="Database unavailable",
        description="Production database is unavailable.",
        expected_domain="Database",
        expected_severity="P1",
    )

    prediction = evaluator.evaluate_case(example)

    assert prediction.domain == "Infrastructure"
    assert prediction.severity == "P1"

    assert prediction.domain_correct is False
    assert prediction.severity_correct is True
    assert prediction.triage_correct is False


def test_llm_evaluator_rejects_deterministic_provider():
    try:
        LLMEvaluator("deterministic")
    except ValueError as exc:
        assert "LLM provider" in str(exc)
    else:
        raise AssertionError(
            "Expected deterministic provider to be rejected."
        )