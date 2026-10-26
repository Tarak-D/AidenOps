-- Phase 15.2 Batch 2: persist evaluation experiment runs.
-- Matches EvaluationRecordEntity and AIOpsDbContext.
-- IF NOT EXISTS skips existing objects; it does not repair an incompatible table or index.

CREATE TABLE IF NOT EXISTS evaluation_runs
(
    id uuid NOT NULL,
    experiment_id character varying(200) NOT NULL,
    model_type character varying(50) NOT NULL,
    model_name character varying(200) NOT NULL,
    prompt_version character varying(200),
    dataset_name character varying(200) NOT NULL,
    dataset_version character varying(50) NOT NULL,
    config_json text NOT NULL,
    sample_count integer NOT NULL,
    started_at timestamp with time zone NOT NULL,
    finished_at timestamp with time zone,
    metrics_json text NOT NULL,
    notes text,
    CONSTRAINT "PK_evaluation_runs" PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS "IX_evaluation_runs_experiment_id"
    ON evaluation_runs (experiment_id);

CREATE INDEX IF NOT EXISTS "IX_evaluation_runs_model_type"
    ON evaluation_runs (model_type);

CREATE INDEX IF NOT EXISTS "IX_evaluation_runs_started_at"
    ON evaluation_runs (started_at);

-- Read-only verification: mapped columns, PostgreSQL types, lengths, and nullability.
SELECT
    ordinal_position,
    column_name,
    data_type,
    character_maximum_length,
    is_nullable
FROM information_schema.columns
WHERE table_schema = 'public'
  AND table_name = 'evaluation_runs'
ORDER BY ordinal_position;

-- Read-only verification: primary-key constraint.
SELECT
    conname,
    pg_get_constraintdef(oid) AS definition
FROM pg_constraint
WHERE conrelid = 'public.evaluation_runs'::regclass
  AND contype = 'p';

-- Read-only verification: indexes.
SELECT
    indexname,
    indexdef
FROM pg_indexes
WHERE schemaname = 'public'
  AND tablename = 'evaluation_runs'
ORDER BY indexname;
