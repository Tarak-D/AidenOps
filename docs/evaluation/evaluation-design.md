# Evaluation Design

## Purpose

AidenOps includes evaluation components for classical ML classification, LLM-based evaluation, retrieval evaluation, and persisted experiment-run comparison.

Evaluation measures model and agent behavior on defined inputs. It does not independently grant operational authority or authorize external tool execution.

## Implemented foundation

### Versioned golden datasets

The Python evaluation package defines `GoldenDataset` and `GoldenDatasetCase`.

A dataset contains:
- Dataset ID, name, and version.
- Cases with unique case IDs, titles, descriptions, and expected domains.
- An optional expected severity field.

Validation checks required dataset metadata, non-empty cases, and unique, non-empty case IDs.

The golden-dataset evaluator adapts cases into examples for the classical ML evaluator. Dataset version metadata exists; a complete dataset registry or immutable version-management workflow is not established by this documentation alone.

### Classical ML baseline

The Python package includes a TF-IDF + Logistic Regression domain classifier.

The classifier supports:
- Training on titles, descriptions, and domain labels.
- Single and batch prediction.
- Predicted domain and confidence output.
- Validation of training input lengths and class requirements.

This is an evaluation baseline independent of the existing LangGraph triage flow. Its existence does not by itself establish comparative performance against every LLM provider or production workload.

### Persisted experiment runs

The .NET evaluation contract records:
- Experiment ID and model type/name.
- Optional prompt version.
- Dataset name and version.
- Configuration JSON and sample count.
- Start and optional finish timestamps.
- Metrics JSON and optional notes.

The evaluation store supports recording runs and listing runs with optional experiment and dataset filters.

### Evaluation API

The host exposes a read-only comparison endpoint:

`GET /api/v1/evaluations/comparison`

It accepts optional `experimentId` and `datasetName` filters and requires the `CanViewQueue` authorization policy.

The endpoint returns a comparison report for persisted evaluation runs. Do not infer that every possible regression workflow, dashboard, or alerting mechanism is implemented solely from this endpoint.

### LLM cost estimation

LLM evaluation supports configured input and output prices per million tokens. When the required pricing and usage data are available, estimated cost is calculated as:

```text
estimated_cost =
  (prompt_tokens * input_price_per_million
   + completion_tokens * output_price_per_million) / 1,000,000
```

This is an estimate based on configured prices and reported token counts, not necessarily a provider invoice. Cost may be unavailable when required inputs are missing. Consult the implementation and tests for the exact validation and serialization behavior.

### Retrieval evaluation

The source includes a retrieval evaluation runner. Review its metrics and test coverage before documenting individual retrieval metrics or claiming complete retrieval-quality reporting.

### Deterministic acceptance-gate core

The .NET evaluation core supports an explicit acceptance policy with required
metric names, an explicit higher-is-better or lower-is-better direction, and
an allowed absolute regression for each metric. The gate compares one uniquely
resolved run for a named baseline experiment with one uniquely resolved run
for a named candidate experiment. It does not treat all pairwise comparison
report entries as release gates.

The result is deterministic for the same runs and policy. It reports an
overall `Passed`, `Failed`, or `Blocked` status and per-metric observed values,
regression amount, allowance, and threshold violation where available.
Metric values and allowances use decimal arithmetic; regression is the
adverse-direction difference, floored at zero. A regression equal to the
allowance passes, while any larger value fails without a floating-point
epsilon. Required metric JSON values that are malformed, duplicated,
nonnumeric, or outside the supported decimal range fail closed. Missing or
invalid required metric values fail. Missing/ambiguous runs, missing required
compatibility metadata, and incompatible runs are blocked, never passed.
Policies require at least one unique metric, a valid explicit direction, and
a non-negative decimal allowance.

Compatibility requires distinct persisted run IDs, matching dataset name and
version, positive matching sample counts, matching model type/name and prompt
version, and equivalent non-empty configuration JSON. LLM runs must also have
non-empty provider and model-version fields in the configuration, whose
configured model type/name/prompt version must match the run record.
Configuration comparison ignores root-level notes, explanatory execution
notes, and the redundant root-level sample count; all other persisted
configuration fields must match.

Provider and model-version values are required and compared for LLM runs, but
are self-reported persisted metadata, not independently attested provider
identity or immutable model revision. Dataset identity/version and sample
count are checked, but persisted run records do not include dataset case IDs
or a content digest, so identical dataset labels/counts cannot prove identical
case contents. Hidden runtime settings not recorded in configuration also
cannot be verified. The gate does not infer these provenance details.

This batch adds no endpoint, persistence for gate results, CI workflow, agent
execution, or tool execution. The existing read-only comparison endpoint and
its pairwise trend-report semantics are unchanged.

## Run configuration and safety

The .NET evaluation run configuration includes:
- Experiment ID.
- Model metadata.
- Dataset name and version.
- Optional maximum case count.
- A deterministic-execution setting, defaulting to `true`.
- An external-tool execution setting, defaulting to `false`.
- Optional notes.

Evaluation runners do not execute tools or perform approval/resume. Setting
`ExecuteExternalTools` to `true` is rejected because there is no evaluation
execution path that uses the server-side approval and `ToolExecutor` lifecycle.
Approval-required outcomes therefore remain pending in the evaluation result,
and execution success is left unknown rather than inferred from a synthetic
tool result.

The presence of these settings documents the intended controls; verify the runner's behavior and tests before relying on them as guarantees for every evaluation path.

## Areas requiring further verification

Before describing these areas as complete, verify their implementation, integration, and test evidence:

- A managed, immutable golden-dataset registry.
- Full end-to-end evaluation across triage, investigation, decisions, and tool proposals.
- Consistent experiment metadata across all evaluation paths.
- Provider, model, and prompt comparisons using comparable datasets and configurations.
- Persisted regression thresholds, reporting, and automated failure gates.
- Defined metric direction, aggregation, missing-value handling, and acceptance thresholds.
- Retrieval metrics and their interpretation.
- Live-provider evaluation behavior and cost accuracy.

## Reproducibility and data handling

Where supported, preserve dataset name/version, experiment identity, model/provider, prompt version, configuration, timestamps, outcomes, and metrics.

Keep secrets and sensitive incident data out of golden datasets and evaluation reports. Use controlled test data and avoid enabling external-tool execution unless the evaluation explicitly requires it and appropriate safeguards are in place.

## Maintenance

Update this document when source code and tests demonstrate a material change. Distinguish implemented capabilities from partial integrations and planned work. Record the relevant commit and test results when documenting a verified milestone.
