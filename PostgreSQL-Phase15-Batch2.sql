-- Phase 15.2 Batch 2
-- Versioned evaluation/golden dataset persistence.

CREATE TABLE IF NOT EXISTS evaluation_datasets
(
    id uuid PRIMARY KEY,
    dataset_id character varying(100) NOT NULL,
    name character varying(200) NOT NULL,
    version character varying(50) NOT NULL,
    description character varying(2000),
    created_at timestamp with time zone NOT NULL,
    cases_json text NOT NULL,

    CONSTRAINT uq_evaluation_datasets_dataset_id_version
        UNIQUE (dataset_id, version)
);

CREATE INDEX IF NOT EXISTS ix_evaluation_datasets_dataset_id
    ON evaluation_datasets (dataset_id);

CREATE INDEX IF NOT EXISTS ix_evaluation_datasets_created_at
    ON evaluation_datasets (created_at);

-- Append-only decisions produced by the deterministic evaluation acceptance gate.
CREATE TABLE IF NOT EXISTS evaluation_acceptance_results
(
    id uuid PRIMARY KEY,
    evaluated_at timestamp with time zone NOT NULL,
    baseline_experiment_id character varying(200) NOT NULL,
    candidate_experiment_id character varying(200) NOT NULL,
    baseline_run_id uuid,
    candidate_run_id uuid,
    policy_name character varying(200) NOT NULL,
    policy_json text NOT NULL,
    status character varying(20) NOT NULL,
    issues_json text NOT NULL,
    metrics_json text NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_evaluation_acceptance_results_evaluated_at
    ON evaluation_acceptance_results (evaluated_at);

CREATE INDEX IF NOT EXISTS ix_evaluation_acceptance_results_baseline_experiment_id
    ON evaluation_acceptance_results (baseline_experiment_id);

CREATE INDEX IF NOT EXISTS ix_evaluation_acceptance_results_candidate_experiment_id
    ON evaluation_acceptance_results (candidate_experiment_id);
