from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable

from agent_service.evaluation.llm_evaluator import (
    LLMEvaluationPrediction,
)
from agent_service.evaluation.llm_metrics import (
    LLMEvaluationMetrics,
)


@dataclass(frozen=True)
class LLMEvaluationReport:
    dataset_id: str
    dataset_name: str
    dataset_version: str
    provider: str
    model: str
    prompt_version: str
    metrics: LLMEvaluationMetrics
    predictions: tuple[LLMEvaluationPrediction, ...]

    @property
    def model_type(self) -> str:
        return "llm"

    @property
    def sample_count(self) -> int:
        return len(self.predictions)

    def to_dict(self) -> dict:
        return {
            "model_type": self.model_type,
            "dataset": {
                "id": self.dataset_id,
                "name": self.dataset_name,
                "version": self.dataset_version,
            },
            "model": {
                "provider": self.provider,
                "name": self.model,
                "prompt_version": self.prompt_version,
            },
            "sample_count": self.sample_count,
            "metrics": {
                "domain_accuracy": self.metrics.domain_accuracy,
                "severity_accuracy": self.metrics.severity_accuracy,
                "triage_accuracy": self.metrics.triage_accuracy,
                "average_confidence": self.metrics.average_confidence,
                "total_latency_ms": self.metrics.total_latency_ms,
                "average_latency_ms": self.metrics.average_latency_ms,
                "prompt_tokens": self.metrics.prompt_tokens,
                "completion_tokens": self.metrics.completion_tokens,
                "total_tokens": self.metrics.total_tokens,
            },
            "predictions": [
                {
                    "case_id": prediction.case_id,
                    "provider": prediction.provider,
                    "model": prediction.model,
                    "prompt_version": prediction.prompt_version,
                    "domain": prediction.domain,
                    "severity": prediction.severity,
                    "confidence": prediction.confidence,
                    "domain_correct": prediction.domain_correct,
                    "severity_correct": prediction.severity_correct,
                    "triage_correct": prediction.triage_correct,
                    "prompt_tokens": prediction.prompt_tokens,
                    "completion_tokens": prediction.completion_tokens,
                    "total_tokens": prediction.total_tokens,
                    "latency_ms": prediction.latency_ms,
                }
                for prediction in self.predictions
            ],
        }


def build_llm_evaluation_report(
    *,
    dataset_id: str,
    dataset_name: str,
    dataset_version: str,
    predictions: Iterable[LLMEvaluationPrediction],
) -> LLMEvaluationReport:
    predictions = tuple(predictions)

    if not predictions:
        raise ValueError(
            "At least one LLM evaluation prediction is required."
        )

    provider = predictions[0].provider
    model = predictions[0].model
    prompt_version = predictions[0].prompt_version

    if any(
        prediction.provider != provider
        for prediction in predictions
    ):
        raise ValueError(
            "All predictions must use the same provider."
        )

    if any(
        prediction.model != model
        for prediction in predictions
    ):
        raise ValueError(
            "All predictions must use the same model."
        )

    if any(
        prediction.prompt_version != prompt_version
        for prediction in predictions
    ):
        raise ValueError(
            "All predictions must use the same prompt version."
        )

    metrics = LLMEvaluationMetrics.calculate(
        predictions
    )

    return LLMEvaluationReport(
        dataset_id=dataset_id,
        dataset_name=dataset_name,
        dataset_version=dataset_version,
        provider=provider,
        model=model,
        prompt_version=prompt_version,
        metrics=metrics,
        predictions=predictions,
    )