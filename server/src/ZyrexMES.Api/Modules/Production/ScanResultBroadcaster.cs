using Microsoft.AspNetCore.SignalR;
using ZyrexMES.Api.Hubs;

namespace ZyrexMES.Api.Modules.Production;

/// <summary>Abstraction over realtime scan-result delivery (testability).</summary>
public interface IScanResultBroadcaster
{
    /// <summary>Notifies the line group that a scan was accepted.</summary>
    Task BroadcastAcceptedAsync(string serialNumber, string stationCode, string lineCode, string result, DateTime atUtc, CancellationToken ct = default);

    /// <summary>Notifies the line group that a scan was rejected with a reason.</summary>
    Task BroadcastRejectedAsync(string serialNumber, string stationCode, string lineCode, string reason, DateTime atUtc, CancellationToken ct = default);

    /// <summary>Raises an operational alert to the line group (e.g. print failures).</summary>
    Task BroadcastAlertAsync(string type, long jobId, string? error, CancellationToken ct = default);
}

/// <summary>SignalR implementation: pushes to the per-line group of ProductionHub.</summary>
public sealed class SignalRScanResultBroadcaster(IHubContext<ProductionHub> hub) : IScanResultBroadcaster
{
    public Task BroadcastAcceptedAsync(string serialNumber, string stationCode, string lineCode, string result, DateTime atUtc, CancellationToken ct = default) =>
        hub.Clients.Group($"line:{lineCode}").SendAsync("ScanAccepted",
            new { serialNumber, stationCode, lineCode, result, atUtc }, ct);

    public Task BroadcastRejectedAsync(string serialNumber, string stationCode, string lineCode, string reason, DateTime atUtc, CancellationToken ct = default) =>
        hub.Clients.Group($"line:{lineCode}").SendAsync("ScanRejected",
            new { serialNumber, stationCode, lineCode, result = "REJECTED", reason, atUtc }, ct);

    public Task BroadcastAlertAsync(string type, long jobId, string? error, CancellationToken ct = default) =>
        hub.Clients.All.SendAsync("AlertRaised", new { type, jobId, error }, ct);
}
