# Security Documentation

Security boundaries and operational considerations.

- [Authentication and authorization](#authentication-and-authorization)
- [Credential isolation](credential-isolation.md) — credential handling boundaries.
- [Operational telemetry](operational-telemetry.md) — operational visibility and telemetry.
- [Documentation index](../README.md)

## Authentication and authorization

The host currently implements only a **local Development identity**. In the
Development environment, `appsettings.Development.json` explicitly enables
`Authentication:DevelopmentIdentity:Enabled`; the `X-Dev-User` request header
selects one of the local `viewer`, `engineer`, `approver`, or `admin` identities.
Omitting the header selects `viewer`. This is a demo/test convenience, not
authentication: a caller can select any of those identities.

The Development identity scheme is not registered outside Development. If the
`Authentication:DevelopmentIdentity` section is present in any non-Development
configuration, host startup fails. Otherwise the host uses an unauthenticated
fallback, so authorization-protected endpoints return 401 and the
`X-Dev-User` header has no effect. No production identity provider is currently
configured; do not deploy this host as an authenticated production service.
Before non-Development use, integrate a supported identity provider, validate
its issuer and credentials, map trusted identity claims to application roles,
and verify the resulting authorization policies.

Sensitive route policies currently include:

- Approval creation, listing, decisions, and agent-run resume require
  `Approver` or `Admin`.
- Evaluation acceptance writes require `Engineer` or `Admin`; read-only
  evaluation comparison requires an authenticated identity.
- Audit reads require `Admin`.
- Ticket reads, knowledge search, and ticket creation/runs require an
  authenticated identity.
- Integration provider selection retains its explicit local-development
  convenience policy; outside Development it requires `Admin`.

The development header must not be forwarded through a proxy or treated as a
trusted identity assertion. The .NET control plane enforces authorization for
approval, ToolExecutor execution, and audit; Python proposals do not grant
identity or authority. The `/demo` page itself is anonymous UI, not an
authorization boundary. Its local Development workflow sends the demo identity
to the APIs, where the same server-side policies still apply.

Keep the Development host bound to loopback for local demos. Do not expose it
to a shared network: anyone who can reach a Development instance can select
the local `admin` identity with `X-Dev-User`.
