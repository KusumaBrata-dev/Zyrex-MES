using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ZyrexMES.Api.Hubs;

[Authorize]
public class ProductionHub : Hub
{
    // Clients (dashboard/kiosk) join a per-line group for cheap server-side filtering.
    public Task JoinLine(string lineCode) => Groups.AddToGroupAsync(Context.ConnectionId, $"line:{lineCode}");
    public Task LeaveLine(string lineCode) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"line:{lineCode}");
}
