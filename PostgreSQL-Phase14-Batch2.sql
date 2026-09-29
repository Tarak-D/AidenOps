-- Persisted, non-secret selection for the active ITSM provider.
CREATE TABLE IF NOT EXISTS integration_provider_selections
(
    id character varying(100) PRIMARY KEY,
    provider character varying(100) NOT NULL
        CHECK (provider IN ('Simulated', 'ServiceNow', 'JiraServiceManagement', 'Zendesk')),
    updated_at timestamp with time zone NOT NULL,
    updated_by character varying(200) NOT NULL
);
