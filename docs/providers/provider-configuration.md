# Provider Configuration

## Safe default

The documented safe development defaults are:

```text
.NET: AI:AgentGatewayMode=Fake
Python: AGENT_LLM_PROVIDER=deterministic
```

These defaults avoid a live model dependency for normal local testing. Confirm exact configuration binding in current source and configuration files.

## Python provider selection

The historical provider abstraction uses:

- `AGENT_LLM_PROVIDER` to select a provider.
- `AGENT_LLM_MODEL` as a common model override where supported.
- Provider-specific model and credential variables for provider-specific configuration.

The historical provider list includes deterministic mode, OpenRouter, NVIDIA NIM, OpenAI, Anthropic, Google, and Azure OpenAI. Availability and current support must be confirmed in the current provider factory and tests.

## Historical environment-variable names

The prior README documented these names:

```text
AGENT_LLM_PROVIDER
AGENT_LLM_MODEL
NVIDIA_NIM_BASE_URL
NVIDIA_NIM_MODEL
NVIDIA_NIM_TIMEOUT_SECONDS
NVIDIA_NIM_REASONING_EFFORT
NVIDIA_NIM_API_KEY
OPENROUTER_MODEL
OPENROUTER_TIMEOUT_SECONDS
OPENROUTER_API_KEY
OPENAI_MODEL
OPENAI_API_KEY
ANTHROPIC_MODEL
ANTHROPIC_API_KEY
GOOGLE_MODEL
GOOGLE_API_KEY
AZURE_OPENAI_MODEL
AZURE_OPENAI_API_KEY
```

This is a historical inventory, not a guarantee that every variable is still read by the current implementation. Compare with `python/.env.example` and the provider factory before relying on a name.

## .NET gateway settings

The previous README documented:

```text
AI:AgentGatewayMode
AI:AgentService:BaseUrl
AI:AgentService:TimeoutSeconds
AI:NvidiaNim:BaseUrl
AI:NvidiaNim:Model
AI:NvidiaNim:ApiKey
```

Confirm the current configuration binding and defaults in `src/AIOps.Host` and the relevant options classes.

## Secret hygiene

- Keep actual keys in local environment configuration or a secret manager.
- Keep example values blank or clearly fake.
- Never commit keys, passwords, or production connection strings.
- Do not log authorization headers or secret-bearing request objects.
