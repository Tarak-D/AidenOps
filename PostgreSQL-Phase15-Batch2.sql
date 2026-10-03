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