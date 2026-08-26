using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ZyrexMES.Domain.Entities;

namespace ZyrexMES.Api.Tests;

/// <summary>Phase 2 end-to-end smoke: staging seed → master import → transaction
/// import → reconciliation → one production scan → one QC fail (repair queue).
/// ZERO-DISTURBANCE: no legacy server is contacted; staging is seeded locally.</summary>
public class Phase2SmokeTests : IClassFixture<CustomWebAppFactory>, IDisposable
{
    private const string LinePrefix = "LINE-SM";
    private const string StationPrefix = "ST-SM";
    private const string SkuPrefix = "SKU-SM";
    private const string SnPrefix = "SN-SM";
    private const string NgCode = "NG-SMOKE";

    private readonly CustomWebAppFactory _factory;
    private readonly HttpClient _anon;

    public Phase2SmokeTests(CustomWebAppFactory factory)
    {
        _factory = factory;
        _anon = Prepare(factory);
    }

    private static HttpClient Prepare(CustomWebAppFactory factory)
    {
        // Full migration-data wipe for a known-clean baseline (counts asserted
        // below are absolute). Deletion order respects FKs; phases commit.
        using var db = factory.CreateDb();
        db.Database.Migrate();

        // Units created by the transaction importer carry arbitrary serials —
        // select by legacy ProductId as well; QC/repair rows cascade with units.
        var legacyProductIds = db.Products
            .Where(x => x.Source == "Legacy" || x.Sku.StartsWith("LGCY-" + SkuPrefix))
            .Select(x => x.Id).ToList();
        var affectedUnitIds = db.Units
            .Where(u => u.SerialNumber.StartsWith(SnPrefix) || legacyProductIds.Contains(u.ProductId))
            .Select(u => u.Id).ToList();
        db.UnitTransactions.Where(t => affectedUnitIds.Contains(t.UnitId)).ExecuteDelete();
        db.Units.Where(u => affectedUnitIds.Contains(u.Id)).ExecuteDelete();
        foreach (var n in db.NgCodes.Where(n => n.Code == NgCode).ToList())
            db.NgCodes.Remove(n);
        db.SaveChanges();
        db.SaveChanges();
        var legacyStationIds = db.Stations.Where(x => x.Source == "Legacy" || x.Code.StartsWith("LGCY-" + StationPrefix)).Select(x => x.Id).ToList();
        db.RoutingSteps.Where(x => legacyStationIds.Contains(x.StationId)).ExecuteDelete();
        db.Routings.Where(x => x.Source == "Legacy").ExecuteDelete();
        db.Products.Where(x => x.Source == "Legacy" || x.Sku.StartsWith("LGCY-" + SkuPrefix)).ExecuteDelete();
        db.Stations.Where(x => x.Source == "Legacy" || x.Code.StartsWith("LGCY-" + StationPrefix)).ExecuteDelete();
        db.Lines.Where(x => x.Source == "Legacy" || x.Code.StartsWith("LGCY-" + LinePrefix)).ExecuteDelete();
        db.LegacyRoutingSnapshots.ExecuteDelete();
        db.LegacyProductSnapshots.ExecuteDelete();
        db.LegacyStationSnapshots.ExecuteDelete();
        db.LegacyLineSnapshots.ExecuteDelete();
        db.LegacyTransactionSnapshots.ExecuteDelete();
        return factory.CreateClient();
    }

    /// <summary>Seeds staging: 3 lines, 6 stations (2 per line), 4 products with
    /// 2-step routings, and 10 transactions (5 units × 2) all at the SECOND
    /// station so a later first-station scan stays valid.</summary>
    private DateTime SeedStaging()
    {
        var baseUtc = new DateTime(2026, 8, 23, 5, 0, 0, DateTimeKind.Utc);
        using var db = _factory.CreateDb();

        for (var line = 1; line <= 3; line++)
            db.LegacyLineSnapshots.Add(new LegacyLineSnapshot
            {
                LegacyCode = $"{LinePrefix}{line}", LegacyName = $"Smoke Line {line}",
                RawJson = "{}", ImportedAtUtc = DateTime.UtcNow,
            });
        for (var st = 1; st <= 6; st++)
            db.LegacyStationSnapshots.Add(new LegacyStationSnapshot
            {
                LegacyLineCode = $"{LinePrefix}{(st + 1) / 2}", LegacyCode = $"{StationPrefix}{st}",
                LegacyName = $"Smoke Station {st}", ProcessType = "ASSY",
                RawJson = "{}", ImportedAtUtc = DateTime.UtcNow,
            });
        for (var p = 1; p <= 4; p++)
        {
            db.LegacyProductSnapshots.Add(new LegacyProductSnapshot
            {
                LegacySku = $"{SkuPrefix}{p}", LegacyName = $"Smoke Model {p}",
                RawJson = "{}", ImportedAtUtc = DateTime.UtcNow,
            });
            // Two-step routing through the first two stations.
            foreach (var (seq, station) in new[] { (10, 1), (20, 2) })
                db.LegacyRoutingSnapshots.Add(new LegacyRoutingSnapshot
                {
                    LegacySku = $"{SkuPrefix}{p}", Sequence = seq, LegacyStationCode = $"{StationPrefix}{station}",
                    RequireLabel = false, RawJson = "{}", ImportedAtUtc = DateTime.UtcNow,
                });
        }

        // 5 units × 2 transactions, all at station 2 (routing step 2).
        string[] unitSkus = [$"{SkuPrefix}1", $"{SkuPrefix}1", $"{SkuPrefix}2", $"{SkuPrefix}2", $"{SkuPrefix}3"];
        for (var u = 0; u < 5; u++)
        {
            for (var k = 0; k < 2; k++)
            {
                db.LegacyTransactionSnapshots.Add(new LegacyTransactionSnapshot
                {
                    SN = $"{SnPrefix}{u + 1:D3}", StationCode = $"{StationPrefix}2",
                    ResultChar = (u + k) % 2 == 0 ? "P" : "F",
                    ScannedAtUtc = baseUtc.AddMinutes(u * 10 + k),
                    OperatorCode = $"OP{u}",
                    RawJson = $$"""{"productSku":"{{unitSkus[u]}}"}""",
                    ImportedAtUtc = DateTime.UtcNow,
                });
            }
        }
        db.SaveChanges();

        // Core NG code for the QC step (NG codes are not part of legacy import).
        db.NgCodes.Add(new NgCode { Code = NgCode, Description = "Smoke defect", IsActive = true });
        db.SaveChanges();
        return baseUtc;
    }

    private async Task<HttpClient> AsAsync(string username, string password, UserRole? role = null)
    {
        _factory.EnsureSeedUsers();
        using (var db = _factory.CreateDb())
        {
            if (role is not null && !db.Users.Any(u => u.Username == username))
                db.Users.Add(new AppUser
                {
                    Username = username,
                    PasswordHash = ZyrexMES.Infrastructure.Security.PasswordHasher.Hash(password),
                    FullName = username,
                    Role = role.Value,
                    IsActive = true,
                });
            db.SaveChanges();
        }
        var login = await _anon.PostAsJsonAsync("/api/auth/login", new { username, password });
        var json = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync());
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());
        return c;
    }

    [Fact]
    public async Task Full_Phase2_Flow_Import_Reconcile_Scan_Quality()
    {
        var baseUtc = SeedStaging();
        var admin = await AsAsync("admin", "Adm1n!pwd");

        // --- 1. Master data import -------------------------------------------
        var masterRes = await admin.PostAsJsonAsync("/api/migration/import-master", new { });
        Assert.Equal(HttpStatusCode.OK, masterRes.StatusCode);
        var master = JsonDocument.Parse(await masterRes.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(3, master.GetProperty("linesCreated").GetInt32());
        Assert.Equal(6, master.GetProperty("stationsCreated").GetInt32());
        Assert.Equal(4, master.GetProperty("productsCreated").GetInt32());
        Assert.Equal(8, master.GetProperty("routingsCreated").GetInt32()); // 4 SKUs × 2 steps

        // --- 2. Transaction import (fictive 1-day window) ---------------------
        var txRes = await admin.PostAsJsonAsync("/api/migration/import-transactions",
            new { fromUtc = baseUtc.AddHours(-1), toUtc = baseUtc.AddHours(23) });
        Assert.Equal(HttpStatusCode.OK, txRes.StatusCode);
        var tx = JsonDocument.Parse(await txRes.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(10, tx.GetProperty("fetched").GetInt32());
        Assert.Equal(5, tx.GetProperty("unitsCreated").GetInt32());
        Assert.Equal(10, tx.GetProperty("transactionsInserted").GetInt32());
        Assert.Empty(tx.GetProperty("errors").EnumerateArray());

        // --- 3. Reconciliation report: everything in sync ---------------------
        var reportRes = await admin.GetAsync("/api/migration/reconciliation/report");
        Assert.Equal(HttpStatusCode.OK, reportRes.StatusCode);
        var rows = JsonDocument.Parse(await reportRes.Content.ReadAsStringAsync()).RootElement;
        foreach (var row in rows.EnumerateArray())
            Assert.True(row.GetProperty("match").GetBoolean(),
                $"{row.GetProperty("table").GetString()} out of sync");

        // --- 4. Random sample verification ------------------------------------
        var sampleRes = await admin.PostAsJsonAsync("/api/migration/reconciliation/sample", new { count = 10 });
        Assert.Equal(HttpStatusCode.OK, sampleRes.StatusCode);
        var sample = JsonDocument.Parse(await sampleRes.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(10, sample.GetProperty("sampled").GetInt32());
        Assert.Equal(10, sample.GetProperty("matched").GetInt32());
        Assert.Empty(sample.GetProperty("mismatches").EnumerateArray());

        // --- 5. Production scan happy path (operator, first routing step) -----
        var op = await AsAsync("op1", "Op!pwd123");
        using (var db = _factory.CreateDb())
        {
            var stationId = db.Stations.Single(s => s.Code == $"LGCY-{StationPrefix}1").Id;
            var scanRes = await op.PostAsJsonAsync("/api/production/scan",
                new { serialNumber = $"{SnPrefix}001", stationId });
            Assert.Equal(HttpStatusCode.OK, scanRes.StatusCode);
            var scan = JsonDocument.Parse(await scanRes.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal("PASS", scan.GetProperty("result").GetString());
        }

        // --- 6. QC fail with ng_code → repair queue entry ----------------------
        var qa = await AsAsync("qa-smoke", "Qa!pwd1234", UserRole.Qa);
        int stationId2, ngCodeId;
        int qcUnitId;
        using (var db = _factory.CreateDb())
        {
            stationId2 = db.Stations.Single(s => s.Code == $"LGCY-{StationPrefix}1").Id;
            ngCodeId = db.NgCodes.Single(n => n.Code == NgCode).Id;
            qcUnitId = db.Units.Single(u => u.SerialNumber == $"{SnPrefix}001").Id;
        }
        var qcRes = await qa.PostAsJsonAsync("/api/quality/results",
            new { serialNumber = $"{SnPrefix}001", stationId = stationId2, verdict = "Fail", ngCodeId, notes = "smoke defect" });
        Assert.Equal(HttpStatusCode.OK, qcRes.StatusCode);
        var qc = JsonDocument.Parse(await qcRes.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("FAIL", qc.GetProperty("result").GetString());

        using (var verify = _factory.CreateDb())
        {
            var repair = verify.Repairs.Single(r => r.UnitId == qcUnitId);
            Assert.Equal(RepairStatus.Open, repair.Status);
            Assert.Equal("smoke defect", repair.ProblemDescription);
            Assert.True(verify.QcResults.Any(q => q.UnitId == qcUnitId && q.Verdict == QcVerdict.Fail));
        }
    }

    public void Dispose() { /* baseline wipe happens in Prepare of next run */ }
}
