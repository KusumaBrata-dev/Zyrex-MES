using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using ZyrexMES.Api.Common;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Legacy;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Modules.Migration;

public sealed record ReconciliationRow(string Table, int LegacyCount, int CoreCount, bool Match);

public sealed record ReconciliationMismatch(string Sn, string Field, string Expected, string Actual);

public sealed class SampleRequest
{
    public int Count { get; set; } = 100;
}

public static class ReconciliationEndpoints
{
    private const string CodePrefix = LegacyImportService.CodePrefix;
    private const string LegacySource = LegacyImportService.LegacySource;
    private const string TxNotesPrefix = "LEGACY:";
    private const int MaxMismatches = 20;
    private const int MaxSampleCount = 500;

    public static void MapReconciliationEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/migration/reconciliation");

        g.MapGet("/report", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await BuildReportAsync(db, ct)))
         .RequireRoles(Roles.Supervisor, Roles.Admin);

        g.MapPost("/sample", async (SampleRequest req, AppDbContext db, CancellationToken ct) =>
        {
            if (req.Count is < 1 or > MaxSampleCount)
            {
                return Results.BadRequest(new { error = $"Count must be between 1 and {MaxSampleCount}." });
            }
            return Results.Ok(await BuildSampleAsync(db, req.Count, ct));
        }).RequireRoles(Roles.Supervisor, Roles.Admin);
    }

    // --- report ---------------------------------------------------------------

    private static async Task<List<ReconciliationRow>> BuildReportAsync(AppDbContext db, CancellationToken ct)
    {
        var rows = new List<ReconciliationRow>(5);

        int legacy = await db.LegacyLineSnapshots.CountAsync(ct);
        int core = await db.Lines.CountAsync(l => l.Source == LegacySource, ct);
        rows.Add(Row("lines", legacy, core));

        legacy = await db.LegacyStationSnapshots.CountAsync(ct);
        core = await db.Stations.CountAsync(s => s.Source == LegacySource, ct);
        rows.Add(Row("stations", legacy, core));

        legacy = await db.LegacyProductSnapshots.CountAsync(ct);
        core = await db.Products.CountAsync(p => p.Source == LegacySource, ct);
        rows.Add(Row("products", legacy, core));

        legacy = await db.LegacyTransactionSnapshots.CountAsync(ct);
        core = await db.UnitTransactions.CountAsync(t => t.Notes != null && t.Notes.StartsWith(TxNotesPrefix), ct);
        rows.Add(Row("transactions", legacy, core));

        // Routing groups: one staging SKU ↔ one imported routing entity.
        legacy = await db.LegacyRoutingSnapshots.Select(r => r.LegacySku).Distinct().CountAsync(ct);
        core = await db.Routings.CountAsync(r => r.Source == LegacySource, ct);
        rows.Add(Row("routings", legacy, core));

        return rows;
    }

    private static ReconciliationRow Row(string table, int legacyCount, int coreCount) =>
        new(table, legacyCount, coreCount, legacyCount == coreCount);

    // --- random sample verification ---------------------------------------------

    private static async Task<object> BuildSampleAsync(AppDbContext db, int count, CancellationToken ct)
    {
        // Set-based random sample (SQL ORDER BY random() LIMIT n).
        var samples = await db.LegacyTransactionSnapshots
            .OrderBy(s => EF.Functions.Random())
            .Take(count)
            .ToListAsync(ct);

        var sns = samples.Select(s => s.SN).Distinct().ToList();
        var units = await db.Units
            .Where(u => sns.Contains(u.SerialNumber))
            .ToDictionaryAsync(u => u.SerialNumber, u => u.Id, ct);

        var stationCodes = samples.Select(s => CodePrefix + s.StationCode).Distinct().ToList();
        var stations = await db.Stations
            .Where(st => stationCodes.Contains(st.Code))
            .ToDictionaryAsync(st => st.Code, st => st.Id, ct);

        var unitIds = units.Values.ToList();
        var txs = await db.UnitTransactions
            .Where(t => unitIds.Contains(t.UnitId))
            .ToListAsync(ct);

        var mismatches = new List<ReconciliationMismatch>();
        var mismatchedTotal = 0;
        foreach (var s in samples)
        {
            // Count independently of the capped example list: every sampled
            // snapshot must be classified even once the list hits its cap.
            if (VerifySnapshot(db, s, units, stations, txs, mismatches))
            {
                mismatchedTotal++;
            }
        }

        return new
        {
            sampled = samples.Count,
            matched = samples.Count - mismatchedTotal,
            mismatchedTotal,
            mismatches,
        };
    }

    /// <summary>Verifies one sampled snapshot against core rows. Returns true
    /// when the snapshot is a mismatch; appends an example to
    /// <paramref name="mismatches"/> while that list is below its cap.</summary>
    private static bool VerifySnapshot(
        AppDbContext db,
        LegacyTransactionSnapshot s,
        Dictionary<string, int> units,
        Dictionary<string, int> stations,
        List<UnitTransaction> txs,
        List<ReconciliationMismatch> mismatches)
    {
        if (!units.TryGetValue(s.SN, out var unitId))
        {
            Add(mismatches, s.SN, "unit", s.SN, "not found");
            return true;
        }
        if (!stations.TryGetValue(CodePrefix + s.StationCode, out var stationId))
        {
            Add(mismatches, s.SN, "station", CodePrefix + s.StationCode, "not found");
            return true;
        }

        var expectedResult = s.ResultChar == "P" ? QcVerdict.Pass : QcVerdict.Fail;
        var candidates = txs.Where(t => t.UnitId == unitId && t.ScannedAtUtc == s.ScannedAtUtc).ToList();
        if (candidates.Count == 0)
        {
            Add(mismatches, s.SN, "transaction", $"{s.StationCode}@{s.ScannedAtUtc:O}", "not found");
            return true;
        }

        var match = candidates.Any(t => t.StationId == stationId && t.Result == expectedResult);
        if (!match)
        {
            var actual = candidates[0];
            Add(mismatches, s.SN, "result/station",
                $"{s.ResultChar}/{s.StationCode}",
                $"{(actual.Result == QcVerdict.Pass ? "P" : "F")}/{db.Stations.Where(st => st.Id == actual.StationId).Select(st => st.Code).FirstOrDefault() ?? "?"}");
            return true;
        }

        return false;
    }

    private static void Add(List<ReconciliationMismatch> mismatches, string sn, string field, string expected, string actual)
    {
        if (mismatches.Count < MaxMismatches)
        {
            mismatches.Add(new ReconciliationMismatch(sn, field, expected, actual));
        }
    }
}
