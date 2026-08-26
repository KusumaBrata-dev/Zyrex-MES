using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ZyrexMES.Api.Common;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Modules.Reports;

public static class ReportsEndpoints
{
    /// <summary>Station counts as "active" while its last scan is younger than this.</summary>
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(30);
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public static void MapReportsEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/reports");

        g.MapGet("/station-summary", async (int stationId, DateOnly date, AppDbContext db) =>
        {
            var station = await db.Stations.FindAsync(stationId);
            if (station is null)
                return Results.NotFound(new { error = "station not found" });
            var (startUtc, endUtc) = ReportDtos.WibRange(date);
            var output = await db.UnitTransactions
                .CountAsync(t => t.StationId == stationId && t.ScannedAtUtc >= startUtc && t.ScannedAtUtc < endUtc);
            var ng = await db.QcResults
                .CountAsync(q => q.StationId == stationId && q.Verdict == QcVerdict.Fail
                                 && q.CheckedAtUtc >= startUtc && q.CheckedAtUtc < endUtc);
            double? yieldPercent = output == 0
                ? null
                : Math.Round(100.0 * (output - ng) / output, 1);
            return Results.Ok(new StationSummaryDto(stationId, station.Code, output, ng, yieldPercent));
        }).RequireAuthorization();

        g.MapGet("/ng-list", async (DateOnly date, int? stationId, AppDbContext db, int page = 1, int pageSize = DefaultPageSize) =>
        {
            if (page < 1) page = 1;
            pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
            var (startUtc, endUtc) = ReportDtos.WibRange(date);
            var fails = db.QcResults
                .Where(q => q.Verdict == QcVerdict.Fail && q.CheckedAtUtc >= startUtc && q.CheckedAtUtc < endUtc);
            if (stationId is not null)
                fails = fails.Where(q => q.StationId == stationId);
            var total = await fails.CountAsync();
            var items = await (from qr in fails
                               join st in db.Stations on qr.StationId equals st.Id
                               join u in db.Units on qr.UnitId equals u.Id
                               orderby qr.CheckedAtUtc descending
                               select new NgListItemDto(
                                   u.SerialNumber,
                                   st.Code,
                                   qr.NgCode!.Code,
                                   qr.Notes,
                                   qr.CheckedAtUtc))
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            return Results.Ok(new NgListDto(total, page, items));
        }).RequireAuthorization();

        g.MapGet("/line-grid", async (AppDbContext db) =>
        {
            var todayWib = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(ReportDtos.WibOffsetHours));
            var (startUtc, endUtc) = ReportDtos.WibRange(todayWib);

            var txByStation = await db.UnitTransactions
                .Where(t => t.ScannedAtUtc >= startUtc && t.ScannedAtUtc < endUtc)
                .GroupBy(t => t.StationId)
                .Select(g => new { StationId = g.Key, Output = g.Count(), LastEvent = g.Max(t => t.ScannedAtUtc) })
                .ToDictionaryAsync(x => x.StationId);
            var ngByStation = await db.QcResults
                .Where(q => q.Verdict == QcVerdict.Fail && q.CheckedAtUtc >= startUtc && q.CheckedAtUtc < endUtc)
                .GroupBy(q => q.StationId)
                .Select(g => new { StationId = g.Key, Ng = g.Count() })
                .ToDictionaryAsync(x => x.StationId);

            var lines = await db.Lines
                .Where(l => l.IsActive)
                .OrderBy(l => l.Code)
                .Select(l => new
                {
                    l.Code,
                    Stations = l.Stations.Where(s => s.IsEnabled).OrderBy(s => s.Code)
                        .Select(s => new { s.Id, s.Code, s.Name }).ToList(),
                })
                .ToListAsync();

            var nowUtc = DateTime.UtcNow;
            var result = lines.Select(l => new LineGridLineDto(
                l.Code,
                l.Stations.Select(s =>
                {
                    txByStation.TryGetValue(s.Id, out var tx);
                    ngByStation.TryGetValue(s.Id, out var ng);
                    var lastEvent = tx?.LastEvent;
                    var status = lastEvent is not null && nowUtc - lastEvent.Value <= ActiveWindow
                        ? "active"
                        : "idle";
                    return new LineGridStationDto(s.Id, s.Code, s.Name, tx?.Output ?? 0, ng?.Ng ?? 0, lastEvent, status);
                }).ToList())).ToList();
            return Results.Ok(new LineGridDto(result));
        }).RequireAuthorization();
    }
}
