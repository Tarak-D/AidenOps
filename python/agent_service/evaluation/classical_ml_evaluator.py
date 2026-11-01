from dataclasses import dataclass
from typing import Sequence


from agent_service.evaluation.classical_ml import (
    ClassicalMLDomainClassifier,
)


@dataclass(frozen=True)
class ClassicalMLEvaluationExample:
    title: str
    description: str
    expected_domain: str
    case_id: str | None = None


@dataclass(frozen=True)
class ClassicalMLEvaluationResult:
    total_cases: int
    correct_cases: int
    accuracy: float
    predictions: tuple[str, ...]
    expected_domains: tuple[str, ...]
    case_ids: tuple[str | None, ...]
    confidences: tuple[float, ...]

    model_type: str = "classical_ml"
    model_name: str = "tfidf-logistic-regression"

    def to_dict(self) -> dict[str, object]:
        return {
            "model_type": self.model_type,
            "model_name": self.model_name,
            "total_cases": self.total_cases,
            "correct_cases": self.correct_cases,
            "accuracy": self.accuracy,
            "case_ids": list(self.case_ids),
            "predictions": list(self.predictions),
            "expected_domains": list(self.expected_domains),
            "confidences": list(self.confidences),
        }


class ClassicalMLEvaluator:
    """
    Evaluation adapter for the classical TF-IDF + Logistic Regression
    domain-classification baseline.
    """

    def __init__(
        self,
        classifier: ClassicalMLDomainClassifier,
    ) -> None:
        self._classifier = classifier

    def evaluate(
        self,
        cases: Sequence[ClassicalMLEvaluationExample],
    ) -> ClassicalMLEvaluationResult:
        if not cases:
            raise ValueError(
                "At least one evaluation case is required."
            )

        ordered_cases = tuple(cases)
        batch_predictions = self._classifier.predict_batch(
            [case.title for case in ordered_cases],
            [case.description for case in ordered_cases],
        )
        predictions = tuple(
            prediction.domain
            for prediction in batch_predictions
        )
        expected_domains = tuple(
            case.expected_domain
            for case in ordered_cases
        )
        case_ids = tuple(
            case.case_id
            for case in ordered_cases
        )
        confidences = tuple(
            prediction.confidence
            for prediction in batch_predictions
        )
        correct_cases = sum(
            prediction == expected
            for prediction, expected in zip(
                predictions,
                expected_domains,
                strict=True,
            )
        )

        total_cases = len(ordered_cases)
        accuracy = correct_cases / total_cases

        return ClassicalMLEvaluationResult(
            total_cases=total_cases,
            correct_cases=correct_cases,
            accuracy=accuracy,
            predictions=predictions,
            expected_domains=expected_domains,
            case_ids=case_ids,
            confidences=confidences,
        )