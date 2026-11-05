-- Phase 15 Batch 7: record evaluation provenance for revision comparisons.
-- Apply to an isolated evaluation database; no run data is modified.

ALTER TABLE evaluation_runs
    ADD COLUMN IF NOT EXISTS source_revision character varying(40);

ALTER TABLE evaluation_runs
    ADD COLUMN IF NOT EXISTS dataset_sha256 character varying(64);

ALTER TABLE evaluation_runs
    ADD COLUMN IF NOT EXISTS knowledge_corpus_sha256 character varying(64);

SELECT
    column_name,
    data_type,
    character_maximum_length,
    is_nullable
FROM information_schema.columns
WHERE table_schema = 'public'
  AND table_name = 'evaluation_runs'
  AND column_name IN (
      'source_revision',
      'dataset_sha256',
      'knowledge_corpus_sha256')
ORDER BY column_name;
