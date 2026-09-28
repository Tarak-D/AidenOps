-- Expand the persisted action status range to include ActionStatus.Cancelled (7).
ALTER TABLE action_executions
    DROP CONSTRAINT IF EXISTS ck_action_executions_status;

ALTER TABLE action_executions
    ADD CONSTRAINT ck_action_executions_status
    CHECK (status BETWEEN 0 AND 7);
