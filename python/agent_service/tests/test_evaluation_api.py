from fastapi.testclient import TestClient

from agent_service import main
from agent_service.agents import triage
from agent_service.evaluation import baseline


class FakeResponse:
    content = (
        '{"domain":"Network",'
        '"severity":"P1",'
        '"confidence":0.95}'
    )
    model = "test-api-model"
    prompt_tokens = 45
    completion_tokens = 14
    latency_ms = 22.0


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


def test_llm_evaluation_endpoint(monkeypatch):
    def fake_create_llm_client(provider):
        assert provider == "nvidia"
        return FakeClient()

    monkeypatch.setattr(
        triage,
        "create_llm_client",
        fake_create_llm_client,
    )

    client = TestClient(main.app)

    response = client.post(
        "/api/v1/evaluation/llm",
        json={
    "datasetId": "phase15-llm-api",
    "datasetName": "phase-15-llm-evaluation",
    "datasetVersion": "1.0",
    "provider": "nvidia",
    "cases": [
        {
            "caseId": "vpn-1",
            "title": "VPN outage",
            "description": (
                "Corporate VPN is down."
            ),
            "expectedDomain": "Network",
            "expectedSeverity": "P1",
        }
    ],
},
    )

    assert response.status_code == 200

    payload = response.json()

    assert payload["modelType"] == "llm"

    assert payload["dataset"] == {
        "id": "phase15-llm-api",
        "name": "phase-15-llm-evaluation",
        "version": "1.0",
    }

    assert payload["model"] == {
    "provider": "nvidia",
    "name": "test-api-model",
    "promptVersion": "v1-nvidia-triage",
}
    assert payload["sampleCount"] == 1

    assert payload["metrics"]["domainAccuracy"] == 1.0
    assert payload["metrics"]["severityAccuracy"] == 1.0
    assert payload["metrics"]["triageAccuracy"] == 1.0

    assert (
    payload["predictions"][0]["caseId"]
    == "vpn-1"
)


def test_llm_evaluation_endpoint_requires_cases():
    client = TestClient(main.app)

    response = client.post(
        "/api/v1/evaluation/llm",
        json={
            "dataset_id": "phase15-invalid",
            "dataset_name": "invalid",
            "dataset_version": "1.0",
            "provider": "nvidia",
            "cases": [],
        },
    )

    assert response.status_code == 422


def test_llm_evaluation_endpoint_requires_expected_severity():
    client = TestClient(main.app)

    response = client.post(
        "/api/v1/evaluation/llm",
        json={
            "dataset_id": "phase15-invalid",
            "dataset_name": "invalid",
            "dataset_version": "1.0",
            "provider": "nvidia",
            "cases": [
                {
                    "case_id": "case-1",
                    "title": "VPN outage",
                    "description": "VPN is down.",
                    "expected_domain": "Network",
                }
            ],
        },
    )

    assert response.status_code == 422


def test_classical_ml_evaluation_endpoint_returns_versioned_ordered_report(
    monkeypatch,
):
    report = baseline.run_classical_ml_baseline()
    monkeypatch.setattr(
        main,
        "run_classical_ml_baseline",
        lambda: report,
    )

    response = TestClient(main.app).get(
        "/api/v1/evaluation/classical-ml"
    )

    assert response.status_code == 200
    payload = response.json()
    assert payload["trainingCorpusId"] == "phase15-classical-ml-training"
    assert payload["trainingCorpusVersion"] == "1.0.0"
    assert payload["datasetId"] == "phase15-classical-ml-baseline"
    assert payload["datasetVersion"] == "1.0.0"
    assert payload["modelType"] == "classical_ml"
    assert payload["totalCases"] == 8
    assert payload["accuracy"] == 1.0
    assert payload["caseIds"] == list(report.case_ids)
    assert payload["predictions"] == list(report.predictions)
    assert payload["caseResults"][0] == {
        "caseId": report.case_ids[0],
        "predictedDomain": report.predictions[0],
        "expectedDomain": report.expected_domains[0],
        "confidence": report.confidences[0],
    }