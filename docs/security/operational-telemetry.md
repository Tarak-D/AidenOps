# Operational Telemetry

## Purpose

Telemetry should help operators understand operation counts, durations, outcomes, and failures without capturing secrets or sensitive payloads.

## Documented telemetry direction

The previous README describes centralized instrumentation with counters, duration histograms, outcome/error classification, and correlation support. It also notes bounded identifiers to prevent arbitrary payload-like values from becoming metric dimensions.

## Safe instrumentation rules

- Prefer bounded, low-cardinality dimensions.
- Do not use full prompts, tickets, user identifiers, arbitrary payloads, API keys, or exception text as metric labels.
- Sanitize exceptions before logging.
- Preserve correlation identifiers where permitted and useful.
- Distinguish success, failure, timeout, and other defined outcomes.
- Avoid recording request bodies by default.

## Verification checklist

Before documenting a metric as implemented, locate its instrument definition and call sites, then check tests for expected tags and redaction. Names, units, dimensions, and exporters should be taken from the current source rather than inferred from this overview.

The current source defines `ActivitySource` and `Meter` instruments and calls
them around selected orchestration and integration operations. The Host uses
Serilog console logging. No OpenTelemetry exporter or `AddMeter`/`AddSource`
registration is present in the inspected Host setup, so instrument creation
does not by itself establish collection or export. PostgreSQL health checks are
registered when a connection string exists, but `/health` is a static Host
liveness response and does not expose those checks as readiness.

## Action execution recovery boundary

Action claims are persisted before tool invocation, and completion is persisted
after the tool returns. If the process stops, cancellation interrupts the flow,
or completion persistence fails after a tool may have run, the persisted action
can remain `Executing` even though its external outcome is unknown. The current
implementation has no stale-claim reconciler or operator recovery workflow.
Treat these actions as indeterminate: do not automatically retry them or mark
them failed until the external side effect has been checked.
