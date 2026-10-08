from agent_service.agents import triage
from agent_service.evaluation.golden_dataset import (
    GoldenDataset,
    GoldenDatasetCase,
)
from agent_service.evaluation.llm_evaluator import (
    LLMEvaluator,
)
from agent_service.evaluation.llm_golden_dataset import (
    LLMGoldenDatasetEvaluator,
)


class FakeResponse:
    content = (
        '{"domain":"Network",'
        '"severity":"P1",'
        '"confidence":0.93}'
    )
    model = "test-llm-model"
    prompt_tokens = 35
    completion_tokens = 12
    latency_ms = 18.0


class FakeClient:
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


def test_llm_golden_dataset_evaluator_uses_dataset_cases(
    monkeypatch,
):
    def fake_create_llm_client(provider):
        assert provider == "nvidia"
        return FakeClient()

    monkeypatch.setattr(
        triage,
        "create_llm_client",
        fake_create_llm_client,
    )

    dataset = GoldenDataset(
        dataset_id="phase15-llm-test",
        name="phase-15-llm-evaluation",
        version="1.0",
        cases=(
            GoldenDatasetCase(
                case_id="vpn-1",
                title="VPN outage",
                description="Corporate VPN is down.",
                expected_domain="Network",
                expected_severity="P1",
            ),
        ),
    )

    evaluator = LLMGoldenDatasetEvaluator(
        LLMEvaluator("nvidia")
    )

    predictions = evaluator.evaluate(dataset)

    assert len(predictions) == 1

    prediction = predictions[0]

    assert prediction.case_id == "vpn-1"
    assert prediction.provider == "nvidia"
    assert prediction.domain == "Network"
    assert prediction.severity == "P1"
    assert prediction.domain_correct is True
    assert prediction.severity_correct is True
    assert prediction.triage_correct is True


def test_llm_golden_dataset_evaluator_requires_severity():
    dataset = GoldenDataset(
        dataset_id="phase15-missing-severity",
        name="phase-15-llm-evaluation",
        version="1.0",
        cases=(
            GoldenDatasetCase(
                case_id="missing-severity",
                title="VPN outage",
                description="Corporate VPN is down.",
                expected_domain="Network",
            ),
        ),
    )

    evaluator = LLMGoldenDatasetEvaluator(
        LLMEvaluator("nvidia")
    )

    try:
        evaluator.evaluate(dataset)
    except ValueError as exc:
        assert (
            "Expected severity is required"
            in str(exc)
        )
    else:
        raise AssertionError(
            "Expected missing severity to be rejected."
        )


def test_existing_classical_golden_case_can_omit_severity():
    dataset = GoldenDataset(
        dataset_id="phase15-classical-compatible",
        name="phase-15-classical-evaluation",
        version="1.0",
        cases=(
            GoldenDatasetCase(
                case_id="network-1",
                title="Network outage",
                description="Office network is down.",
                expected_domain="Network",
            ),
        ),
    )

    dataset.validate()

    assert dataset.cases[0].expected_severity is None