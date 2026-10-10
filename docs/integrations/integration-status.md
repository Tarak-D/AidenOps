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

## Cisco ThousandEyes

The previous README describes an agent-to-server instant-test flow: create a test, poll for a result, then normalize diagnostic fields such as latency, packet loss, target/server, agent details, and provider status. It states that live verification requires valid credentials and account/agent configuration.

Verify current code, provider API behavior, and live test evidence before claiming a successful production run.

Do not describe an integration as complete without specifying its verification level and supporting evidence.