using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using ZyrexMES.Api.Common;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Domain.Enums;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Modules.Reports;

public static class ExportEndpoints
{
    public static void MapExportEndpoints(this WebApplication app)
    {
        var exports = app.MapGroup("/api/export");

        exports.MapGet("/ng-list.xlsx", async (DateOnly from, DateOnly to, int? stationId, AppDbContext db) =>
        {
            var (fromUtc, _) = ReportDtos.WibRange(from);
            var (_, toUtc) = ReportDtos.WibRange(to);
            toUtc = toUtc.AddDays(1);

            var query = db.QcResults
                .Where(q => q.Verdict == QcVerdict.Fail
                    && q.CheckedAtUtc >= fromUtc && q.CheckedAtUtc < toUtc);

            if (stationId.HasValue)
                query = query.Where(q => q.StationId == stationId.Value);

            var items = await query
                .OrderByDescending(q => q.CheckedAtUtc)
                .Select(q => new
                {
                    q.Unit.SerialNumber,
                    StationCode = q.Station.Code,
                    NgCode = q.NgCode != null ? q.NgCode.Code : "Unknown",
                    q.Notes,
                    CheckedAtUtc = q.CheckedAtUtc
                })
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("NG List");
            worksheet.Cell(1, 1).Value = "SN";
            worksheet.Cell(1, 2).Value = "Station";
            worksheet.Cell(1, 3).Value = "NG Code";
            worksheet.Cell(1, 4).Value = "Notes";
            worksheet.Cell(1, 5).Value = "Checked At (WIB)";

            var wibZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                worksheet.Cell(i + 2, 1).Value = item.SerialNumber;
                worksheet.Cell(i + 2, 2).Value = item.StationCode;
                worksheet.Cell(i + 2, 3).Value = item.NgCode;
                worksheet.Cell(i + 2, 4).Value = item.Notes ?? "";
                worksheet.Cell(i + 2, 5).Value = TimeZoneInfo.ConvertTimeFromUtc(item.CheckedAtUtc, wibZone).ToString("yyyy-MM-dd HH:mm:ss");
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;

            return Results.File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"ng-list-{DateTime.UtcNow:yyyyMMdd}.xlsx");
        }).RequireRoles(Roles.Leader, Roles.Supervisor, Roles.Admin);

        exports.MapGet("/station-summary.xlsx", async (DateOnly date, AppDbContext db) =>
        {
            var (start, end) = ReportDtos.WibRange(date);

            var stationSummaries = await db.Stations
                .Where(s => s.IsEnabled)
                .Select(s => new
                {
                    s.Code,
                    s.Name,
                    Output = db.UnitTransactions
                        .Where(t => t.StationId == s.Id && t.ScannedAtUtc >= start && t.ScannedAtUtc < end)
                        .Count(),
                    Ng = db.QcResults
                        .Where(q => q.StationId == s.Id && q.Verdict == QcVerdict.Fail
                            && q.CheckedAtUtc >= start && q.CheckedAtUtc < end)
                        .Count(),
                })
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Station Summary");
            worksheet.Cell(1, 1).Value = "Station Code";
            worksheet.Cell(1, 2).Value = "Station Name";
            worksheet.Cell(1, 3).Value = "Output";
            worksheet.Cell(1, 4).Value = "NG";
            worksheet.Cell(1, 5).Value = "Yield %";

            for (int i = 0; i < stationSummaries.Count; i++)
            {
                var item = stationSummaries[i];
                double yield = item.Output == 0 ? 0 : Math.Round(100.0 * (item.Output - item.Ng) / item.Output, 1);
                worksheet.Cell(i + 2, 1).Value = item.Code;
                worksheet.Cell(i + 2, 2).Value = item.Name;
                worksheet.Cell(i + 2, 3).Value = item.Output;
                worksheet.Cell(i + 2, 4).Value = item.Ng;
                worksheet.Cell(i + 2, 5).Value = yield;
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;

            return Results.File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"station-summary-{date:yyyyMMdd}.xlsx");
        }).RequireRoles(Roles.Leader, Roles.Supervisor, Roles.Admin);
    }
}