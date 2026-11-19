
const jsonHeaders = { Accept: "application/json" };

export async function loadDashboard(useDevelopmentAdmin) {
    const approvalHeaders = useDevelopmentAdmin
        ? { ...jsonHeaders, "X-Dev-User": "admin" }
        : jsonHeaders;

    const [tickets, approvals, cloud, itsm, network, evaluations] =
        await Promise.all([
            settled("/api/v1/tickets/", jsonHeaders),
            settled("/api/v1/approvals/", approvalHeaders),
            settled("/api/v1/integrations/cloud/status", jsonHeaders),
            settled("/api/v1/integrations/itsm/status", jsonHeaders),
            settled("/api/v1/integrations/network-diagnostics/status", jsonHeaders),
            settled("/api/v1/evaluations/comparison", jsonHeaders)
        ]);

    const ticketRows = Array.isArray(tickets.value)
        ? tickets.value.slice(0, 6).map(ticket => ({
            title: ticket.title || "Untitled incident",
            domain: domainLabel(ticket.domain),
            severity: severityLabel(ticket.severity),
            status: ticketStatusLabel(ticket.status),
            updatedAt: ticket.updatedAt || ticket.createdAt || null
        }))
        : [];

    const approvalRows = Array.isArray(approvals.value)
        ? approvals.value
        : [];

    const evaluationCount =
        typeof evaluations.value?.runCount === "number"
            ? evaluations.value.runCount
            : null;

    return {
        ticketCount: Array.isArray(tickets.value)
            ? tickets.value.length
            : null,
        ticketsAvailable: tickets.ok,
        pendingApprovals: approvals.ok
            ? approvalRows.filter(
                item => approvalLabel(item.status) === "Pending"
            ).length
            : null,
        approvalsAvailable: approvals.ok,
        evaluationRunCount: evaluationCount,
        evaluationAvailable: evaluations.ok,
        cloud: cloud.ok ? provider(cloud.value) : null,
        itsm: itsm.ok ? provider(itsm.value) : null,
        network: network.ok ? provider(network.value) : null,
        tickets: ticketRows,
        hasPartialFailure:
            !tickets.ok ||
            !approvals.ok ||
            !cloud.ok ||
            !itsm.ok ||
            !network.ok ||
            !evaluations.ok
    };
}

async function settled(url, headers) {
    try {
        const response = await fetch(url, {
            credentials: "same-origin",
            headers
        });

        return response.ok
            ? { ok: true, value: await response.json() }
            : { ok: false, value: null };
    } catch {
        return { ok: false, value: null };
    }
}

function domainLabel(value) {
    return enumLabel(value, [
        "Unknown",
        "Network",
        "Identity",
        "Database",
        "Infrastructure"
    ]);
}

function severityLabel(value) {
    return enumLabel(value, [
        "P4",
        "P3",
        "P2",
        "P1"
    ]);
}

function ticketStatusLabel(value) {
    return enumLabel(value, [
        "New",
        "Triaging",
        "Triaged",
        "Investigating",
        "Knowledge retrieval",
        "Resolution proposed",
        "Awaiting approval",
        "Action executing",
        "Verifying",
        "Resolved",
        "Escalating",
        "Escalated",
        "Failed"
    ]);
}

function approvalLabel(value) {
    return enumLabel(value, [
        "Pending",
        "Approved",
        "Rejected",
        "Expired"
    ]);
}

function enumLabel(value, labels) {
    if (typeof value === "number") {
        return Number.isInteger(value) && value >= 0 && value < labels.length
            ? labels[value]
            : "Not reported";
    }

    return typeof value === "string" && value.trim()
        ? value
        : "Not reported";
}

function provider(value) {
    const mode = value.mode || "Unavailable";
    const connectionStatus = value.connectionStatus || null;

    return {
        displayName: value.displayName || value.activeProvider || "Unavailable",
        mode,
        connectionStatus,
        health: providerHealth(connectionStatus, mode)
    };
}

function providerHealth(connectionStatus, mode) {
    const normalize = value =>
        String(value || "").replace(/[^a-z]/gi, "").toLowerCase();

    const status = normalize(connectionStatus);
    const providerMode = normalize(mode);

    if (["simulated", "connected"].includes(status)) {
        return "healthy";
    }

    if (status === "notconfigured") {
        return "warning";
    }

    if (
        [
            "unavailable",
            "authenticationfailed",
            "permissiondenied",
            "timeout",
            "providererror"
        ].includes(status)
    ) {
        return "error";
    }

    if (providerMode === "simulated") {
        return "healthy";
    }

    return "unknown";
}
