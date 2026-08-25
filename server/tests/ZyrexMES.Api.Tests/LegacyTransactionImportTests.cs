using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ZyrexMES.Domain.Entities;

namespace ZyrexMES.Api.Tests;

public class LegacyTransactionImportTests(CustomWebAppFactory factory) : IClassFixture<CustomWebAppFactory>
{
    private const string Prefix = "LGCY-";
    private const string LineCode = "L-LGCYTX";
    private const string StationCode = "LGCY-ST-TX1";
    private const string ProductSku = "LGCY-TXSKU1";

    private readonly HttpClient _anon = Prepare(factory);

    private static HttpClient Prepare(CustomWebAppFactory factory)
    {
        // Cleanup order respects FKs; each phase commits before the next.
        using var db = factory.CreateDb();
        db.Database.Migrate();

        var unitIds = db.Units.Where(u => u.SerialNumber.StartsWith(Prefix)).Select(u => u.Id).ToList();
        foreach (var t in db.UnitTransactions.Where(t => unitIds.Contains(t.UnitId)).ToList())
            db.UnitTransactions.Remove(t);
        db.SaveChanges();

        foreach (var u in db.Units.Where(u => u.SerialNumber.StartsWith(Prefix)).ToList())
            db.Units.Remove(u);
        foreach (var usr in db.Users.Where(x => x.Username == "legacy-import").ToList())
            db.Users.Remove(usr);
        db.SaveChanges();

        foreach (var p in db.Products.Where(x => x.Sku == ProductSku).ToList())
            db.Products.Remove(p);
        foreach (var s in db.Stations.Where(x => x.Code == StationCode).ToList())
            db.Stations.Remove(s);
        foreach (var l in db.Lines.Where(x => x.Code == LineCode).ToList())
            db.Lines.Remove(l);
        db.SaveChanges();

        foreach (var snap in db.LegacyTransactionSnapshots.Where(x => x.SN.StartsWith(Prefix)).ToList())
            db.LegacyTransactionSnapshots.Remove(snap);
        db.SaveChanges();
        return factory.CreateClient();
    }

    private async Task<HttpClient> AdminAsync()
    {
        factory.EnsureSeedUsers();
        var res = await _anon.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Adm1n!pwd" });
        var json = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());
        return c;
    }

    /// <summary>Seeds core master data (line/station/product as Task 4 import would)
    /// plus three staging snapshots for two units inside the window.</summary>
    private void SeedHappyPath(DateTime baseUtc)
    {
        using var db = factory.CreateDb();
        var line = new Line { Code = LineCode, Name = "Legacy TX Line", IsActive = true };
        db.Lines.Add(line);
        db.SaveChanges();
        db.Stations.Add(new Station { LineId = line.Id, Code = StationCode, Name = "Legacy TX Station", ProcessType = "ICT", IsEnabled = true });
        db.Products.Add(new Product { Sku = ProductSku, Name = "Legacy TX Model", IsActive = true });
        db.SaveChanges();

        db.LegacyTransactionSnapshots.Add(new LegacyTransactionSnapshot
        {
            SN = Prefix + "SN-A", StationCode = "ST-TX1", ResultChar = "P",
            ScannedAtUtc = baseUtc.AddMinutes(1), OperatorCode = "OP77",
            RawJson = """{"productSku":"TXSKU1"}""", ImportedAtUtc = DateTime.UtcNow,
        });
        db.LegacyTransactionSnapshots.Add(new LegacyTransactionSnapshot
        {
            SN = Prefix + "SN-A", StationCode = "ST-TX1", ResultChar = "F",
            ScannedAtUtc = baseUtc.AddMinutes(2), OperatorCode = "OP77",
            RawJson = """{"productSku":"TXSKU1"}""", ImportedAtUtc = DateTime.UtcNow,
        });
        db.LegacyTransactionSnapshots.Add(new LegacyTransactionSnapshot
        {
            SN = Prefix + "SN-B", StationCode = "ST-TX1", ResultChar = "P",
            ScannedAtUtc = baseUtc.AddMinutes(3), OperatorCode = null,
            RawJson = """{"productSku":"TXSKU1"}""", ImportedAtUtc = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    private object Body(DateTime from, DateTime to, string source = "Staging", bool confirmLive = false) =>
        new { fromUtc = from, toUtc = to, source, confirmLive };

    private static JsonElement Summary(HttpResponseMessage res) =>
        JsonDocument.Parse(res.Content.ReadAsStringAsync().Result).RootElement;

    [Fact]
    public async Task Import_Maps_Staging_Snapshots_Into_Core_And_Is_Idempotent()
    {
        var baseUtc = new DateTime(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);
        SeedHappyPath(baseUtc);
        var admin = await AdminAsync();

        var res = await admin.PostAsJsonAsync("/api/migration/import-transactions",
            Body(baseUtc, baseUtc.AddHours(1)));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var run1 = Summary(res);
        Assert.Equal(3, run1.GetProperty("fetched").GetInt32());
        Assert.Equal(2, run1.GetProperty("unitsCreated").GetInt32());
        Assert.Equal(3, run1.GetProperty("transactionsInserted").GetInt32());
        Assert.Equal(0, run1.GetProperty("duplicatesSkipped").GetInt32());
        Assert.Empty(run1.GetProperty("errors").EnumerateArray());

        using (var db = factory.CreateDb())
        {
            var importerUser = db.Users.Single(u => u.Username == "legacy-import");
            Assert.Equal(UserRole.Supervisor, importerUser.Role);
            Assert.True(importerUser.IsActive);

            var units = db.Units.Where(u => u.SerialNumber.StartsWith(Prefix + "SN-")).ToList();
            Assert.Equal(2, units.Count);
            Assert.All(units, u => Assert.Equal(UnitStatus.Completed, u.Status));
            Assert.All(units, u => Assert.Equal(ProductSku, db.Products.Single(p => p.Id == u.ProductId).Sku));

            var txs = db.UnitTransactions
                .Where(t => t.ScannedAtUtc >= baseUtc && t.ScannedAtUtc <= baseUtc.AddHours(1))
                .OrderBy(t => t.ScannedAtUtc).ToList();
            Assert.Equal(3, txs.Count);
            Assert.Equal(QcVerdict.Pass, txs[0].Result);
            Assert.Equal(QcVerdict.Fail, txs[1].Result);
            Assert.All(txs, t => Assert.Equal(importerUser.Id, t.UserId));
            Assert.All(txs, t => Assert.StartsWith("LEGACY:", t.Notes!));
            Assert.Equal("LEGACY:OP77", txs[0].Notes);
        }

        // Rerun: everything is a duplicate; nothing new is written.
        var run2 = Summary(await admin.PostAsJsonAsync("/api/migration/import-transactions",
            Body(baseUtc, baseUtc.AddHours(1))));
        Assert.Equal(3, run2.GetProperty("fetched").GetInt32());
        Assert.Equal(0, run2.GetProperty("unitsCreated").GetInt32());
        Assert.Equal(0, run2.GetProperty("transactionsInserted").GetInt32());
        Assert.Equal(3, run2.GetProperty("duplicatesSkipped").GetInt32());
    }

    [Fact]
    public async Task Import_Skips_Snapshot_Without_Product_Mapping_And_Reports_Error()
    {
        var baseUtc = new DateTime(2026, 8, 21, 8, 0, 0, DateTimeKind.Utc);
        using (var db = factory.CreateDb())
        {
            var line = new Line { Code = LineCode, Name = "Legacy TX Line", IsActive = true };
            db.Lines.Add(line);
            db.SaveChanges();
            db.Stations.Add(new Station { LineId = line.Id, Code = StationCode, Name = "Legacy TX Station", IsEnabled = true });
            db.SaveChanges();
            db.LegacyTransactionSnapshots.Add(new LegacyTransactionSnapshot
            {
                SN = Prefix + "SN-X", StationCode = "ST-TX1", ResultChar = "P",
                ScannedAtUtc = baseUtc.AddMinutes(5), OperatorCode = null,
                RawJson = """{"productSku":"UNKNOWN-SKU"}""", ImportedAtUtc = DateTime.UtcNow,
            });
            db.SaveChanges();
        }

        var admin = await AdminAsync();
        var summary = Summary(await admin.PostAsJsonAsync("/api/migration/import-transactions",
            Body(baseUtc, baseUtc.AddHours(1))));

        Assert.Equal(1, summary.GetProperty("fetched").GetInt32());
        Assert.Equal(0, summary.GetProperty("unitsCreated").GetInt32());
        Assert.Equal(0, summary.GetProperty("transactionsInserted").GetInt32());
        var errors = summary.GetProperty("errors").EnumerateArray().ToList();
        Assert.Single(errors);
        Assert.Contains("product", errors[0].GetString(), StringComparison.OrdinalIgnoreCase);

        using (var db = factory.CreateDb())
            Assert.False(db.Units.Any(u => u.SerialNumber == Prefix + "SN-X"));
    }

    [Fact]
    public async Task Import_Rejects_Invalid_Window_400()
    {
        var admin = await AdminAsync();

        var futureTo = await admin.PostAsJsonAsync("/api/migration/import-transactions",
            Body(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow.AddDays(1)));
        Assert.Equal(HttpStatusCode.BadRequest, futureTo.StatusCode);

        var tooWide = await admin.PostAsJsonAsync("/api/migration/import-transactions",
            Body(DateTime.UtcNow.AddDays(-400), DateTime.UtcNow));
        Assert.Equal(HttpStatusCode.BadRequest, tooWide.StatusCode);
    }

    [Fact]
    public async Task Live_Source_Without_Approval_Returns_409()
    {
        var admin = await AdminAsync();

        var noConfirm = await admin.PostAsJsonAsync("/api/migration/import-transactions",
            Body(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, source: "LegacyApi", confirmLive: false));
        Assert.Equal(HttpStatusCode.Conflict, noConfirm.StatusCode);

        var confirmedButNotApproved = await admin.PostAsJsonAsync("/api/migration/import-transactions",
            Body(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, source: "LegacyApi", confirmLive: true));
        Assert.Equal(HttpStatusCode.Conflict, confirmedButNotApproved.StatusCode);
    }
}
