import pytest

from agent_service.evaluation.llm_evaluator import (
    LLMEvaluationPrediction,
)
from agent_service.evaluation.llm_metrics import (
    LLMEvaluationMetrics,
)
from agent_service.models.agent import StepTrace


def make_prediction(
    *,
    provider: str = "nvidia",
    domain_correct: bool = True,
    severity_correct: bool = True,
    confidence: float = 0.9,
    latency_ms: float = 100.0,
    prompt_tokens: int = 50,
    completion_tokens: int = 20,
) -> LLMEvaluationPrediction:
    trace = StepTrace(
        agent="triage",
        step_name="llm_triage",
        model="test-model",
        prompt_version="v1-test-triage",
        prompt_tokens=prompt_tokens,
        completion_tokens=completion_tokens,
        latency_ms=latency_ms,
        summary="test",
    )

    return LLMEvaluationPrediction(
        case_id="case-1",
        provider=provider,
        domain="Network",
        severity="P2",
        confidence=confidence,
        domain_correct=domain_correct,
        severity_correct=severity_correct,
        triage_correct=(
            domain_correct and severity_correct
        ),
        model=trace.model,
        prompt_version=trace.prompt_version,
        prompt_tokens=prompt_tokens,
        completion_tokens=completion_tokens,
        latency_ms=latency_ms,
        trace=trace,
    )


def test_metrics_calculate_accuracy_and_tokens():
    predictions = [
        make_prediction(),
        make_prediction(
            domain_correct=False,
            severity_correct=True,
            confidence=0.7,
            latency_ms=200.0,
            prompt_tokens=60,
            completion_tokens=30,
        ),
    ]

    metrics = LLMEvaluationMetrics.calculate(
        predictions
    )

    assert metrics.provider == "nvidia"
    assert metrics.sample_count == 2
    assert metrics.domain_accuracy == 0.5
    assert metrics.severity_accuracy == 1.0
    assert metrics.triage_accuracy == 0.5
    assert metrics.average_confidence == pytest.approx(0.8)
    assert metrics.total_latency_ms == pytest.approx(300.0)
    assert metrics.average_latency_ms == pytest.approx(150.0)
    assert metrics.prompt_tokens == 110
    assert metrics.completion_tokens == 50
    assert metrics.total_tokens == 160


def test_metrics_reject_empty_predictions():
    with pytest.raises(ValueError, match="At least one"):
        LLMEvaluationMetrics.calculate([])


def test_metrics_reject_mixed_providers():
    predictions = [
        make_prediction(provider="nvidia"),
        make_prediction(provider="openrouter"),
    ]

    with pytest.raises(
        ValueError,
        match="same provider",
    ):
        LLMEvaluationMetrics.calculate(predictions)