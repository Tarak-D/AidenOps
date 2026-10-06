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


@dataclass(frozen=True)
class ClassicalMLEvaluationResult:
    total_cases: int
    correct_cases: int
    accuracy: float
    predictions: tuple[str, ...]
    expected_domains: tuple[str, ...]

    model_type: str = "classical_ml"
    model_name: str = "tfidf-logistic-regression"

    def to_dict(self) -> dict[str, object]:
        return {
            "model_type": self.model_type,
            "model_name": self.model_name,
            "total_cases": self.total_cases,
            "correct_cases": self.correct_cases,
            "accuracy": self.accuracy,
            "predictions": list(self.predictions),
            "expected_domains": list(self.expected_domains),
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

        predictions: list[str] = []
        expected_domains: list[str] = []
        correct_cases = 0

        for case in cases:
            prediction = self._classifier.predict(
                case.title,
                case.description,
            )

            predictions.append(prediction.domain)
            expected_domains.append(case.expected_domain)

            if prediction.domain == case.expected_domain:
                correct_cases += 1

        total_cases = len(cases)
        accuracy = correct_cases / total_cases

        return ClassicalMLEvaluationResult(
            total_cases=total_cases,
            correct_cases=correct_cases,
            accuracy=accuracy,
            predictions=tuple(predictions),
            expected_domains=tuple(expected_domains),
        )