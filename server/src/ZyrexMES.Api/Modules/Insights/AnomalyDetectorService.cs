using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZyrexMES.Api.Hubs;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Modules.Insights;

public class AnomalyDetectorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<InsightsOptions> _options;
    private readonly ILogger<AnomalyDetectorService> _logger;

    public AnomalyDetectorService(IServiceScopeFactory scopeFactory, IOptions<InsightsOptions> options, ILogger<AnomalyDetectorService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = _options.Value.EvaluationIntervalMinutes;
        if (intervalMinutes <= 0) intervalMinutes = 5;
        var interval = TimeSpan.FromMinutes(intervalMinutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Anomaly detection cycle failed");
            }
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task ExecuteOnceAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var opts = scope.ServiceProvider.GetRequiredService<IOptions<InsightsOptions>>().Value;
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ProductionHub>>();

        var nowUtc = DateTime.UtcNow;
        var hourStart = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc);
        var hourEnd = hourStart.AddHours(1);
        var dedupeSince = nowUtc.AddMinutes(-30);

        var lines = await db.Lines.Where(l => l.IsActive).ToListAsync(ct);
        foreach (var line in lines)
        {
            var stationIds = await db.Stations.Where(s => s.LineId == line.Id && s.IsEnabled).Select(s => s.Id).ToListAsync(ct);
            if (stationIds.Count == 0) continue;

            var outputCurrent = await db.UnitTransactions.CountAsync(t => stationIds.Contains(t.StationId) && t.ScannedAtUtc >= hourStart && t.ScannedAtUtc < hourEnd, ct);
            var ngCurrent = await db.QcResults.CountAsync(q => stationIds.Contains(q.StationId) && q.CheckedAtUtc >= hourStart && q.CheckedAtUtc < hourEnd && q.Verdict == QcVerdict.Fail, ct);
            double? yieldCurrent = outputCurrent == 0 ? null : 100.0 * (outputCurrent - ngCurrent) / outputCurrent;

            // Historical yields same hour last 7 days
            var yields = new List<double>();
            for (var d = 1; d <= 7; d++)
            {
                var hStart = hourStart.AddDays(-d);
                var hEnd = hStart.AddHours(1);
                var outH = await db.UnitTransactions.CountAsync(t => stationIds.Contains(t.StationId) && t.ScannedAtUtc >= hStart && t.ScannedAtUtc < hEnd, ct);
                if (outH == 0) continue;
                var ngH = await db.QcResults.CountAsync(q => stationIds.Contains(q.StationId) && q.CheckedAtUtc >= hStart && q.CheckedAtUtc < hEnd && q.Verdict == QcVerdict.Fail, ct);
                yields.Add(100.0 * (outH - ngH) / outH);
            }
            var baseline = yields.Count > 0 ? yields.Average() : 60.0;

            // Evaluate low_yield
            if (yieldCurrent.HasValue && yieldCurrent.Value < opts.MinYieldPercent)
            {
                var exists = await db.Alerts.AnyAsync(a => a.LineCode == line.Code && a.Type == "low_yield" && a.CreatedAtUtc >= dedupeSince, ct);
                if (!exists)
                {
                    var alert = new Alert
                    {
                        Type = "low_yield",
                        Severity = "critical",
                        Message = $"Yield {yieldCurrent.Value:F1}% below threshold {opts.MinYieldPercent}% on line {line.Code}",
                        LineCode = line.Code,
                        CreatedAtUtc = nowUtc,
                    };
                    db.Alerts.Add(alert);
                    await db.SaveChangesAsync(ct);
                    await hub.Clients.All.SendAsync("AlertRaised", new { id = alert.Id, type = alert.Type, severity = alert.Severity, message = alert.Message, lineCode = alert.LineCode, createdAtUtc = alert.CreatedAtUtc }, ct);
                }
            }

            // Evaluate yield_drop
            if (yieldCurrent.HasValue)
            {
                var drop = baseline - yieldCurrent.Value;
                if (drop >= opts.YieldDropPercent)
                {
                    var exists = await db.Alerts.AnyAsync(a => a.LineCode == line.Code && a.Type == "yield_drop" && a.CreatedAtUtc >= dedupeSince, ct);
                    if (!exists)
                    {
                        var alert = new Alert
                        {
                            Type = "yield_drop",
                            Severity = "warning",
                            Message = $"Yield {yieldCurrent.Value:F1}% dropped {drop:F1}% from baseline {baseline:F1}% on line {line.Code}",
                            LineCode = line.Code,
                            CreatedAtUtc = nowUtc,
                        };
                        db.Alerts.Add(alert);
                        await db.SaveChangesAsync(ct);
                        await hub.Clients.All.SendAsync("AlertRaised", new { id = alert.Id, type = alert.Type, severity = alert.Severity, message = alert.Message, lineCode = alert.LineCode, createdAtUtc = alert.CreatedAtUtc }, ct);
                    }
                }
            }

            // Evaluate ng_spike
            if (ngCurrent >= opts.NgSpikePerHour)
            {
                var exists = await db.Alerts.AnyAsync(a => a.LineCode == line.Code && a.Type == "ng_spike" && a.CreatedAtUtc >= dedupeSince, ct);
                if (!exists)
                {
                    var alert = new Alert
                    {
                        Type = "ng_spike",
                        Severity = "warning",
                        Message = $"NG count {ngCurrent} exceeded threshold {opts.NgSpikePerHour} on line {line.Code}",
                        LineCode = line.Code,
                        CreatedAtUtc = nowUtc,
                    };
                    db.Alerts.Add(alert);
                    await db.SaveChangesAsync(ct);
                    await hub.Clients.All.SendAsync("AlertRaised", new { id = alert.Id, type = alert.Type, severity = alert.Severity, message = alert.Message, lineCode = alert.LineCode, createdAtUtc = alert.CreatedAtUtc }, ct);
                }
            }
        }
    }
}
