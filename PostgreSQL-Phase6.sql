CREATE TABLE IF NOT EXISTS action_executions
(
    id uuid NOT NULL,
    ticket_id uuid NOT NULL,
    tool_name varchar(200) NOT NULL,
    arguments_json text NOT NULL,
    risk integer NOT NULL,
    requires_approval boolean NOT NULL,
    proposed_by varchar(200) NOT NULL,
    reason text NULL,
    status integer NOT NULL,
    result_summary text NULL,
    created_at timestamp with time zone NOT NULL,
    executed_at timestamp with time zone NULL,

    CONSTRAINT action_executions_pkey PRIMARY KEY (id),
    CONSTRAINT ck_action_executions_risk CHECK (risk BETWEEN 0 AND 2),
    CONSTRAINT ck_action_executions_status CHECK (status BETWEEN 0 AND 6)
);

CREATE INDEX IF NOT EXISTS ix_action_executions_ticket_id
    ON action_executions (ticket_id);

CREATE INDEX IF NOT EXISTS ix_action_executions_status
    ON action_executions (status);

CREATE INDEX IF NOT EXISTS ix_action_executions_created_at
    ON action_executions (created_at);


CREATE TABLE IF NOT EXISTS approval_requests
(
    id uuid NOT NULL,
    action_execution_id uuid NOT NULL,
    ticket_id uuid NOT NULL,
    requested_by varchar(200) NOT NULL,
    justification text NOT NULL,
    status integer NOT NULL,
    created_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    decided_by varchar(200) NULL,
    decided_at timestamp with time zone NULL,
    decision_comment text NULL,

    CONSTRAINT approval_requests_pkey PRIMARY KEY (id),

    CONSTRAINT fk_approval_requests_action_execution
        FOREIGN KEY (action_execution_id)
        REFERENCES action_executions (id)
        ON DELETE RESTRICT,

    CONSTRAINT ck_approval_requests_status
        CHECK (status BETWEEN 0 AND 3),

    CONSTRAINT uq_approval_requests_action_execution
        UNIQUE (action_execution_id)
);

CREATE INDEX IF NOT EXISTS ix_approval_requests_ticket_id
    ON approval_requests (ticket_id);

CREATE INDEX IF NOT EXISTS ix_approval_requests_status
    ON approval_requests (status);

CREATE INDEX IF NOT EXISTS ix_approval_requests_expires_at
    ON approval_requests (expires_at);
