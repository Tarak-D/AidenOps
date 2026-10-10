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
