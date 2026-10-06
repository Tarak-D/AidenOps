from agent_service.evaluation.baseline import (
    build_baseline_classifier,
    build_baseline_dataset,
    run_classical_ml_baseline,
)


def test_baseline_dataset_is_versioned():
    dataset = build_baseline_dataset()

    assert dataset.dataset_id == "phase15-classical-ml-baseline"
    assert dataset.name == "Phase 15 Classical ML Baseline"
    assert dataset.version == "1.0.0"
    assert len(dataset.cases) == 8


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
    assert report.model_type == "classical_ml"
    assert report.model_name == "tfidf-logistic-regression"

    assert report.total_cases == 8
    assert report.correct_cases == 8
    assert report.accuracy == 1.0


def test_baseline_report_to_dict():
    report = run_classical_ml_baseline()

    payload = report.to_dict()

    assert payload == {
        "dataset_id": "phase15-classical-ml-baseline",
        "dataset_name": "Phase 15 Classical ML Baseline",
        "dataset_version": "1.0.0",
        "model_type": "classical_ml",
        "model_name": "tfidf-logistic-regression",
        "total_cases": 8,
        "correct_cases": 8,
        "accuracy": 1.0,
    }


def test_baseline_run_is_reproducible():
    first = run_classical_ml_baseline()
    second = run_classical_ml_baseline()

    assert first == second