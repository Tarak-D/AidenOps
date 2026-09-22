import json
import os
import time
from dataclasses import dataclass
from typing import Any, Protocol

import httpx


@dataclass(frozen=True)
class LlmResponse:
    content: str
    model: str
    prompt_tokens: int
    completion_tokens: int
    latency_ms: float


class LlmClient(Protocol):
    def chat(
        self,
        *,
        system_prompt: str,
        user_prompt: str,
        temperature: float = 0.0,
        max_tokens: int = 512,
        response_format: dict[str, Any] | None = None,
    ) -> LlmResponse:
        ...


def _required_env(
    name: str,
) -> str:
    value = os.getenv(
        name,
        "",
    ).strip()

    if not value:
        raise RuntimeError(
            f"{name} is required for the selected "
            "LLM provider."
        )

    return value


def _resolve_model(
    provider: str,
    explicit_model: str | None = None,
) -> str:
    if explicit_model:
        return explicit_model.strip()

    common_model = os.getenv(
        "AGENT_LLM_MODEL",
        "",
    ).strip()

    if common_model:
        return common_model

    provider_model_env = {
        "nvidia": "NVIDIA_NIM_MODEL",
        "openrouter": "OPENROUTER_MODEL",
        "openai": "OPENAI_MODEL",
        "anthropic": "ANTHROPIC_MODEL",
        "google": "GOOGLE_MODEL",
        "azure_openai": "AZURE_OPENAI_MODEL",
    }.get(provider)

    if provider_model_env:
        model = os.getenv(
            provider_model_env,
            "",
        ).strip()

        if model:
            return model

    raise RuntimeError(
        f"No model configured for provider "
        f"'{provider}'. Set AGENT_LLM_MODEL or "
        f"the provider-specific model variable."
    )


class OpenAICompatibleClient:
    """
    Generic OpenAI-compatible chat completion client.

    This supports providers exposing:
      POST /chat/completions

    The base URL should normally include the provider's
    API version path, for example:

      https://api.openai.com/v1
      https://openrouter.ai/api/v1
      https://integrate.api.nvidia.com/v1
      https://generativelanguage.googleapis.com/v1beta/openai
      https://<resource>.openai.azure.com/openai/v1
    """

    def __init__(
        self,
        *,
        provider: str,
        base_url: str,
        api_key: str,
        model: str,
        timeout_seconds: float = 60.0,
        extra_headers: dict[str, str] | None = None,
        extra_payload: dict[str, Any] | None = None,
    ) -> None:
        self.provider = provider
        self.base_url = base_url.rstrip("/")
        self.api_key = api_key.strip()
        self.model = model
        self.timeout_seconds = timeout_seconds
        self.extra_headers = extra_headers or {}
        self.extra_payload = extra_payload or {}

    def chat(
        self,
        *,
        system_prompt: str,
        user_prompt: str,
        temperature: float = 0.0,
        max_tokens: int = 512,
        response_format: dict[str, Any] | None = None,
    ) -> LlmResponse:
        if not self.api_key:
            raise RuntimeError(
                f"API key is required for provider "
                f"'{self.provider}'."
            )

        headers = {
            "Content-Type": "application/json",
            "Authorization": f"Bearer {self.api_key}",
            **self.extra_headers,
        }

        payload: dict[str, Any] = {
            "model": self.model,
            "messages": [
                {
                    "role": "system",
                    "content": system_prompt,
                },
                {
                    "role": "user",
                    "content": user_prompt,
                },
            ],
            "temperature": temperature,
            "max_tokens": max_tokens,
            "stream": False,
            **self.extra_payload,
        }

        if response_format is not None:
            payload["response_format"] = response_format

        started = time.perf_counter()

        with httpx.Client(
            timeout=self.timeout_seconds,
        ) as client:
            response = client.post(
                f"{self.base_url}/chat/completions",
                headers=headers,
                json=payload,
            )

        latency_ms = (
            time.perf_counter() - started
        ) * 1000.0

        response.raise_for_status()

        body = response.json()

        choices = body.get(
            "choices",
            [],
        )

        if not choices:
            raise ValueError(
                f"Provider '{self.provider}' returned "
                "no choices."
            )

        message = choices[0].get(
            "message",
            {},
        )

        content = message.get("content")

        if not isinstance(content, str):
            raise ValueError(
                f"Provider '{self.provider}' returned "
                "non-text message content."
            )

        if not content.strip():
            raise ValueError(
                f"Provider '{self.provider}' returned "
                "empty message content."
            )

        usage = body.get("usage") or {}

        return LlmResponse(
            content=content,
            model=str(
                body.get("model")
                or self.model
            ),
            prompt_tokens=int(
                usage.get(
                    "prompt_tokens",
                    usage.get(
                        "input_tokens",
                        0,
                    ),
                )
            ),
            completion_tokens=int(
                usage.get(
                    "completion_tokens",
                    usage.get(
                        "output_tokens",
                        0,
                    ),
                )
            ),
            latency_ms=latency_ms,
        )


class NvidiaNimClient(OpenAICompatibleClient):
    """
    NVIDIA NIM provider.

    Default endpoint:
      https://integrate.api.nvidia.com/v1

    Default model:
      moonshotai/kimi-k3

    Kimi-K3 is a reasoning model. For concise structured
    classification, use low reasoning effort by default.
    """

    def __init__(
        self,
        *,
        base_url: str | None = None,
        api_key: str | None = None,
        model: str | None = None,
        timeout_seconds: float | None = None,
    ) -> None:
        configured_model = (
            model
            or os.getenv(
                "AGENT_LLM_MODEL",
                "",
            ).strip()
            or os.getenv(
                "NVIDIA_NIM_MODEL",
                "moonshotai/kimi-k3",
            )
        )

        super().__init__(
            provider="nvidia",
            base_url=(
                base_url
                or os.getenv(
                    "NVIDIA_NIM_BASE_URL",
                    "https://integrate.api.nvidia.com/v1",
                )
            ),
            api_key=(
                api_key
                if api_key is not None
                else os.getenv(
                    "NVIDIA_NIM_API_KEY",
                    "",
                )
            ),
            model=configured_model,
            timeout_seconds=float(
                timeout_seconds
                if timeout_seconds is not None
                else os.getenv(
                    "NVIDIA_NIM_TIMEOUT_SECONDS",
                    "180",
                )
            ),
            extra_headers={
                "Accept": "application/json",
            },
            extra_payload={
                "reasoning_effort": os.getenv(
                    "NVIDIA_NIM_REASONING_EFFORT",
                    "low",
                ).strip(),
            },
        )


class OpenRouterClient(OpenAICompatibleClient):
    """
    OpenRouter provider.

    Default endpoint:
      https://openrouter.ai/api/v1
    """

    def __init__(
        self,
        *,
        base_url: str | None = None,
        api_key: str | None = None,
        model: str | None = None,
        timeout_seconds: float | None = None,
    ) -> None:
        super().__init__(
            provider="openrouter",
            base_url=(
                base_url
                or os.getenv(
                    "OPENROUTER_BASE_URL",
                    "https://openrouter.ai/api/v1",
                )
            ),
            api_key=(
                api_key
                if api_key is not None
                else os.getenv(
                    "OPENROUTER_API_KEY",
                    "",
                )
            ),
            model=_resolve_model(
                "openrouter",
                model,
            ),
            timeout_seconds=float(
                timeout_seconds
                if timeout_seconds is not None
                else os.getenv(
                    "OPENROUTER_TIMEOUT_SECONDS",
                    "60",
                )
            ),
            extra_headers={
                "HTTP-Referer": os.getenv(
                    "OPENROUTER_HTTP_REFERER",
                    "",
                ),
                "X-Title": os.getenv(
                    "OPENROUTER_X_TITLE",
                    "AidenOps AgentSwarm",
                ),
            },
        )


class OpenAIClient(OpenAICompatibleClient):
    """
    OpenAI provider.

    Default endpoint:
      https://api.openai.com/v1
    """

    def __init__(
        self,
        *,
        base_url: str | None = None,
        api_key: str | None = None,
        model: str | None = None,
        timeout_seconds: float | None = None,
    ) -> None:
        super().__init__(
            provider="openai",
            base_url=(
                base_url
                or os.getenv(
                    "OPENAI_BASE_URL",
                    "https://api.openai.com/v1",
                )
            ),
            api_key=(
                api_key
                if api_key is not None
                else os.getenv(
                    "OPENAI_API_KEY",
                    "",
                )
            ),
            model=_resolve_model(
                "openai",
                model,
            ),
            timeout_seconds=float(
                timeout_seconds
                if timeout_seconds is not None
                else os.getenv(
                    "OPENAI_TIMEOUT_SECONDS",
                    "60",
                )
            ),
        )


class GoogleClient(OpenAICompatibleClient):
    """
    Google Gemini provider using Google's
    OpenAI-compatible API.

    Default endpoint:
      https://generativelanguage.googleapis.com/v1beta/openai
    """

    def __init__(
        self,
        *,
        base_url: str | None = None,
        api_key: str | None = None,
        model: str | None = None,
        timeout_seconds: float | None = None,
    ) -> None:
        super().__init__(
            provider="google",
            base_url=(
                base_url
                or os.getenv(
                    "GOOGLE_BASE_URL",
                    "https://generativelanguage.googleapis.com/v1beta/openai",
                )
            ),
            api_key=(
                api_key
                if api_key is not None
                else os.getenv(
                    "GOOGLE_API_KEY",
                    "",
                )
            ),
            model=_resolve_model(
                "google",
                model,
            ),
            timeout_seconds=float(
                timeout_seconds
                if timeout_seconds is not None
                else os.getenv(
                    "GOOGLE_TIMEOUT_SECONDS",
                    "60",
                )
            ),
        )


class AzureOpenAIClient(OpenAICompatibleClient):
    """
    Azure OpenAI provider.

    Default endpoint is derived from:

      AZURE_OPENAI_ENDPOINT

    Example:

      https://my-resource.openai.azure.com/openai/v1

    The model value is the Azure deployment/model name.
    """

    def __init__(
        self,
        *,
        base_url: str | None = None,
        api_key: str | None = None,
        model: str | None = None,
        timeout_seconds: float | None = None,
    ) -> None:
        configured_endpoint = (
            base_url
            or os.getenv(
                "AZURE_OPENAI_BASE_URL",
                "",
            )
        ).strip()

        if not configured_endpoint:
            endpoint = os.getenv(
                "AZURE_OPENAI_ENDPOINT",
                "",
            ).strip()

            if not endpoint:
                raise RuntimeError(
                    "AZURE_OPENAI_ENDPOINT or "
                    "AZURE_OPENAI_BASE_URL is required "
                    "for Azure OpenAI."
                )

            configured_endpoint = (
                endpoint.rstrip("/")
                + "/openai/v1"
            )

        super().__init__(
            provider="azure_openai",
            base_url=configured_endpoint,
            api_key=(
                api_key
                if api_key is not None
                else os.getenv(
                    "AZURE_OPENAI_API_KEY",
                    "",
                )
            ),
            model=_resolve_model(
                "azure_openai",
                model,
            ),
            timeout_seconds=float(
                timeout_seconds
                if timeout_seconds is not None
                else os.getenv(
                    "AZURE_OPENAI_TIMEOUT_SECONDS",
                    "60",
                )
            ),
        )


class AnthropicClient:
    """
    Anthropic native Messages API client.

    Endpoint:
      https://api.anthropic.com/v1/messages
    """

    def __init__(
        self,
        *,
        base_url: str | None = None,
        api_key: str | None = None,
        model: str | None = None,
        timeout_seconds: float | None = None,
    ) -> None:
        self.provider = "anthropic"

        self.base_url = (
            base_url
            or os.getenv(
                "ANTHROPIC_BASE_URL",
                "https://api.anthropic.com",
            )
        ).rstrip("/")

        self.api_key = (
            api_key
            if api_key is not None
            else os.getenv(
                "ANTHROPIC_API_KEY",
                "",
            )
        ).strip()

        self.model = _resolve_model(
            "anthropic",
            model,
        )

        self.timeout_seconds = float(
            timeout_seconds
            if timeout_seconds is not None
            else os.getenv(
                "ANTHROPIC_TIMEOUT_SECONDS",
                "60",
            )
        )

    def chat(
        self,
        *,
        system_prompt: str,
        user_prompt: str,
        temperature: float = 0.0,
        max_tokens: int = 512,
        response_format: dict[str, Any] | None = None,
    ) -> LlmResponse:
        if not self.api_key:
            raise RuntimeError(
                "ANTHROPIC_API_KEY is required for "
                "the Anthropic provider."
            )

        headers = {
            "Content-Type": "application/json",
            "x-api-key": self.api_key,
            "anthropic-version": "2023-06-01",
        }

        payload: dict[str, Any] = {
            "model": self.model,
            "system": system_prompt,
            "messages": [
                {
                    "role": "user",
                    "content": user_prompt,
                }
            ],
            "max_tokens": max_tokens,
        }

        # Anthropic's native API has provider/model-specific
        # sampling behavior. Keep the common interface but do
        # not force temperature into requests for models where
        # it is not accepted.
        if temperature != 0.0:
            payload["temperature"] = temperature

        started = time.perf_counter()

        with httpx.Client(
            timeout=self.timeout_seconds,
        ) as client:
            response = client.post(
                f"{self.base_url}/v1/messages",
                headers=headers,
                json=payload,
            )

        latency_ms = (
            time.perf_counter() - started
        ) * 1000.0

        response.raise_for_status()

        body = response.json()

        content_blocks = body.get(
            "content",
            [],
        )

        text_parts: list[str] = []

        for block in content_blocks:
            if (
                isinstance(block, dict)
                and block.get("type") == "text"
                and isinstance(
                    block.get("text"),
                    str,
                )
            ):
                text_parts.append(
                    block["text"]
                )

        content = "".join(text_parts).strip()

        if not content:
            raise ValueError(
                "Anthropic returned no text "
                "content."
            )

        usage = body.get("usage") or {}

        return LlmResponse(
            content=content,
            model=str(
                body.get("model")
                or self.model
            ),
            prompt_tokens=int(
                usage.get(
                    "input_tokens",
                    0,
                )
            ),
            completion_tokens=int(
                usage.get(
                    "output_tokens",
                    0,
                )
            ),
            latency_ms=latency_ms,
        )


def create_llm_client(
    provider: str | None = None,
    model: str | None = None,
) -> LlmClient:
    """
    Create the configured LLM provider.

    Supported providers:

      deterministic
      nvidia
      openrouter
      openai
      anthropic
      google
      azure_openai

    The deterministic provider is intentionally not an LLM
    client. It is handled by the agent layer so CI remains
    completely offline.
    """

    selected = (
        provider
        or os.getenv(
            "AGENT_LLM_PROVIDER",
            "deterministic",
        )
    ).strip().lower()

    if selected == "nvidia":
        return NvidiaNimClient(
            model=model,
        )

    if selected == "openrouter":
        return OpenRouterClient(
            model=model,
        )

    if selected == "openai":
        return OpenAIClient(
            model=model,
        )

    if selected == "anthropic":
        return AnthropicClient(
            model=model,
        )

    if selected == "google":
        return GoogleClient(
            model=model,
        )

    if selected in {
        "azure",
        "azure_openai",
        "azure-openai",
    }:
        return AzureOpenAIClient(
            model=model,
        )

    if selected == "deterministic":
        raise ValueError(
            "The deterministic provider does not "
            "create an LLM client. Use the deterministic "
            "agent path instead."
        )

    raise ValueError(
        f"Unsupported AGENT_LLM_PROVIDER="
        f"'{selected}'. Expected one of: "
        "deterministic, nvidia, openrouter, openai, "
        "anthropic, google, azure_openai."
    )


def parse_json_object(
    content: str,
) -> dict[str, Any]:
    """
    Parse a JSON object returned by a model.

    Strict JSON is preferred. A markdown JSON code fence
    is accepted as a small robustness measure.
    """

    text = content.strip()

    if text.startswith("```"):
        lines = text.splitlines()

        if (
            lines
            and lines[0].strip().startswith("```")
        ):
            lines = lines[1:]

        if (
            lines
            and lines[-1].strip() == "```"
        ):
            lines = lines[:-1]

        text = "\n".join(lines).strip()

        if text.lower().startswith("json"):
            text = text[4:].lstrip()

    parsed = json.loads(text)

    if not isinstance(parsed, dict):
        raise ValueError(
            "Expected the model response to be "
            "a JSON object."
        )

    return parsed