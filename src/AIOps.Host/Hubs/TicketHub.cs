using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AIOps.Host.Hubs;

/// <summary>
/// Real-time event hub for the operations dashboard. Server→client events are
/// published by ITicketNotifier (Phase 9). Client-callable methods are limited to
/// group membership; mutations (approve/reject) always go through REST endpoints
/// where authorization, idempotency and audit are enforced.
/// </summary>
[Authorize]
public sealed class TicketHub : Hub
{
    public Task JoinTicket(Guid ticketId) => Groups.AddToGroupAsync(Context.ConnectionId, $"ticket-{ticketId}");
    public Task LeaveTicket(Guid ticketId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ticket-{ticketId}");
}
