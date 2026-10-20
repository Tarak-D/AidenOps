# Integration Status and Verification

## Integration inventory in the previous README

The historical documentation listed these integration areas:

| Area | Named integrations |
|---|---|
| Cloud | AWS EC2 |
| Directory and identity | Microsoft Graph / Entra ID |
| ITSM | ServiceNow, Jira Service Management, Zendesk |
| VPN and network access | Cisco Secure Access, Palo Alto GlobalProtect, Fortinet FortiGate |
| Network diagnostics | Cisco ThousandEyes |

The list is an inventory from prior documentation. It does not by itself prove that each integration is production-ready or has passed a live end-to-end test.

## How to report status accurately

Use distinct labels:

- **Interface present** — abstraction or contract exists.
- **Implementation present** — provider code exists.
- **Simulated/tested** — tests or fake implementation cover behavior.
- **Live smoke-tested** — a real external service was reached with valid configuration.
- **Production-validated** — operational, security, failure, retry, and deployment requirements have been verified.

Do not collapse these states into a single “complete” label.

## Current source-level inventory

The following classifications are based on adapters and mock-based tests in
the repository. No live external service was contacted during this audit.

| Area | Source-level status | Important boundary |
|---|---|---|
| Cloud | AWS EC2 SDK adapter present; Simulated remains the default. | AWS calls are behind `ICloudProvider` and `ToolExecutor`. SDK-mocked tests are not a live AWS smoke test. |
| Directory | Microsoft Graph group membership adapter present; Simulated remains the default. | Production password reset explicitly remains unsupported. Role-assignable groups fail closed. Graph tests use SDK mocks. |
| ITSM | ServiceNow, Jira Service Management, and Zendesk HTTP adapters and persisted selection are present. | Connector tests use mocked HTTP responses. No live tenant/account validation is recorded. |
| VPN diagnostics | Cisco Secure Access, Palo Alto GlobalProtect, and Fortinet adapters are present, alongside Simulated. | Provider selection is persisted; configured real providers do not silently become Simulated. No live smoke test is recorded. |
| General network diagnostics | Cisco ThousandEyes adapter is present; Simulated remains available. | Other listed vendor slots are unconfigured placeholders. Status checks and active diagnostics may contact a real provider when selected and configured. |

Treat adapter presence and mock-tested behavior as implementation evidence,
not proof of production authorization, tenant configuration, or live service
availability.

## Cisco ThousandEyes

The previous README describes an agent-to-server instant-test flow: create a test, poll for a result, then normalize diagnostic fields such as latency, packet loss, target/server, agent details, and provider status. It states that live verification requires valid credentials and account/agent configuration.

Verify current code, provider API behavior, and live test evidence before claiming a successful production run.

Do not describe an integration as complete without specifying its verification level and supporting evidence.
