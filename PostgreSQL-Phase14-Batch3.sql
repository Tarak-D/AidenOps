-- Persisted, non-secret selection for the active network diagnostic category/provider.
CREATE TABLE IF NOT EXISTS network_diagnostic_provider_selections
(
    id character varying(100) PRIMARY KEY,
    category character varying(100) NOT NULL
        CHECK (category IN ('Vpn', 'GeneralNetwork')),
    provider character varying(100) NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by character varying(200) NOT NULL,
    CHECK (
        (category = 'Vpn' AND provider IN ('Cisco', 'PaloAltoNetworks', 'Fortinet', 'Cloudflare', 'Citrix', 'OpenVPN', 'Simulated'))
        OR
        (category = 'GeneralNetwork' AND provider IN ('CiscoThousandEyes', 'Datadog', 'Dynatrace', 'Zabbix', 'PRTG', 'Simulated'))
    )
);
