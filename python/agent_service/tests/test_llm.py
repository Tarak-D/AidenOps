import httpx
import pytest

from agent_service.llm import (
    AnthropicClient,
    AzureOpenAIClient,
    GoogleClient,
    LlmResponse,
    NvidiaNimClient,
    OpenAIClient,
    OpenRouterClient,
    create_llm_client,
    parse_json_object,
)


def test_parse_json_object_accepts_plain_json() -> None:
    result = parse_json_object(
        '{"domain":"Network","severity":"P2","confidence":0.91}'
    )

    assert result["domain"] == "Network"
    assert result["severity"] == "P2"
    assert result["confidence"] == 0.91


def test_parse_json_object_accepts_markdown_json_fence() -> None:
    result = parse_json_object(
        """```json
{"domain":"Identity","severity":"P3","confidence":0.82}
```"""
    )

    assert result["domain"] == "Identity"
    assert result["severity"] == "P3"
    assert result["confidence"] == 0.82


def test_parse_json_object_rejects_non_object() -> None:
    with pytest.raises(ValueError):
        parse_json_object(
            '["not", "an", "object"]'
        )


def test_nvidia_nim_requires_api_key() -> None:
    client = NvidiaNimClient(
        base_url="https://example.test/v1",
        api_key="",
        model="test-model",
    )

    with pytest.raises(
        RuntimeError,
        match="API key is required",
    ):
        client.chat(
            system_prompt="You are a test agent.",
            user_prompt="Test request.",
        )


def test_openrouter_requires_api_key() -> None:
    client = OpenRouterClient(
        base_url="https://example.test/v1",
        api_key="",
        model="test-model",
    )

    with pytest.raises(
        RuntimeError,
        match="API key is required",
    ):
        client.chat(
            system_prompt="You are a test agent.",
            user_prompt="Test request.",
        )


def test_openai_requires_api_key() -> None:
    client = OpenAIClient(
        base_url="https://example.test/v1",
        api_key="",
        model="test-model",
    )

    with pytest.raises(
        RuntimeError,
        match="API key is required",
    ):
        client.chat(
            system_prompt="You are a test agent.",
            user_prompt="Test request.",
        )


def test_google_requires_api_key() -> None:
    client = GoogleClient(
        base_url="https://example.test/v1beta/openai",
        api_key="",
        model="gemini-test",
    )

    with pytest.raises(
        RuntimeError,
        match="API key is required",
    ):
        client.chat(
            system_prompt="You are a test agent.",
            user_prompt="Test request.",
        )


def test_azure_openai_requires_api_key() -> None:
    client = AzureOpenAIClient(
        base_url="https://example.test/openai/v1",
        api_key="",
        model="test-deployment",
    )

    with pytest.raises(
        RuntimeError,
        match="API key is required",
    ):
        client.chat(
            system_prompt="You are a test agent.",
            user_prompt="Test request.",
        )


def test_anthropic_requires_api_key() -> None:
    client = AnthropicClient(
        base_url="https://example.test",
        api_key="",
        model="claude-test",
    )

    with pytest.raises(
        RuntimeError,
        match="ANTHROPIC_API_KEY is required",
    ):
        client.chat(
            system_prompt="You are a test agent.",
            user_prompt="Test request.",
        )


def test_nvidia_nim_client_parses_chat_completion(
    monkeypatch,
) -> None:
    def handler(
        request: httpx.Request,
    ) -> httpx.Response:
        assert request.method == "POST"

        assert str(request.url) == (
            "https://example.test/v1/chat/completions"
        )

        assert request.headers["Authorization"] == (
            "Bearer test-key"
        )

        body = request.read().decode("utf-8")

        assert '"model":"test-model"' in body
        assert '"temperature":0.0' in body
        assert '"max_tokens":200' in body
        assert '"stream":false' in body
        assert '"response_format"' in body
        assert '"type":"json_object"' in body

        return httpx.Response(
            200,
            json={
                "model": "test-model",
                "choices": [
                    {
                        "message": {
                            "content": (
                                '{"domain":"Network",'
                                '"severity":"P2",'
                                '"confidence":0.91}'
                            )
                        }
                    }
                ],
                "usage": {
                    "prompt_tokens": 123,
                    "completion_tokens": 45,
                },
            },
            request=request,
        )

    transport = httpx.MockTransport(handler)

    client = NvidiaNimClient(
        base_url="https://example.test/v1",
        api_key="test-key",
        model="test-model",
    )

    original_client = httpx.Client

    class MockClient:
        def __init__(self, *args, **kwargs):
            kwargs["transport"] = transport

            self._client = original_client(
                *args,
                **kwargs,
            )

        def __enter__(self):
            self._client.__enter__()
            return self

        def __exit__(self, *args):
            return self._client.__exit__(*args)

        def post(self, *args, **kwargs):
            return self._client.post(
                *args,
                **kwargs,
            )

    monkeypatch.setattr(
        httpx,
        "Client",
        MockClient,
    )

    result = client.chat(
        system_prompt="You are a triage agent.",
        user_prompt="VPN is down.",
        temperature=0.0,
        max_tokens=200,
        response_format={
            "type": "json_object",
        },
    )

    assert result.content.startswith(
        '{"domain":"Network"'
    )
    assert result.model == "test-model"
    assert result.prompt_tokens == 123
    assert result.completion_tokens == 45
    assert result.latency_ms >= 0.0


def test_create_llm_client_selects_all_providers(
    monkeypatch,
) -> None:
    monkeypatch.setenv(
        "AZURE_OPENAI_ENDPOINT",
        "https://test-resource.openai.azure.com",
    )

    providers = {
        "nvidia": NvidiaNimClient,
        "openrouter": OpenRouterClient,
        "openai": OpenAIClient,
        "anthropic": AnthropicClient,
        "google": GoogleClient,
        "azure_openai": AzureOpenAIClient,
    }

    for provider, expected_type in providers.items():
        client = create_llm_client(
            provider=provider,
            model="test-model",
        )

        assert isinstance(
            client,
            expected_type,
        )

def test_create_llm_client_rejects_deterministic() -> None:
    with pytest.raises(
        ValueError,
        match="deterministic provider",
    ):
        create_llm_client(
            provider="deterministic",
        )


def test_create_llm_client_rejects_unknown_provider() -> None:
    with pytest.raises(
        ValueError,
        match="Unsupported AGENT_LLM_PROVIDER",
    ):
        create_llm_client(
            provider="does-not-exist",
            model="test-model",
        )


def test_azure_openai_builds_endpoint_from_resource() -> None:
    client = AzureOpenAIClient(
        base_url="https://example.openai.azure.com/openai/v1",
        api_key="test-key",
        model="test-deployment",
    )

    assert client.base_url == (
        "https://example.openai.azure.com/openai/v1"
    )


def test_llm_response_shape() -> None:
    response = LlmResponse(
        content="{}",
        model="test-model",
        prompt_tokens=10,
        completion_tokens=5,
        latency_ms=1.5,
    )

    assert response.content == "{}"
    assert response.model == "test-model"
    assert response.prompt_tokens == 10
    assert response.completion_tokens == 5
    assert response.latency_ms == 1.5