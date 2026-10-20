import json

import httpx
import pytest

from agent_service.llm import (
    AnthropicClient,
    AzureOpenAIClient,
    GoogleClient,
    LlmResponse,
    NvidiaNimClient,
    OpenAICompatibleClient,
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

        body = json.loads(request.read())

        assert body["model"] == "test-model"
        assert body["temperature"] == 0.0
        assert body["max_tokens"] == 200
        assert body["stream"] is False
        assert body["response_format"] == {"type": "json_object"}

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


def test_openrouter_client_parses_chat_completion(
    monkeypatch,
) -> None:
    monkeypatch.setenv(
        "OPENROUTER_HTTP_REFERER",
        "https://example.test/aidenops",
    )

    monkeypatch.setenv(
        "OPENROUTER_X_TITLE",
        "AidenOps AgentSwarm",
    )

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

        assert request.headers["HTTP-Referer"] == (
            "https://example.test/aidenops"
        )

        assert request.headers["X-Title"] == (
            "AidenOps AgentSwarm"
        )

        body = json.loads(request.read())

        assert body["model"] == "test-model"
        assert body["temperature"] == 0.0
        assert body["max_tokens"] == 200
        assert body["stream"] is False

        # Batch 10.3:
        # OpenRouter must receive strict structured output.
        response_format = body["response_format"]
        assert response_format["type"] == "json_schema"
        json_schema = response_format["json_schema"]
        assert json_schema["name"] == "aidenops_triage"
        assert json_schema["strict"] is True
        assert json_schema["schema"]["required"] == [
            "domain",
            "severity",
            "confidence",
        ]
        assert json_schema["schema"]["additionalProperties"] is False

        # Batch 10.3:
        # Require the selected OpenRouter provider to support
        # the requested parameters.
        assert body["provider"]["require_parameters"] is True

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

    client = OpenRouterClient(
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
            "type": "json_schema",
            "json_schema": {
                "name": "aidenops_triage",
                "strict": True,
                "schema": {
                    "type": "object",
                    "properties": {
                        "domain": {
                            "type": "string",
                            "enum": [
                                "Network",
                                "Identity",
                                "Database",
                                "Infrastructure",
                                "Unknown",
                            ],
                            "description": "The incident domain.",
                        },
                        "severity": {
                            "type": "string",
                            "enum": [
                                "P1",
                                "P2",
                                "P3",
                            ],
                            "description": (
                                "The incident severity."
                            ),
                        },
                        "confidence": {
                            "type": "number",
                            "description": (
                                "Classification confidence "
                                "from 0.0 to 1.0."
                            ),
                        },
                    },
                    "required": [
                        "domain",
                        "severity",
                        "confidence",
                    ],
                    "additionalProperties": False,
                },
            },
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


@pytest.mark.parametrize(
    ("client_type", "fails_before_response"),
    [
        (OpenAIClient, False),
        (AnthropicClient, False),
        (OpenAIClient, True),
        (AnthropicClient, True),
    ],
)
def test_provider_request_errors_do_not_expose_sensitive_data(
    monkeypatch: pytest.MonkeyPatch,
    client_type: type[OpenAIClient] | type[AnthropicClient],
    fails_before_response: bool,
) -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        if fails_before_response:
            raise httpx.ConnectError(
                "transport error containing test-key and request-sensitive-body",
                request=request,
            )

        return httpx.Response(
            500,
            json={"error": {"message": "response-sensitive-body"}},
            request=request,
        )

    transport = httpx.MockTransport(handler)
    original_client = httpx.Client

    class MockClient:
        def __init__(self, *args, **kwargs):
            kwargs["transport"] = transport
            self._client = original_client(*args, **kwargs)

        def __enter__(self):
            self._client.__enter__()
            return self

        def __exit__(self, *args):
            return self._client.__exit__(*args)

        def post(self, *args, **kwargs):
            return self._client.post(*args, **kwargs)

    monkeypatch.setattr(httpx, "Client", MockClient)

    client = client_type(
        base_url=(
            "https://example.test"
            if client_type is AnthropicClient
            else "https://example.test/v1"
        ),
        api_key="test-key",
        model="test-model",
    )

    expected_error = (
        "request failed"
        if fails_before_response
        else "HTTP 500"
    )
    with pytest.raises(RuntimeError, match=expected_error) as error:
        client.chat(
            system_prompt="request-sensitive-body",
            user_prompt="request-sensitive-body",
        )

    assert "test-key" not in str(error.value)
    assert "request-sensitive-body" not in str(error.value)
    assert "response-sensitive-body" not in str(error.value)
    assert error.value.__cause__ is None
    assert error.value.__context__ is None
    assert not hasattr(error.value, "request")
    assert not hasattr(error.value, "response")


@pytest.mark.parametrize(
    "client_type",
    [OpenAIClient, AnthropicClient],
)
def test_provider_clients_reject_malformed_json_without_chaining_body(
    monkeypatch: pytest.MonkeyPatch,
    client_type: type[OpenAIClient] | type[AnthropicClient],
) -> None:
    response_body = "sensitive-response-body"

    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(
            200,
            content=response_body.encode(),
            headers={"Content-Type": "application/json"},
            request=request,
        )

    transport = httpx.MockTransport(handler)
    original_client = httpx.Client

    class MockClient:
        def __init__(self, *args, **kwargs):
            kwargs["transport"] = transport
            self._client = original_client(*args, **kwargs)

        def __enter__(self):
            self._client.__enter__()
            return self

        def __exit__(self, *args):
            return self._client.__exit__(*args)

        def post(self, *args, **kwargs):
            return self._client.post(*args, **kwargs)

    monkeypatch.setattr(httpx, "Client", MockClient)

    client = client_type(
        base_url=(
            "https://example.test"
            if client_type is AnthropicClient
            else "https://example.test/v1"
        ),
        api_key="test-key",
        model=(
            "claude-test"
            if client_type is AnthropicClient
            else "test-model"
        ),
    )

    with pytest.raises(ValueError, match="returned invalid JSON") as error:
        client.chat(
            system_prompt="Test system",
            user_prompt="Test user",
        )

    assert error.value.__cause__ is None
    assert error.value.__context__ is None
    assert response_body not in str(error.value)
    assert error.value.args == (
        "Anthropic returned invalid JSON."
        if client_type is AnthropicClient
        else "Provider 'openai' returned invalid JSON.",
    )


def test_openai_compatible_client_prevents_auth_header_overrides(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        assert request.headers["Authorization"] == "Bearer configured-key"
        assert request.headers["X-Custom-Header"] == "custom-value"
        assert "X-Api-Key" not in request.headers
        assert "Api-Key" not in request.headers
        assert "X-Goog-Api-Key" not in request.headers
        return httpx.Response(
            200,
            json={
                "choices": [
                    {
                        "message": {
                            "content": "ok",
                        },
                    },
                ],
                "usage": {
                    "prompt_tokens": 1,
                    "completion_tokens": 1,
                },
            },
            request=request,
        )

    transport = httpx.MockTransport(handler)
    original_client = httpx.Client

    class MockClient:
        def __init__(self, *args, **kwargs):
            kwargs["transport"] = transport
            self._client = original_client(*args, **kwargs)

        def __enter__(self):
            self._client.__enter__()
            return self

        def __exit__(self, *args):
            return self._client.__exit__(*args)

        def post(self, *args, **kwargs):
            return self._client.post(*args, **kwargs)

    monkeypatch.setattr(httpx, "Client", MockClient)

    client = OpenAICompatibleClient(
        provider="test",
        base_url="https://example.test/v1",
        api_key="configured-key",
        model="test-model",
        extra_headers={
            "aUtHoRiZaTiOn": "Bearer overridden-key",
            "X-Api-Key": "overridden-key",
            "api-key": "also-overridden-key",
            "x-goog-api-key": "another-overridden-key",
            "X-Custom-Header": "custom-value",
        },
    )

    result = client.chat(
        system_prompt="Test system",
        user_prompt="Test user",
    )

    assert result.content == "ok"

def test_openai_compatible_client_rejects_malformed_choice(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(
            200,
            json={"choices": ["invalid-choice"]},
            request=request,
        )

    transport = httpx.MockTransport(handler)
    original_client = httpx.Client

    class MockClient:
        def __init__(self, *args, **kwargs):
            kwargs["transport"] = transport
            self._client = original_client(*args, **kwargs)

        def __enter__(self):
            self._client.__enter__()
            return self

        def __exit__(self, *args):
            return self._client.__exit__(*args)

        def post(self, *args, **kwargs):
            return self._client.post(*args, **kwargs)

    monkeypatch.setattr(httpx, "Client", MockClient)

    client = OpenAIClient(
        base_url="https://example.test/v1",
        api_key="test-key",
        model="test-model",
    )

    with pytest.raises(ValueError, match="invalid choice"):
        client.chat(
            system_prompt="Test system",
            user_prompt="Test user",
        )


@pytest.mark.parametrize(
    "payload",
    [
        [],
        {"choices": [{"message": {"content": "ok"}}], "usage": []},
        {
            "choices": [{"message": {"content": "ok"}}],
            "usage": {"prompt_tokens": -1},
        },
        {
            "choices": [{"message": {"content": "ok"}}],
            "usage": {"completion_tokens": True},
        },
        {
            "choices": [{"message": {"content": "ok"}}],
            "usage": {},
        },
        {
            "choices": [{"message": {"content": "ok"}}],
            "usage": {"prompt_tokens": 1},
        },
    ],
)
def test_openai_compatible_client_rejects_invalid_response_metadata(
    monkeypatch: pytest.MonkeyPatch,
    payload: object,
) -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json=payload, request=request)

    transport = httpx.MockTransport(handler)
    original_client = httpx.Client

    class MockClient:
        def __init__(self, *args, **kwargs):
            kwargs["transport"] = transport
            self._client = original_client(*args, **kwargs)

        def __enter__(self):
            self._client.__enter__()
            return self

        def __exit__(self, *args):
            return self._client.__exit__(*args)

        def post(self, *args, **kwargs):
            return self._client.post(*args, **kwargs)

    monkeypatch.setattr(httpx, "Client", MockClient)
    client = OpenAIClient(
        base_url="https://example.test/v1",
        api_key="test-key",
        model="test-model",
    )

    with pytest.raises(ValueError):
        client.chat(system_prompt="Test", user_prompt="Test")


@pytest.mark.parametrize("timeout", [0, -1, float("inf"), float("nan")])
def test_openai_compatible_client_rejects_invalid_timeout(
    timeout: float,
) -> None:
    with pytest.raises(ValueError, match="finite positive"):
        OpenAIClient(
            base_url="https://example.test/v1",
            api_key="test-key",
            model="test-model",
            timeout_seconds=timeout,
        )


@pytest.mark.parametrize("client_type", [OpenAIClient, NvidiaNimClient])
@pytest.mark.parametrize("model", ["", "   "])
def test_openai_compatible_client_rejects_blank_model(
    client_type: type[OpenAIClient] | type[NvidiaNimClient],
    model: str,
) -> None:
    with pytest.raises(ValueError, match="must not be blank"):
        client_type(
            base_url="https://example.test/v1",
            api_key="test-key",
            model=model,
        )


def test_create_llm_client_rejects_explicit_blank_provider(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("AGENT_LLM_PROVIDER", "openai")

    with pytest.raises(ValueError, match="must not be blank"):
        create_llm_client(provider="", model="test-model")


def test_anthropic_client_uses_native_messages_contract(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        assert request.url.path == "/v1/messages"
        assert request.headers["x-api-key"] == "test-key"
        assert request.headers["anthropic-version"] == "2023-06-01"
        payload = json.loads(request.read())
        assert payload["system"] == "system"
        assert payload["messages"] == [
            {"role": "user", "content": "incident"}
        ]
        assert payload["max_tokens"] == 80
        assert "response_format" not in payload
        return httpx.Response(
            200,
            json={
                "model": "claude-test",
                "content": [{"type": "text", "text": '{"ok":true}'}],
                "usage": {"input_tokens": 17, "output_tokens": 9},
            },
            request=request,
        )

    transport = httpx.MockTransport(handler)
    original_client = httpx.Client

    class MockClient:
        def __init__(self, *args, **kwargs):
            kwargs["transport"] = transport
            self._client = original_client(*args, **kwargs)

        def __enter__(self):
            self._client.__enter__()
            return self

        def __exit__(self, *args):
            return self._client.__exit__(*args)

        def post(self, *args, **kwargs):
            return self._client.post(*args, **kwargs)

    monkeypatch.setattr(httpx, "Client", MockClient)
    client = AnthropicClient(
        base_url="https://example.test",
        api_key="test-key",
        model="claude-test",
    )

    result = client.chat(
        system_prompt="system",
        user_prompt="incident",
        max_tokens=80,
        response_format={"type": "json_object"},
    )

    assert result.content == '{"ok":true}'
    assert result.model == "claude-test"
    assert result.prompt_tokens == 17
    assert result.completion_tokens == 9
