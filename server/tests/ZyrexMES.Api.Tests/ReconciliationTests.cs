using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ZyrexMES.Domain.Entities;

namespace ZyrexMES.Api.Tests;

public class ReconciliationTests(CustomWebAppFactory factory) : IClassFixture<CustomWebAppFactory>
{
    private const string LineCode = "L-RECON";
    private const string StationCode = "LGCY-ST-REC";   // core code = LGCY- + staging "ST-REC"
    private const string ProductSku = "LGCY-SKU-REC";   // core sku  = LGCY- + staging "SKU-REC"
    private const string RawStation = "ST-REC";
    private const string RawSku = "SKU-REC";

    private readonly HttpClient _anon = Prepare(factory);

    private static HttpClient Prepare(CustomWebAppFactory factory)
    {
        // Reconciliation counts are GLOBAL, so this class wipes ALL migration
        // data (staging fixtures + Source=Legacy core rows) for a known-clean
        // baseline. Other test classes re-seed their own fixtures in ctors.
        // Deletion order respects FKs; each phase commits before the next.
        using var db = factory.CreateDb();
        db.Database.Migrate();

        // Units created by the transaction importer carry arbitrary serials —
        // select by legacy ProductId as well, then clear txs before units.
        var legacyProductIds = db.Products.Where(x => x.Source == "Legacy").Select(x => x.Id).ToList();
        var affectedUnitIds = db.Units
            .Where(u => u.SerialNumber.StartsWith("LGCY-") || legacyProductIds.Contains(u.ProductId))
            .Select(u => u.Id).ToList();
        db.UnitTransactions
            .Where(t => affectedUnitIds.Contains(t.UnitId) || (t.Notes != null && t.Notes.StartsWith("LEGACY:")))
            .ExecuteDelete();
        db.Units.Where(u => affectedUnitIds.Contains(u.Id)).ExecuteDelete();

        foreach (var r in db.Routings.Where(r => r.Source == "Legacy").ToList())
            db.Routings.Remove(r);
        db.SaveChanges();

        foreach (var p in db.Products.Where(x => x.Source == "Legacy" || x.Sku == ProductSku).ToList())
            db.Products.Remove(p);
        foreach (var s in db.Stations.Where(x => x.Source == "Legacy" || x.Code == StationCode).ToList())
            db.Stations.Remove(s);
        foreach (var l in db.Lines.Where(x => x.Source == "Legacy" || x.Code == LineCode || x.Code == "L-RECON-MANUAL").ToList())
            db.Lines.Remove(l);
        db.SaveChanges();

        db.LegacyRoutingSnapshots.ExecuteDelete();
        db.LegacyProductSnapshots.ExecuteDelete();
        db.LegacyStationSnapshots.ExecuteDelete();
        db.LegacyLineSnapshots.ExecuteDelete();
        db.LegacyTransactionSnapshots.ExecuteDelete();
        return factory.CreateClient();
    }

    private async Task<HttpClient> AsAsync(string username, string password, UserRole? role = null, string fullName = "")
    {
        factory.EnsureSeedUsers();
        using (var db = factory.CreateDb())
        {
            if (role is not null && !db.Users.Any(u => u.Username == username))
                db.Users.Add(new AppUser
                {
                    Username = username,
                    PasswordHash = ZyrexMES.Infrastructure.Security.PasswordHasher.Hash(password),
                    FullName = fullName,
                    Role = role.Value,
                    IsActive = true,
                });
            db.SaveChanges();
        }
        var res = await _anon.PostAsJsonAsync("/api/auth/login", new { username, password });
        var json = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());
        return c;
    }

    /// <summary>Seeds a fully synced legacy→core dataset: 1 line, 1 station,
    /// 1 product, 1 routing group, 3 transactions — plus one manual line that
    /// must NOT be counted as legacy.</summary>
    private DateTime SeedSynced()
    {
        var baseUtc = new DateTime(2026, 8, 22, 6, 0, 0, DateTimeKind.Utc);
        using var db = factory.CreateDb();

        var line = new Line { Code = LineCode, Name = "Recon Line", IsActive = true, Source = "Legacy" };
        db.Lines.Add(line);
        db.Lines.Add(new Line { Code = "L-RECON-MANUAL", Name = "Manual Line", IsActive = true });
        db.SaveChanges();
        db.Stations.Add(new Station { LineId = line.Id, Code = StationCode, Name = "Recon Station", ProcessType = "ICT", IsEnabled = true, Source = "Legacy" });
        var product = new Product { Sku = ProductSku, Name = "Recon Model", IsActive = true, Source = "Legacy" };
        db.Products.Add(product);
        db.SaveChanges();
        db.Routings.Add(new Routing { ProductId = product.Id, Name = ProductSku, IsActive = true, Source = "Legacy" });
        db.SaveChanges();

        var user = db.Users.OrderBy(u => u.Id).First();
        var station = db.Stations.Single(s => s.Code == StationCode);
        foreach (var (sn, minutes, result) in new[] { ("LGCY-REC-A", 1, QcVerdict.Pass), ("LGCY-REC-B", 2, QcVerdict.Fail), ("LGCY-REC-C", 3, QcVerdict.Pass) })
        {
            var unit = new Unit { SerialNumber = sn, ProductId = product.Id, Status = UnitStatus.Completed, CreatedAtUtc = baseUtc.AddMinutes(minutes) };
            db.Units.Add(unit);
            db.SaveChanges();
            db.UnitTransactions.Add(new UnitTransaction
            {
                UnitId = unit.Id, StationId = station.Id, UserId = user.Id,
                ScannedAtUtc = baseUtc.AddMinutes(minutes), Result = result, Notes = "LEGACY:OP1",
            });
        }

        db.LegacyLineSnapshots.Add(new LegacyLineSnapshot { LegacyCode = "LINE-REC", LegacyName = "Recon Line", RawJson = "{}", ImportedAtUtc = DateTime.UtcNow });
        db.LegacyStationSnapshots.Add(new LegacyStationSnapshot { LegacyLineCode = "LINE-REC", LegacyCode = RawStation, LegacyName = "Recon Station", ProcessType = "ICT", RawJson = "{}", ImportedAtUtc = DateTime.UtcNow });
        db.LegacyProductSnapshots.Add(new LegacyProductSnapshot { LegacySku = RawSku, LegacyName = "Recon Model", RawJson = "{}", ImportedAtUtc = DateTime.UtcNow });
        db.LegacyRoutingSnapshots.Add(new LegacyRoutingSnapshot { LegacySku = RawSku, Sequence = 10, LegacyStationCode = RawStation, RequireLabel = false, RawJson = "{}", ImportedAtUtc = DateTime.UtcNow });
        foreach (var (sn, minutes, result) in new[] { ("LGCY-REC-A", 1, "P"), ("LGCY-REC-B", 2, "F"), ("LGCY-REC-C", 3, "P") })
        {
            db.LegacyTransactionSnapshots.Add(new LegacyTransactionSnapshot
            {
                SN = sn, StationCode = RawStation, ResultChar = result,
                ScannedAtUtc = baseUtc.AddMinutes(minutes), OperatorCode = "OP1",
                RawJson = $$"""{"productSku":"{{RawSku}}"}""", ImportedAtUtc = DateTime.UtcNow,
            });
        }
        db.SaveChanges();
        return baseUtc;
    }

    [Fact]
    public async Task Report_All_Match_When_Core_In_Sync_And_Ignores_Manual_Rows()
    {
        SeedSynced();
        var supervisor = await AsAsync("supervisor1", "Sup!pwd123", UserRole.Supervisor, "Supervisor Satu");

        var res = await supervisor.GetAsync("/api/migration/reconciliation/report");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var rows = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

        var byTable = rows.EnumerateArray().ToDictionary(r => r.GetProperty("table").GetString()!, r => r);
        Assert.Equal(5, byTable.Count);
        foreach (var name in new[] { "lines", "stations", "products", "transactions", "routings" })
            Assert.True(byTable.ContainsKey(name), $"missing table row {name}");

        Assert.Equal(1, byTable["lines"].GetProperty("legacyCount").GetInt32());
        Assert.Equal(1, byTable["lines"].GetProperty("coreCount").GetInt32()); // manual line excluded
        foreach (var name in new[] { "lines", "stations", "products", "transactions", "routings" })
        {
            Assert.True(byTable[name].GetProperty("match").GetBoolean(), $"{name} should match");
        }
        Assert.Equal(3, byTable["transactions"].GetProperty("legacyCount").GetInt32());
        Assert.Equal(3, byTable["transactions"].GetProperty("coreCount").GetInt32());
    }

    [Fact]
    public async Task Report_Detects_Drift_When_Staging_Has_Extra_Transaction()
    {
        SeedSynced();
        using (var db = factory.CreateDb())
        {
            db.LegacyTransactionSnapshots.Add(new LegacyTransactionSnapshot
            {
                SN = "LGCY-REC-D", StationCode = RawStation, ResultChar = "P",
                ScannedAtUtc = new DateTime(2026, 8, 22, 7, 0, 0, DateTimeKind.Utc),
                OperatorCode = "OP1", RawJson = """{"productSku":"SKU-REC"}""", ImportedAtUtc = DateTime.UtcNow,
            });
            db.SaveChanges();
        }

        var supervisor = await AsAsync("supervisor1", "Sup!pwd123", UserRole.Supervisor, "Supervisor Satu");
        var rows = JsonDocument.Parse(await supervisor.GetStringAsync("/api/migration/reconciliation/report")).RootElement;
        var tx = rows.EnumerateArray().Single(r => r.GetProperty("table").GetString() == "transactions");
        Assert.Equal(4, tx.GetProperty("legacyCount").GetInt32());
        Assert.Equal(3, tx.GetProperty("coreCount").GetInt32());
        Assert.False(tx.GetProperty("match").GetBoolean());
    }

    [Fact]
    public async Task Sample_All_Match_When_In_Sync()
    {
        SeedSynced();
        var supervisor = await AsAsync("supervisor1", "Sup!pwd123", UserRole.Supervisor, "Supervisor Satu");

        var res = await supervisor.PostAsJsonAsync("/api/migration/reconciliation/sample", new { count = 100 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(3, body.GetProperty("sampled").GetInt32());
        Assert.Equal(3, body.GetProperty("matched").GetInt32());
        Assert.Empty(body.GetProperty("mismatches").EnumerateArray());
    }

    [Fact]
    public async Task Sample_Detects_Field_Mismatch_When_Core_Was_Corrected()
    {
        SeedSynced();
        using (var db = factory.CreateDb())
        {
            // Simulate a manual correction: shift one imported transaction's time.
            var unit = db.Units.Single(u => u.SerialNumber == "LGCY-REC-A");
            var tx = db.UnitTransactions.Single(t => t.UnitId == unit.Id);
            tx.ScannedAtUtc = tx.ScannedAtUtc.AddMinutes(1);
            db.SaveChanges();
        }

        var supervisor = await AsAsync("supervisor1", "Sup!pwd123", UserRole.Supervisor, "Supervisor Satu");
        var res = await supervisor.PostAsJsonAsync("/api/migration/reconciliation/sample", new { count = 100 });
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(3, body.GetProperty("sampled").GetInt32());
        Assert.Equal(2, body.GetProperty("matched").GetInt32());
        var mismatches = body.GetProperty("mismatches").EnumerateArray().ToList();
        Assert.Single(mismatches);
        Assert.Equal("LGCY-REC-A", mismatches[0].GetProperty("sn").GetString());
        Assert.False(string.IsNullOrEmpty(mismatches[0].GetProperty("field").GetString()));
    }

    [Fact]
    public async Task Sample_Caps_Mismatch_List_At_20_But_Counts_Total()
    {
        SeedSynced();
        // 25 snapshots whose units were never imported → 25 mismatches.
        using (var db = factory.CreateDb())
        {
            for (var i = 1; i <= 25; i++)
                db.LegacyTransactionSnapshots.Add(new LegacyTransactionSnapshot
                {
                    SN = $"LGCY-REC-M{i:D2}", StationCode = RawStation, ResultChar = "P",
                    ScannedAtUtc = new DateTime(2026, 8, 22, 8, i % 60, 0, DateTimeKind.Utc),
                    OperatorCode = "OP1", RawJson = "{}", ImportedAtUtc = DateTime.UtcNow,
                });
            db.SaveChanges();
        }

        var supervisor = await AsAsync("supervisor1", "Sup!pwd123", UserRole.Supervisor, "Supervisor Satu");
        var res = await supervisor.PostAsJsonAsync("/api/migration/reconciliation/sample", new { count = 28 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

        // 28 sampled = 3 synced + 25 broken; matched must use the INDEPENDENT
        // total counter, not the capped example list.
        Assert.Equal(28, body.GetProperty("sampled").GetInt32());
        Assert.Equal(3, body.GetProperty("matched").GetInt32());
        Assert.Equal(25, body.GetProperty("mismatchedTotal").GetInt32());
        Assert.Equal(20, body.GetProperty("mismatches").GetArrayLength());
    }

    [Fact]
    public async Task Sample_Rejects_Out_Of_Range_Count_400()
    {
        var supervisor = await AsAsync("supervisor1", "Sup!pwd123", UserRole.Supervisor, "Supervisor Satu");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await supervisor.PostAsJsonAsync("/api/migration/reconciliation/sample", new { count = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await supervisor.PostAsJsonAsync("/api/migration/reconciliation/sample", new { count = 501 })).StatusCode);
    }

    [Fact]
    public async Task Operator_Cannot_Access_Reconciliation_403()
    {
        factory.EnsureSeedUsers();
        var login = await _anon.PostAsJsonAsync("/api/auth/login", new { username = "op1", password = "Op!pwd123" });
        var json = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync());
        var op = factory.CreateClient();
        op.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());

        var report = await op.GetAsync("/api/migration/reconciliation/report");
        var sample = await op.PostAsJsonAsync("/api/migration/reconciliation/sample", new { count = 10 });
        Assert.Equal(HttpStatusCode.Forbidden, report.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sample.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Cannot_Access_Reconciliation_401()
    {
        var report = await _anon.GetAsync("/api/migration/reconciliation/report");
        var sample = await _anon.PostAsJsonAsync("/api/migration/reconciliation/sample", new { count = 10 });
        Assert.Equal(HttpStatusCode.Unauthorized, report.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sample.StatusCode);
    }
}
