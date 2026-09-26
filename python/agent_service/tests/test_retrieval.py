import httpx
import pytest

from agent_service.retrieval import service


def make_response(
    status_code: int,
    *,
    url: str,
    json: object,
) -> httpx.Response:
    request = httpx.Request(
        "GET",
        url,
    )

    return httpx.Response(
        status_code,
        json=json,
        request=request,
    )


def test_retrieve_knowledge_calls_control_plane(monkeypatch):
    captured = {}

    def fake_get(url, *, params, timeout):
        captured["url"] = url
        captured["params"] = params
        captured["timeout"] = timeout

        return make_response(
            200,
            url=url,
            json=[
                {
                    "chunkId": "chunk-1",
                    "documentId": "doc-1",
                    "source": "runbook.md",
                    "title": "VPN Troubleshooting",
                    "chunkIndex": 0,
                    "content": "Restart the VPN client.",
                    "similarity": 0.91,
                    "metadataJson": "{\"domain\":\"Network\"}",
                }
            ],
        )

    monkeypatch.setattr(service.httpx, "get", fake_get)

    monkeypatch.setenv(
        "AIOPS_CONTROL_PLANE_URL",
        "http://localhost:5166",
    )

    knowledge, trace = service.retrieve_knowledge(
        "VPN connection failure",
        limit=3,
    )

    assert captured["url"] == (
        "http://localhost:5166/api/v1/knowledge/search"
    )

    assert captured["params"] == {
        "q": "VPN connection failure",
        "limit": 3,
    }

    assert captured["timeout"] == 10.0

    assert len(knowledge) == 1
    assert knowledge[0]["chunk_id"] == "chunk-1"
    assert knowledge[0]["document_id"] == "doc-1"
    assert knowledge[0]["source"] == "runbook.md"
    assert knowledge[0]["title"] == "VPN Troubleshooting"
    assert knowledge[0]["content"] == "Restart the VPN client."
    assert knowledge[0]["similarity"] == 0.91

    assert trace.agent == "KnowledgeAgent"
    assert trace.step_name == "knowledge"
    assert trace.prompt_version == "v1-http"


def test_retrieve_knowledge_uses_default_control_plane_url(monkeypatch):
    captured = {}

    def fake_get(url, *, params, timeout):
        captured["url"] = url

        return make_response(
            200,
            url=url,
            json=[],
        )

    monkeypatch.delenv(
        "AIOPS_CONTROL_PLANE_URL",
        raising=False,
    )

    monkeypatch.setattr(service.httpx, "get", fake_get)

    knowledge, trace = service.retrieve_knowledge(
        "database connection failure",
    )

    assert captured["url"] == (
        "http://localhost:5166/api/v1/knowledge/search"
    )

    assert knowledge == []
    assert trace.agent == "KnowledgeAgent"


def test_retrieve_knowledge_rejects_empty_query():
    knowledge, trace = service.retrieve_knowledge("   ")

    assert knowledge == []
    assert trace.agent == "KnowledgeAgent"
    assert trace.step_name == "knowledge"


def test_retrieve_knowledge_rejects_invalid_limit():
    with pytest.raises(
        ValueError,
        match="greater than zero",
    ):
        service.retrieve_knowledge(
            "VPN failure",
            limit=0,
        )


def test_retrieve_knowledge_raises_on_http_failure(monkeypatch):
    def fake_get(url, *, params, timeout):
        return make_response(
            500,
            url=url,
            json={
                "error": "database unavailable",
            },
        )

    monkeypatch.setattr(service.httpx, "get", fake_get)

    with pytest.raises(
        RuntimeError,
        match="Knowledge retrieval request failed",
    ):
        service.retrieve_knowledge(
            "VPN failure",
        )


def test_retrieve_knowledge_rejects_invalid_response_shape(monkeypatch):
    def fake_get(url, *, params, timeout):
        return make_response(
            200,
            url=url,
            json={
                "results": [],
            },
        )

    monkeypatch.setattr(service.httpx, "get", fake_get)

    with pytest.raises(
        RuntimeError,
        match="unexpected response shape",
    ):
        service.retrieve_knowledge(
            "VPN failure",
        )