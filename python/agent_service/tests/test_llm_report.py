import pytest

from agent_service.evaluation.llm_evaluator import (
    LLMEvaluationPrediction,
)
from agent_service.evaluation.llm_metrics import (
    LLMEvaluationMetrics,
)
from agent_service.evaluation.llm_report import (
    build_llm_evaluation_report,
)
from agent_service.models.agent import StepTrace


def make_prediction(
    *,
    case_id: str,
    provider: str = "nvidia",
    model: str = "test-model",
    prompt_version: str = "v1-nvidia-triage",
    domain_correct: bool = True,
    severity_correct: bool = True,
) -> LLMEvaluationPrediction:
    trace = StepTrace(
        agent="triage",
        step_name="llm_triage",
        model=model,
        prompt_version=prompt_version,
        prompt_tokens=40,
        completion_tokens=15,
        latency_ms=25.0,
        summary="test",
    )

    return LLMEvaluationPrediction(
        case_id=case_id,
        provider=provider,
        domain="Network",
        severity="P1",
        confidence=0.9,
        domain_correct=domain_correct,
        severity_correct=severity_correct,
        triage_correct=(
            domain_correct and severity_correct
        ),
        model=model,
        prompt_version=prompt_version,
        prompt_tokens=40,
        completion_tokens=15,
        latency_ms=25.0,
        trace=trace,
    )


def test_build_report_contains_dataset_model_and_metrics():
    predictions = [
        make_prediction(case_id="case-1"),
        make_prediction(
            case_id="case-2",
            domain_correct=False,
        ),
    ]

    report = build_llm_evaluation_report(
        dataset_id="phase15-test",
        dataset_name="phase-15-llm-evaluation",
        dataset_version="1.0",
        predictions=predictions,
    )

    assert report.model_type == "llm"
    assert report.dataset_id == "phase15-test"
    assert report.dataset_name == "phase-15-llm-evaluation"
    assert report.dataset_version == "1.0"

    assert report.provider == "nvidia"
    assert report.model == "test-model"
    assert report.prompt_version == "v1-nvidia-triage"

    assert report.sample_count == 2
    assert report.metrics.domain_accuracy == 0.5
    assert report.metrics.severity_accuracy == 1.0
    assert report.metrics.triage_accuracy == 0.5


def test_report_to_dict_has_stable_contract():
    prediction = make_prediction(
        case_id="case-1"
    )

    report = build_llm_evaluation_report(
        dataset_id="dataset-1",
        dataset_name="golden",
        dataset_version="2.0",
        predictions=[prediction],
    )

    payload = report.to_dict()

    assert payload["model_type"] == "llm"

    assert payload["dataset"] == {
        "id": "dataset-1",
        "name": "golden",
        "version": "2.0",
    }

    assert payload["model"] == {
        "provider": "nvidia",
        "name": "test-model",
        "prompt_version": "v1-nvidia-triage",
    }

    assert payload["sample_count"] == 1

    assert payload["metrics"]["domain_accuracy"] == 1.0
    assert payload["metrics"]["severity_accuracy"] == 1.0
    assert payload["metrics"]["triage_accuracy"] == 1.0
    assert payload["metrics"]["total_tokens"] == 55

    assert len(payload["predictions"]) == 1
    assert payload["predictions"][0]["case_id"] == "case-1"


def test_report_rejects_mixed_models():
    predictions = [
        make_prediction(
            case_id="case-1",
            model="model-a",
        ),
        make_prediction(
            case_id="case-2",
            model="model-b",
        ),
    ]

    with pytest.raises(
        ValueError,
        match="same model",
    ):
        build_llm_evaluation_report(
            dataset_id="dataset-1",
            dataset_name="golden",
            dataset_version="1.0",
            predictions=predictions,
        )


def test_report_rejects_mixed_prompt_versions():
    predictions = [
        make_prediction(
            case_id="case-1",
            prompt_version="v1",
        ),
        make_prediction(
            case_id="case-2",
            prompt_version="v2",
        ),
    ]

    with pytest.raises(
        ValueError,
        match="same prompt version",
    ):
        build_llm_evaluation_report(
            dataset_id="dataset-1",
            dataset_name="golden",
            dataset_version="1.0",
            predictions=predictions,
        )