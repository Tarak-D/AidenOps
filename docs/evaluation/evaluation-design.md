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

## Run configuration and safety

The .NET evaluation run configuration includes:
- Experiment ID.
- Model metadata.
- Dataset name and version.
- Optional maximum case count.
- A deterministic-execution setting, defaulting to `true`.
- An external-tool execution setting, defaulting to `false`.
- Optional notes.

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
