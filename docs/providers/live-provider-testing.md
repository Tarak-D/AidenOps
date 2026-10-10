# Live Provider Smoke Testing

Live-provider testing is optional and separate from deterministic unit tests. It requires network access, valid credentials, provider permissions, and a currently available model.

## Before running

1. Activate the repository's Python virtual environment.
2. Check `python/.env.example` and the current provider factory for supported variable names.
3. Select a currently available model.
4. Set credentials only in the local shell or a local ignored environment file.
5. Run deterministic tests independently.

## OpenRouter example

The following variable names were documented in an earlier README. Verify them against the current implementation first.

From the Python directory:

```powershell
cd D:\Project\AIOps.AgentSwarm\python
.\.venv\Scripts\Activate.ps1

$env:AGENT_LLM_PROVIDER = "openrouter"
$env:OPENROUTER_API_KEY = "YOUR_REAL_OPENROUTER_KEY"
$env:AGENT_LLM_MODEL = "YOUR_CURRENT_MODEL"
```

Replace the placeholders locally. Never paste a real API key into chat or commit it.

## Discover OpenRouter models

The previous README documented this catalog request:

```powershell
python -c "import os,httpx; key=os.getenv('OPENROUTER_API_KEY',''); print('KEY:', 'SET' if key else 'NOT SET'); r=httpx.get('https://openrouter.ai/api/v1/models',headers={'Authorization':f'Bearer {key}'},timeout=30); print('STATUS:',r.status_code); print(r.text[:3000])"
```

Use a returned model identifier only after confirming it is available to your account. The documented common model override is `AGENT_LLM_MODEL`; verify current behavior before relying on it.

## What to verify

- Credentials are accepted without exposing them.
- The endpoint is reachable and the model is available.
- The application parses and validates the response.
- Model, latency, and token metadata are recorded when available.
- Errors do not leak credentials or sensitive request data.

A successful provider smoke test does not prove production readiness.

## Return to deterministic mode

After testing, restore the documented setting in the current PowerShell session:

```powershell
$env:AGENT_LLM_PROVIDER = "deterministic"
```

This does not change persistent environment variables or configuration files.

## Related guides

- [Provider configuration](provider-configuration.md)
- [Development setup](../development/README.md)
- [Troubleshooting](../development/troubleshooting.md)