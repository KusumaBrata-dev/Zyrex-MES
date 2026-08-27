using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ZyrexMES.Api.Common;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Modules.Insights;

public static class InsightsEndpoints
{
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
    }
}
