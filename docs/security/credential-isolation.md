# Credential Isolation

## Security boundary

Credentials used by external integrations and LLM providers must remain under controlled configuration boundaries. The model must not gain direct access to privileged credentials or execute consequential operations independently.

## Principles

- Keep secrets out of source control and README examples.
- Use local environment configuration or a managed secret store.
- Avoid placing secrets in agent prompts, tool manifests, traces, exception messages, or audit payloads.
- Return sanitized errors from integration boundaries.
- Restrict credential-bearing operations to trusted server-side components.
- Use tests to verify that sensitive values are not returned or logged.

## Integration contracts

Integration results and contracts should contain only the information needed by orchestration and audit. Do not return raw authorization headers, tokens, or secret-bearing provider responses.

## Verification

For every integration, review:

1. Where credentials are loaded.
2. Which class owns the credential.
3. Whether the credential crosses the .NET/Python boundary.
4. Whether logs or exceptions can include it.
5. Whether tests assert secret redaction.
6. Whether local examples contain blank/fake values only.

This page describes the security intent; use current implementation and tests to establish which controls are actually enforced.
