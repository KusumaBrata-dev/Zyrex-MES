using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ZyrexMES.Domain.Entities;

namespace ZyrexMES.Api.Tests;

public class LegacyMasterImportTests(CustomWebAppFactory factory) : IClassFixture<CustomWebAppFactory>
{
    private const string Prefix = "LGCY-";

    private readonly HttpClient _anon = Prepare(factory);

    private static HttpClient Prepare(CustomWebAppFactory factory)
    {
        // Import counts are ABSOLUTE, so this class wipes ALL migration data for a
        // known-clean baseline (ReconciliationTests does the same; other classes
        // re-seed their own fixtures in their ctors). Ordered immediate bulk
        // deletes: steps on legacy stations, then products (routings/steps
        // cascade), then lines (stations cascade), then all staging fixtures.
        using var db = factory.CreateDb();
        db.Database.Migrate();
        // Units (and their transactions) reference legacy products with Restrict
        // FKs — clear them before master data.
        db.UnitTransactions.Where(t => t.Unit.SerialNumber.StartsWith(Prefix) || (t.Notes != null && t.Notes.StartsWith("LEGACY:"))).ExecuteDelete();
        db.Units.Where(u => u.SerialNumber.StartsWith(Prefix)).ExecuteDelete();
        var legacyStationIds = db.Stations.Where(x => x.Source == "Legacy" || x.Code.StartsWith(Prefix)).Select(x => x.Id).ToList();
        db.RoutingSteps.Where(x => legacyStationIds.Contains(x.StationId)).ExecuteDelete();
        db.Routings.Where(x => x.Source == "Legacy").ExecuteDelete();
        db.Products.Where(x => x.Source == "Legacy" || x.Sku.StartsWith(Prefix)).ExecuteDelete();
        db.Stations.Where(x => x.Source == "Legacy" || x.Code.StartsWith(Prefix)).ExecuteDelete();
        db.Lines.Where(x => x.Source == "Legacy" || x.Code.StartsWith(Prefix)).ExecuteDelete();
        db.LegacyRoutingSnapshots.ExecuteDelete();
        db.LegacyProductSnapshots.ExecuteDelete();
        db.LegacyStationSnapshots.ExecuteDelete();
        db.LegacyLineSnapshots.ExecuteDelete();
        db.LegacyTransactionSnapshots.ExecuteDelete();
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

    private void SeedCoherentStaging()
    {
        using var db = factory.CreateDb();
        db.LegacyLineSnapshots.Add(new LegacyLineSnapshot { LegacyCode = "LINE-A", LegacyName = "Legacy Line A", RawJson = "{}", ImportedAtUtc = DateTime.UtcNow });
        db.LegacyStationSnapshots.Add(new LegacyStationSnapshot { LegacyLineCode = "LINE-A", LegacyCode = "ST-A", LegacyName = "Legacy ICT", ProcessType = "ICT", RawJson = "{}", ImportedAtUtc = DateTime.UtcNow });
        db.LegacyProductSnapshots.Add(new LegacyProductSnapshot { LegacySku = "SKU-A", LegacyName = "Legacy Model A", RawJson = "{}", ImportedAtUtc = DateTime.UtcNow });
        db.LegacyRoutingSnapshots.Add(new LegacyRoutingSnapshot { LegacySku = "SKU-A", Sequence = 10, LegacyStationCode = "ST-A", RequireLabel = true, RawJson = "{}", ImportedAtUtc = DateTime.UtcNow });
        db.LegacyRoutingSnapshots.Add(new LegacyRoutingSnapshot { LegacySku = "SKU-A", Sequence = 20, LegacyStationCode = "ST-A", RequireLabel = false, RawJson = "{}", ImportedAtUtc = DateTime.UtcNow });
        db.SaveChanges();
    }

    private static JsonElement Summary(HttpResponseMessage res) =>
        JsonDocument.Parse(res.Content.ReadAsStringAsync().Result).RootElement;

    [Fact]
    public async Task Import_Creates_Core_Rows_With_Legacy_Source_And_Is_Idempotent()
    {
        SeedCoherentStaging();

        // Manual guard row must never be touched by the import.
        using (var db = factory.CreateDb())
        {
            if (!db.Lines.Any(l => l.Code == "L-MANUAL"))
                db.Lines.Add(new Line { Code = "L-MANUAL", Name = "Manual Line", IsActive = true });
            db.SaveChanges();
        }

        var admin = await AdminAsync();

        var run1 = Summary(await admin.PostAsJsonAsync("/api/migration/import-master", new { }));
        Assert.Equal(1, run1.GetProperty("linesCreated").GetInt32());
        Assert.Equal(1, run1.GetProperty("stationsCreated").GetInt32());
        Assert.Equal(1, run1.GetProperty("productsCreated").GetInt32());
        Assert.Equal(2, run1.GetProperty("routingsCreated").GetInt32());
        Assert.Equal(0, run1.GetProperty("skippedDuplicates").GetInt32());
        Assert.Equal(0, run1.GetProperty("skippedMissingStation").GetInt32());

        using (var db = factory.CreateDb())
        {
            var line = db.Lines.Include(l => l.Stations).Single(l => l.Code == Prefix + "LINE-A");
            Assert.Equal("Legacy Line A", line.Name);
            Assert.Equal("Legacy", line.Source);
            var station = line.Stations.Single(s => s.Code == Prefix + "ST-A");
            Assert.Equal("Legacy", station.Source);

            var product = db.Products.Include(p => p.Routings).ThenInclude(r => r.Steps).Single(p => p.Sku == Prefix + "SKU-A");
            Assert.Equal("Legacy", product.Source);
            var routing = product.Routings.Single(r => r.Name == Prefix + "SKU-A");
            Assert.Equal("Legacy", routing.Source);
            Assert.Equal(2, routing.Steps.Count);
            Assert.Contains(routing.Steps, st => st.Sequence == 10 && st.RequireLabel);

            Assert.Equal("Manual", db.Lines.Single(l => l.Code == "L-MANUAL").Source);
        }

        // Second run: nothing new; every staged master row is now a duplicate.
        var before = CountCoreRows();
        var run2 = Summary(await admin.PostAsJsonAsync("/api/migration/import-master", new { }));
        Assert.Equal(0, run2.GetProperty("linesCreated").GetInt32());
        Assert.Equal(0, run2.GetProperty("stationsCreated").GetInt32());
        Assert.Equal(0, run2.GetProperty("productsCreated").GetInt32());
        Assert.Equal(0, run2.GetProperty("routingsCreated").GetInt32());
        Assert.Equal(5, run2.GetProperty("skippedDuplicates").GetInt32());

        // Third run: summary identical to second run and row count stable.
        var run3 = Summary(await admin.PostAsJsonAsync("/api/migration/import-master", new { }));
        Assert.Equal(run2.GetRawText(), run3.GetRawText());
        Assert.Equal(before, CountCoreRows());
    }

    [Fact]
    public async Task Import_Skips_Routing_Row_When_Station_Missing()
    {
        using (var db = factory.CreateDb())
        {
            db.LegacyProductSnapshots.Add(new LegacyProductSnapshot { LegacySku = "SKU-B", LegacyName = "Legacy Model B", RawJson = "{}", ImportedAtUtc = DateTime.UtcNow });
            db.LegacyRoutingSnapshots.Add(new LegacyRoutingSnapshot { LegacySku = "SKU-B", Sequence = 10, LegacyStationCode = "ST-GHOST", RequireLabel = false, RawJson = "{}", ImportedAtUtc = DateTime.UtcNow });
            db.SaveChanges();
        }

        var admin = await AdminAsync();
        var run1 = Summary(await admin.PostAsJsonAsync("/api/migration/import-master", new { }));
        Assert.Equal(0, run1.GetProperty("routingsCreated").GetInt32());
        Assert.Equal(1, run1.GetProperty("skippedMissingStation").GetInt32());

        // run2 skips the product as duplicate; from then on summaries are stable.
        var run2 = Summary(await admin.PostAsJsonAsync("/api/migration/import-master", new { }));
        Assert.Equal(0, run2.GetProperty("productsCreated").GetInt32());
        var run3 = Summary(await admin.PostAsJsonAsync("/api/migration/import-master", new { }));
        Assert.Equal(run2.GetRawText(), run3.GetRawText());
    }

    [Theory]
    [InlineData("leader1", "Lead!pwd12", HttpStatusCode.Forbidden)]
    [InlineData("op1", "Op!pwd123", HttpStatusCode.Forbidden)]
    public async Task Non_Admin_Cannot_Import_403(string user, string password, HttpStatusCode expected)
    {
        factory.EnsureSeedUsers();
        var res = await _anon.PostAsJsonAsync("/api/auth/login", new { username = user, password });
        var json = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());

        var import = await c.PostAsJsonAsync("/api/migration/import-master", new { });
        Assert.Equal(expected, import.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Cannot_Import_401()
    {
        var res = await _anon.PostAsJsonAsync("/api/migration/import-master", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    private int CountCoreRows()
    {
        using var db = factory.CreateDb();
        return db.Lines.Count(l => l.Code.StartsWith(Prefix))
             + db.Stations.Count(s => s.Code.StartsWith(Prefix))
             + db.Products.Count(p => p.Sku.StartsWith(Prefix))
             + db.Routings.Count(r => r.Name.StartsWith(Prefix))
             + db.RoutingSteps.Count(st => st.Routing.Name.StartsWith(Prefix));
    }
}
