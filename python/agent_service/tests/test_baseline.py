from agent_service.evaluation.baseline import (
    build_baseline_classifier,
    build_baseline_dataset,
    build_baseline_training_corpus,
    run_classical_ml_baseline,
)


def test_baseline_dataset_is_versioned():
    dataset = build_baseline_dataset()

    assert dataset.dataset_id == "phase15-classical-ml-baseline"
    assert dataset.name == "Phase 15 Classical ML Baseline"
    assert dataset.version == "1.0.0"
    assert len(dataset.cases) == 8


def test_training_corpus_is_fixed_versioned_and_separate_from_evaluation():
    training = build_baseline_training_corpus()
    evaluation = build_baseline_dataset()

    assert training.dataset_id == "phase15-classical-ml-training"
    assert training.name == "Phase 15 Classical ML Training Corpus"
    assert training.version == "1.0.0"
    assert tuple(case.case_id for case in training.cases) == (
        "training-network-001",
        "training-network-002",
        "training-identity-001",
        "training-identity-002",
        "training-database-001",
        "training-database-002",
        "training-infrastructure-001",
        "training-infrastructure-002",
    )
    assert all(
        case.case_id not in {evaluation_case.case_id for evaluation_case in evaluation.cases}
        for case in training.cases
    )


def test_baseline_classifier_contains_expected_domains():
    classifier = build_baseline_classifier()

    assert classifier.classes == (
        "Database",
        "Identity",
        "Infrastructure",
        "Network",
    )


def test_baseline_run_is_successful():
    report = run_classical_ml_baseline()

    assert report.dataset_id == "phase15-classical-ml-baseline"
    assert report.dataset_version == "1.0.0"
    assert report.training_corpus_id == "phase15-classical-ml-training"
    assert report.training_corpus_version == "1.0.0"
    assert report.model_type == "classical_ml"
    assert report.model_name == "tfidf-logistic-regression"

    assert report.total_cases == 8
    assert report.correct_cases == 8
    assert report.accuracy == 1.0
    assert report.case_ids == tuple(
        case.case_id for case in build_baseline_dataset().cases
    )
    assert report.predictions == report.expected_domains
    assert len(report.confidences) == report.total_cases


def test_baseline_report_to_dict():
    report = run_classical_ml_baseline()

    payload = report.to_dict()

    assert payload == {
        "training_corpus_id": "phase15-classical-ml-training",
        "training_corpus_name": "Phase 15 Classical ML Training Corpus",
        "training_corpus_version": "1.0.0",
        "dataset_id": "phase15-classical-ml-baseline",
        "dataset_name": "Phase 15 Classical ML Baseline",
        "dataset_version": "1.0.0",
        "model_type": "classical_ml",
        "model_name": "tfidf-logistic-regression",
        "total_cases": 8,
        "correct_cases": 8,
        "accuracy": 1.0,
        "case_ids": [
            "network-001",
            "network-002",
            "identity-001",
            "identity-002",
            "database-001",
            "database-002",
            "infrastructure-001",
            "infrastructure-002",
        ],
        "predictions": [
            "Network",
            "Network",
            "Identity",
            "Identity",
            "Database",
            "Database",
            "Infrastructure",
            "Infrastructure",
        ],
        "expected_domains": [
            "Network",
            "Network",
            "Identity",
            "Identity",
            "Database",
            "Database",
            "Infrastructure",
            "Infrastructure",
        ],
        "confidences": list(report.confidences),
        "case_results": [
            {
                "case_id": case_id,
                "predicted_domain": prediction,
                "expected_domain": expected,
                "confidence": confidence,
            }
            for case_id, prediction, expected, confidence in zip(
                report.case_ids,
                report.predictions,
                report.expected_domains,
                report.confidences,
                strict=True,
            )
        ],
    }


def test_baseline_run_is_reproducible():
    first = run_classical_ml_baseline()
    second = run_classical_ml_baseline()

    assert first == second