from dataclasses import dataclass

from agent_service.evaluation.classical_ml import (
    ClassicalMLDomainClassifier,
)
from agent_service.evaluation.classical_ml_evaluator import (
    ClassicalMLEvaluationResult,
    ClassicalMLEvaluator,
)
from agent_service.evaluation.golden_dataset import (
    GoldenDataset,
    GoldenDatasetCase,
    GoldenDatasetEvaluator,
)


@dataclass(frozen=True)
class ClassicalMLBaselineReport:
    training_corpus_id: str
    training_corpus_name: str
    training_corpus_version: str
    dataset_id: str
    dataset_name: str
    dataset_version: str
    model_type: str
    model_name: str
    total_cases: int
    correct_cases: int
    accuracy: float
    case_ids: tuple[str, ...]
    predictions: tuple[str, ...]
    expected_domains: tuple[str, ...]
    confidences: tuple[float, ...]

    def to_dict(self) -> dict[str, object]:
        return {
            "training_corpus_id": self.training_corpus_id,
            "training_corpus_name": self.training_corpus_name,
            "training_corpus_version": self.training_corpus_version,
            "dataset_id": self.dataset_id,
            "dataset_name": self.dataset_name,
            "dataset_version": self.dataset_version,
            "model_type": self.model_type,
            "model_name": self.model_name,
            "total_cases": self.total_cases,
            "correct_cases": self.correct_cases,
            "accuracy": self.accuracy,
            "case_ids": list(self.case_ids),
            "predictions": list(self.predictions),
            "expected_domains": list(self.expected_domains),
            "confidences": list(self.confidences),
            "case_results": [
                {
                    "case_id": case_id,
                    "predicted_domain": prediction,
                    "expected_domain": expected,
                    "confidence": confidence,
                }
                for case_id, prediction, expected, confidence in zip(
                    self.case_ids,
                    self.predictions,
                    self.expected_domains,
                    self.confidences,
                    strict=True,
                )
            ],
        }


def build_baseline_training_corpus() -> GoldenDataset:
    return GoldenDataset(
        dataset_id="phase15-classical-ml-training",
        name="Phase 15 Classical ML Training Corpus",
        version="1.0.0",
        cases=(
            GoldenDatasetCase(
                "training-network-001",
                "VPN failure",
                "Corporate VPN is down.",
                "Network",
            ),
            GoldenDatasetCase(
                "training-network-002",
                "Network outage",
                "Office network is unavailable.",
                "Network",
            ),
            GoldenDatasetCase(
                "training-identity-001",
                "Password reset",
                "User needs a password reset.",
                "Identity",
            ),
            GoldenDatasetCase(
                "training-identity-002",
                "Account locked",
                "User account is locked.",
                "Identity",
            ),
            GoldenDatasetCase(
                "training-database-001",
                "Database unavailable",
                "Production database is unavailable.",
                "Database",
            ),
            GoldenDatasetCase(
                "training-database-002",
                "SQL query failure",
                "Production SQL query failed.",
                "Database",
            ),
            GoldenDatasetCase(
                "training-infrastructure-001",
                "CPU spike",
                "Production server CPU is critically high.",
                "Infrastructure",
            ),
            GoldenDatasetCase(
                "training-infrastructure-002",
                "Disk full",
                "Production server disk is full.",
                "Infrastructure",
            ),
        ),
    )


def _fit_baseline_classifier(
    training_corpus: GoldenDataset,
) -> ClassicalMLDomainClassifier:
    training_corpus.validate()
    classifier = ClassicalMLDomainClassifier()

    classifier.fit(
        [case.title for case in training_corpus.cases],
        [case.description for case in training_corpus.cases],
        [case.expected_domain for case in training_corpus.cases],
    )

    return classifier


def build_baseline_classifier() -> ClassicalMLDomainClassifier:
    return _fit_baseline_classifier(
        build_baseline_training_corpus()
    )


def build_baseline_dataset() -> GoldenDataset:
    return GoldenDataset(
        dataset_id="phase15-classical-ml-baseline",
        name="Phase 15 Classical ML Baseline",
        version="1.0.0",
        cases=(
            GoldenDatasetCase(
                case_id="network-001",
                title="VPN connection failure",
                description="The corporate VPN connection is unavailable.",
                expected_domain="Network",
            ),
            GoldenDatasetCase(
                case_id="network-002",
                title="Network outage",
                description="The office network is completely unavailable.",
                expected_domain="Network",
            ),
            GoldenDatasetCase(
                case_id="identity-001",
                title="Password reset",
                description="The user needs a password reset.",
                expected_domain="Identity",
            ),
            GoldenDatasetCase(
                case_id="identity-002",
                title="Locked account",
                description="The user account is locked and cannot login.",
                expected_domain="Identity",
            ),
            GoldenDatasetCase(
                case_id="database-001",
                title="Database unavailable",
                description="The production database is unavailable.",
                expected_domain="Database",
            ),
            GoldenDatasetCase(
                case_id="database-002",
                title="SQL query failure",
                description="A production SQL query is failing.",
                expected_domain="Database",
            ),
            GoldenDatasetCase(
                case_id="infrastructure-001",
                title="CPU spike",
                description="Production server CPU usage is critically high.",
                expected_domain="Infrastructure",
            ),
            GoldenDatasetCase(
                case_id="infrastructure-002",
                title="Disk full",
                description="Production server disk space is exhausted.",
                expected_domain="Infrastructure",
            ),
        ),
    )


def run_classical_ml_baseline() -> ClassicalMLBaselineReport:
    training_corpus = build_baseline_training_corpus()
    classifier = _fit_baseline_classifier(training_corpus)
    dataset = build_baseline_dataset()

    evaluator = GoldenDatasetEvaluator(
        ClassicalMLEvaluator(classifier)
    )

    result: ClassicalMLEvaluationResult = evaluator.evaluate(
        dataset
    )

    return ClassicalMLBaselineReport(
        training_corpus_id=training_corpus.dataset_id,
        training_corpus_name=training_corpus.name,
        training_corpus_version=training_corpus.version,
        dataset_id=dataset.dataset_id,
        dataset_name=dataset.name,
        dataset_version=dataset.version,
        model_type=result.model_type,
        model_name=result.model_name,
        total_cases=result.total_cases,
        correct_cases=result.correct_cases,
        accuracy=result.accuracy,
        case_ids=tuple(case.case_id for case in dataset.cases),
        predictions=result.predictions,
        expected_domains=result.expected_domains,
        confidences=result.confidences,
    )