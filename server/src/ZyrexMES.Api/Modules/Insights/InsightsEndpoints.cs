using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ZyrexMES.Api.Common;
using ZyrexMES.Api.Modules.Reports;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Modules.Insights;

public static class InsightsEndpoints
{
    private static readonly TimeZoneInfo WibZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");

    public static void MapInsightsEndpoints(this WebApplication app)
    {
        var alerts = app.MapGroup("/api/alerts");
        alerts.MapGet("", async (bool? unackedOnly, int? take, AppDbContext db) =>
        {
            var q = db.Alerts.AsQueryable();
            if (unackedOnly == true) q = q.Where(a => a.AcknowledgedAtUtc == null);
            var limit = take is null ? 50 : Math.Clamp(take.Value, 1, 200);
            var items = await q.OrderByDescending(a => a.CreatedAtUtc).Take(limit).ToListAsync();
            return Results.Ok(items.Select(a => new
            {
                id = a.Id,
                type = a.Type,
                severity = a.Severity,
                message = a.Message,
                lineCode = a.LineCode,
                createdAtUtc = a.CreatedAtUtc,
                acknowledgedAtUtc = a.AcknowledgedAtUtc,
            }));
        }).RequireAuthorization();

        alerts.MapPost("/{id:long}/ack", async (long id, AppDbContext db) =>
        {
            var alert = await db.Alerts.FindAsync(id);
            if (alert is null) return Results.NotFound(new { error = "alert not found" });
            if (alert.AcknowledgedAtUtc is null)
            {
                alert.AcknowledgedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            return Results.Ok(new
            {
                id = alert.Id,
                type = alert.Type,
                severity = alert.Severity,
                message = alert.Message,
                lineCode = alert.LineCode,
                createdAtUtc = alert.CreatedAtUtc,
                acknowledgedAtUtc = alert.AcknowledgedAtUtc,
            });
        }).RequireRoles(Roles.Supervisor, Roles.Admin);

        app.MapGet("/api/insights/thresholds", (IOptions<InsightsOptions> opts) =>
        {
            var v = opts.Value;
            return Results.Ok(new
            {
                minYieldPercent = v.MinYieldPercent,
                yieldDropPercent = v.YieldDropPercent,
                ngSpikePerHour = v.NgSpikePerHour,
                evaluationIntervalMinutes = v.EvaluationIntervalMinutes,
            });
        }).RequireAuthorization();

        var insights = app.MapGroup("/api/insights");
        
        insights.MapGet("/yield-trend", async (AppDbContext db, int days = 7, string? lineCode = null) =>
        {
            days = Math.Clamp(days, 1, 31);
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(ReportDtos.WibOffsetHours));
            var dates = Enumerable.Range(0, days).Select(i => today.AddDays(-i)).Reverse().ToList();

            var lineIds = string.IsNullOrEmpty(lineCode)
                ? await db.Lines.Select(l => l.Id).ToListAsync()
                : await db.Lines.Where(l => l.Code == lineCode).Select(l => l.Id).ToListAsync();

            var stationIds = await db.Stations
                .Where(s => lineIds.Contains(s.LineId))
                .Select(s => s.Id)
                .ToListAsync();

            var results = new List<object>();
            foreach (var date in dates)
            {
                var (start, end) = ReportDtos.WibRange(date);
                var output = await db.UnitTransactions
                    .Where(t => stationIds.Contains(t.StationId) && t.ScannedAtUtc >= start && t.ScannedAtUtc < end)
                    .CountAsync();
                var ng = await db.QcResults
                    .Where(q => stationIds.Contains(q.StationId) && q.Verdict == QcVerdict.Fail && q.CheckedAtUtc >= start && q.CheckedAtUtc < end)
                    .CountAsync();
                var yield = output == 0 ? (double?)null : Math.Round(100.0 * (output - ng) / output, 1);
                results.Add(new { date = date.ToString("yyyy-MM-dd"), output, ng, yieldPercent = yield });
            }
            return Results.Ok(new { points = results });
        }).RequireAuthorization();

        insights.MapGet("/ng-pareto", async (AppDbContext db, DateOnly from, DateOnly to, string? lineCode = null) =>
        {
            var lineIds = string.IsNullOrEmpty(lineCode)
                ? await db.Lines.Select(l => l.Id).ToListAsync()
                : await db.Lines.Where(l => l.Code == lineCode).Select(l => l.Id).ToListAsync();

            var stationIds = await db.Stations
                .Where(s => lineIds.Contains(s.LineId))
                .Select(s => s.Id)
                .ToListAsync();

            var (fromUtc, _) = ReportDtos.WibRange(from);
            var (_, toUtc) = ReportDtos.WibRange(to);
            toUtc = toUtc.AddDays(1);

            var items = await db.QcResults
                .Where(q => stationIds.Contains(q.StationId) && q.Verdict == QcVerdict.Fail && q.CheckedAtUtc >= fromUtc && q.CheckedAtUtc < toUtc)
                .GroupBy(q => q.NgCodeId)
                .Select(g => new { NgCodeId = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToListAsync();

            var ngCodeIds = items.Select(i => i.NgCodeId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
            var ngCodes = await db.NgCodes
                .Where(n => ngCodeIds.Contains(n.Id))
                .ToDictionaryAsync(n => n.Id, n => n.Code);

            var result = items.Select(i => new
            {
                ngCode = i.NgCodeId.HasValue ? ngCodes.GetValueOrDefault(i.NgCodeId.Value, "Unknown") : "Unknown",
                count = i.Count
            }).ToList();

            return Results.Ok(new { items = result });
        }).RequireAuthorization();
    }
}