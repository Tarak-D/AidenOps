const reporterEmail = "portfolio.demo@example.local";

export async function loadPersistedEvaluationResults() {
    return get("/api/v1/evaluations/comparison");
}

export async function runVpnSandboxDemo() {
    const network = await get("/api/v1/integrations/network-diagnostics/status");
    if (network.category !== "Vpn" || network.activeProvider !== "Simulated" || network.mode !== "Simulated") throw new Error("The VPN sandbox demo requires the server-side VPN provider to be Simulated.");
    const ticket = await createTicket("VPN connectivity failure — portfolio sandbox", "A demo user cannot connect to the VPN. Investigate the connectivity issue and run a read-only VPN diagnostic using the simulated sandbox provider.");
    const run = await startRun(ticket);
    return { outcome: label(run.outcome, ["Resolved", "Awaiting approval", "Escalated", "Failed", "Proposal created"]), proposedTool: run.proposal?.toolName || null, summary: run.resolutionSummary || run.escalationSummary || run.error || null, ticketApiUrl: `/api/v1/tickets/${ticket.id}` };
}

export async function startApprovalSandboxDemo(devAdmin) {
    const cloud = await get("/api/v1/integrations/cloud/status");
    if (cloud.activeProvider !== "Simulated" || cloud.mode !== "Simulated" || cloud.isProduction) throw new Error("The approval sandbox demo requires the server-side cloud provider to be Simulated.");
    const ticket = await createTicket("Restart simulated EC2 instance i-portfolio-demo-001", "The sandbox EC2 server is unavailable. Restart instance i-portfolio-demo-001 after human approval.", "PORTFOLIO-APPROVAL-SANDBOX");
    const run = await startRun(ticket);
    const actionId = run.proposal?.actionExecutionId;
    if (!(run.outcome === 1 || run.outcome === "AwaitingApproval") || !actionId || run.proposal?.toolName !== "Cloud.RestartInstance") throw new Error("The server did not create the expected approval-required simulated action. No decision was sent.");
    const approvals = await get("/api/v1/approvals/", authHeaders(devAdmin));
    const approval = approvals.find(x => x.actionExecutionId === actionId);
    if (!approval) throw new Error("The proposed action has no persisted pending approval request.");
    return result(ticket.id, actionId, approval, run.proposal, devAdmin);
}

export async function decideApprovalSandboxDemo(ticketId, actionId, approvalId, approve, devAdmin) {
    const approval = await send(`/api/v1/approvals/${approvalId}/decision`, { approve, comment: approve ? "Approved for the portfolio simulated sandbox demo." : "Rejected for the portfolio simulated sandbox demo." }, authHeaders(devAdmin));
    return result(ticketId, actionId, approval, null, devAdmin);
}

export async function refreshApprovalSandboxDemo(ticketId, actionId, approvalId, devAdmin) {
    return result(ticketId, actionId, await get(`/api/v1/approvals/${approvalId}`, authHeaders(devAdmin)), null, devAdmin);
}

async function createTicket(title, description, externalRef = null) {
    return send("/api/v1/tickets/", { title, description, reporterEmail, reporterName: "Portfolio Demo", externalRef });
}

async function startRun(ticket) {
    return send(`/api/v1/tickets/${ticket.id}/agent-runs`, { correlationId: crypto.randomUUID(), ticket: { ticketId: ticket.id, externalRef: ticket.externalRef || "PORTFOLIO-SANDBOX", title: ticket.title, description: ticket.description, reporterEmail: ticket.reporterEmail, domain: ticket.domain, severity: ticket.severity, status: ticket.status }, allowedTools: [], triageConfidenceThreshold: 0.6, maxAttempts: 2 });
}

async function result(ticketId, actionId, approval, proposal, devAdmin) {
    const ticket = await get(`/api/v1/tickets/${ticketId}`);
    let timeline = [], auditError = null;
    try { timeline = await loadTimeline(ticket, actionId, approval.id, devAdmin); }
    catch { auditError = "The approval decision is persisted, but the audit timeline is unavailable to the current identity. An administrator can refresh it."; }
    const approvalStatus = label(approval.status, ["Pending", "Approved", "Rejected", "Expired"]);
    const events = new Set(timeline.map(x => x.eventType));
    const executionState = events.has("ActionExecutionSucceeded") ? "Simulated execution completed" : events.has("ActionExecutionFailed") ? "Execution failed" : events.has("ApprovalRejected") ? "Rejected — execution was not started" : events.has("ApprovalExpired") ? "Expired — execution was not started" : approvalStatus === "Approved" ? "Approved — awaiting persisted execution result" : "Awaiting approval";
    return { ticketId, actionExecutionId: actionId, approvalId: approval.id, proposedTool: proposal?.toolName || "Cloud.RestartInstance", approvalStatus, explanation: "The server registered Cloud.RestartInstance as a Moderate-risk infrastructure change, so an authorized human decision is required before execution.", executionState, canDecide: approvalStatus === "Pending", auditError, timeline, ticketApiUrl: `/api/v1/tickets/${ticketId}`, approvalApiUrl: `/api/v1/approvals/${approval.id}`, ticketAuditApiUrl: `/api/v1/audit/ticket/${compact(ticketId)}`, actionAuditApiUrl: `/api/v1/audit/action_execution/${compact(actionId)}`, approvalAuditApiUrl: `/api/v1/audit/approval_request/${compact(approval.id)}` };
}

async function loadTimeline(ticket, actionId, approvalId, devAdmin) {
    const headers = authHeaders(devAdmin);
    const [ticketAudit, actionAudit, approvalAudit] = await Promise.all([
        get(`/api/v1/audit/ticket/${compact(ticket.id)}`, headers), get(`/api/v1/audit/action_execution/${compact(actionId)}`, headers), get(`/api/v1/audit/approval_request/${compact(approvalId)}`, headers)]);
    const activity = (ticket.activity || []).map(x => ({ occurredAt: x.occurredAt, at: time(x.occurredAt), eventType: x.kind || "Ticket activity", summary: x.summary || "Persisted ticket activity." }));
    const audit = [...ticketAudit, ...actionAudit, ...approvalAudit].map(x => ({ occurredAt: x.occurredAt, at: time(x.occurredAt), eventType: x.record.eventType, summary: auditSummary(x.record.eventType) }));
    return [...activity, ...audit].sort((a, b) => new Date(a.occurredAt) - new Date(b.occurredAt));
}

function auditSummary(type) { return ({ TicketCreated: "The incident was created.", AgentRunStarted: "The agent-run result was recorded.", ToolProposalValidated: "The server validated the proposal against the registered schema and policy.", ApprovalRequested: "A persisted human approval request was created.", ApprovalApproved: "An authorized approver accepted the action.", ApprovalRejected: "An authorized approver rejected the action.", ApprovalExpired: "The approval request expired before execution.", ActionExecutionStarted: "The control plane started the approved action through ToolExecutor.", ActionExecutionSucceeded: "The simulated provider reported a successful execution.", ActionExecutionFailed: "The control plane recorded an execution failure.", AgentResumeRequested: "The control plane requested a server-authoritative resume.", AgentResumeAccepted: "Persisted state was accepted for resume.", AgentRunResumed: "The agent processed the persisted execution result." })[type] || "Persisted audit event."; }
function authHeaders(devAdmin) { const headers = { "Content-Type": "application/json", Accept: "application/json" }; if (devAdmin) headers["X-Dev-User"] = "admin"; return headers; }
async function get(url, headers = { Accept: "application/json" }) { return request(url, { method: "GET", headers }); }
async function send(url, body, headers = { "Content-Type": "application/json", Accept: "application/json" }) { return request(url, { method: "POST", headers, body: JSON.stringify(body) }); }
async function request(url, options) { const response = await fetch(url, { ...options, credentials: "same-origin" }); if (response.ok) return response.json(); let message = "The control plane rejected the request."; try { const problem = await response.json(); message = problem.detail || problem.title || firstError(problem.errors) || message; } catch {} throw new Error(`${message} (HTTP ${response.status})`); }
function firstError(errors) { if (!errors) return null; const value = Object.values(errors).find(x => Array.isArray(x) && x.length); return value?.[0] || null; }
function compact(value) { return value.replaceAll("-", ""); }
function time(value) { return new Date(value).toLocaleString(); }
function label(value, labels) { return typeof value === "number" && value >= 0 && value < labels.length ? labels[value] : value || "Not reported"; }
