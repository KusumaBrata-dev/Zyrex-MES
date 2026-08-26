using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Tests;

public class ReportsEndpointsTests(CustomWebAppFactory factory) : IClassFixture<CustomWebAppFactory>
{
    private const string RepNgCode = "REP-NG1";
    private readonly HttpClient _anon = Prepare(factory);

    private static HttpClient Prepare(CustomWebAppFactory factory)
    {
        // Clean up leftovers from previous runs so reruns stay idempotent.
        using var db = factory.CreateDb();
        db.Database.Migrate();
        var lines = db.Lines.Where(l => l.Code.StartsWith("L-REP")).ToList();
        var lineIds = lines.Select(l => l.Id).ToList();
        var unitIds = db.Units.Where(u => u.SerialNumber.StartsWith("SN-REP")).Select(u => u.Id).ToList();
        db.UnitTransactions.RemoveRange(db.UnitTransactions.Where(t => unitIds.Contains(t.UnitId)));
        db.QcResults.RemoveRange(db.QcResults.Where(q => unitIds.Contains(q.UnitId)));
        db.Units.RemoveRange(db.Units.Where(u => u.SerialNumber.StartsWith("SN-REP")));
        db.Lines.RemoveRange(lines);
        db.Products.RemoveRange(db.Products.Where(p => p.Sku == "SKU-REP"));
        db.NgCodes.RemoveRange(db.NgCodes.Where(n => n.Code == RepNgCode));
        db.SaveChanges();
        return factory.CreateClient();
    }

    private async Task<(HttpClient admin, HttpClient op)> ClientsAsync()
    {
        factory.EnsureSeedUsers();
        async Task<HttpClient> As(string u, string p)
        {
            var res = await _anon.PostAsJsonAsync("/api/auth/login", new { username = u, password = p });
            var json = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
            var c = factory.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());
            return c;
        }
        return (await As("admin", "Adm1n!pwd"), await As("op1", "Op!pwd123"));
    }

    private sealed record SeedCtx(int LineId, int StationAId, int StationBId, int UserId, int NgCodeId);

    /// <summary>Seeds line L-REP-&lt;sfx&gt; with stations ST-REP-A&lt;sfx&gt;/ST-REP-B&lt;sfx&gt; plus shared product/ng-code.</summary>
    private async Task<SeedCtx> SeedLineAsync(string sfx)
    {
        factory.EnsureSeedUsers();
        using var db = factory.CreateDb();
        var userId = db.Users.First(u => u.Username == "admin").Id;

        var product = await db.Products.FirstOrDefaultAsync(p => p.Sku == "SKU-REP");
        if (product is null)
        {
            product = new Product { Sku = "SKU-REP", Name = "Report Test Product" };
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }
        var ng = await db.NgCodes.FirstOrDefaultAsync(n => n.Code == RepNgCode);
        if (ng is null)
        {
            ng = new NgCode { Code = RepNgCode, Description = "report test ng" };
            db.NgCodes.Add(ng);
            await db.SaveChangesAsync();
        }

        var line = new Line { Code = $"L-REP-{sfx}", Name = $"Report Line {sfx}" };
        var sa = new Station { Line = line, Code = $"ST-REP-A{sfx}", Name = "Station A" };
        var sb = new Station { Line = line, Code = $"ST-REP-B{sfx}", Name = "Station B" };
        db.Lines.Add(line);
        db.Stations.AddRange(sa, sb);
        await db.SaveChangesAsync();
        return new SeedCtx(line.Id, sa.Id, sb.Id, userId, ng.Id);
    }

    private async Task<int> AddUnitWithTxAsync(string sfx, int i, int stationId, int userId, DateTime scannedAtUtc)
    {
        using var db = factory.CreateDb();
        var productId = db.Products.First(p => p.Sku == "SKU-REP").Id;
        var unit = new Unit { SerialNumber = $"SN-REP-{sfx}-{i}", ProductId = productId, Status = UnitStatus.InProgress, CreatedAtUtc = scannedAtUtc };
        db.Units.Add(unit);
        db.UnitTransactions.Add(new UnitTransaction
        {
            Unit = unit, StationId = stationId, UserId = userId, ScannedAtUtc = scannedAtUtc, Result = QcVerdict.Pass,
        });
        await db.SaveChangesAsync();
        return unit.Id;
    }

    private async Task AddQcFailAsync(int unitId, int stationId, int userId, int ngCodeId, DateTime checkedAtUtc, string notes)
    {
        using var db = factory.CreateDb();
        db.QcResults.Add(new QcResult
        {
            UnitId = unitId, StationId = stationId, UserId = userId,
            Verdict = QcVerdict.Fail, NgCodeId = ngCodeId, Notes = notes, CheckedAtUtc = checkedAtUtc,
        });
        await db.SaveChangesAsync();
    }

    private static string TodayWib() => DateTime.UtcNow.AddHours(7).ToString("yyyy-MM-dd");

    [Fact]
    public async Task Station_Summary_Returns_Counters_And_Yield()
    {
        var (admin, _) = await ClientsAsync();
        var ctx = await SeedLineAsync("S1");
        var now = DateTime.UtcNow;
        // Brief: 3 pass transactions + exactly 1 failed QC at the same station.
        var firstUnitId = await AddUnitWithTxAsync("S1", 0, ctx.StationAId, ctx.UserId, now.AddMinutes(-10));
        await AddQcFailAsync(firstUnitId, ctx.StationAId, ctx.UserId, ctx.NgCodeId, now.AddMinutes(-9), "scratch");
        for (var i = 1; i < 3; i++)
            await AddUnitWithTxAsync("S1", i, ctx.StationAId, ctx.UserId, now.AddMinutes(-10 - i));

        var res = await admin.GetAsync($"/api/reports/station-summary?stationId={ctx.StationAId}&date={TodayWib()}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ctx.StationAId, json.GetProperty("stationId").GetInt32());
        Assert.Equal("ST-REP-AS1", json.GetProperty("stationCode").GetString());
        Assert.Equal(3, json.GetProperty("output").GetInt32());
        Assert.Equal(1, json.GetProperty("ng").GetInt32());
        Assert.Equal(66.7, json.GetProperty("yieldPercent").GetDouble());
    }

    [Fact]
    public async Task Station_Summary_Yield_Null_When_No_Output()
    {
        var (admin, _) = await ClientsAsync();
        var ctx = await SeedLineAsync("S2");

        var res = await admin.GetAsync($"/api/reports/station-summary?stationId={ctx.StationBId}&date={TodayWib()}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("output").GetInt32());
        Assert.Equal(0, json.GetProperty("ng").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("yieldPercent").ValueKind);
    }

    [Fact]
    public async Task Station_Summary_Unknown_Station_404()
    {
        var (admin, _) = await ClientsAsync();
        var res = await admin.GetAsync($"/api/reports/station-summary?stationId=999999&date={TodayWib()}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Ng_List_Paginates_And_Filters_By_Station()
    {
        var (admin, _) = await ClientsAsync();
        var ctx = await SeedLineAsync("N1");
        var now = DateTime.UtcNow;
        // 2 fails at A, 1 fail at B, 1 pass QC (excluded).
        var u1 = await AddUnitWithTxAsync("N1", 1, ctx.StationAId, ctx.UserId, now.AddMinutes(-30));
        var u2 = await AddUnitWithTxAsync("N1", 2, ctx.StationAId, ctx.UserId, now.AddMinutes(-20));
        var u3 = await AddUnitWithTxAsync("N1", 3, ctx.StationBId, ctx.UserId, now.AddMinutes(-15));
        var u4 = await AddUnitWithTxAsync("N1", 4, ctx.StationAId, ctx.UserId, now.AddMinutes(-10));
        await AddQcFailAsync(u1, ctx.StationAId, ctx.UserId, ctx.NgCodeId, now.AddMinutes(-29), "ng one");
        await AddQcFailAsync(u2, ctx.StationAId, ctx.UserId, ctx.NgCodeId, now.AddMinutes(-19), "ng two");
        await AddQcFailAsync(u3, ctx.StationBId, ctx.UserId, ctx.NgCodeId, now.AddMinutes(-14), "ng three");
        using (var db = factory.CreateDb())
        {
            db.QcResults.Add(new QcResult
            {
                UnitId = u4, StationId = ctx.StationAId, UserId = ctx.UserId,
                Verdict = QcVerdict.Pass, CheckedAtUtc = now.AddMinutes(-9),
            });
            await db.SaveChangesAsync();
        }

        // Pagination: our 3 fails plus possible leftovers from other suites share
        // the day window, so only assert the page shape here; exact totals are
        // asserted per-station below (stations seeded here are exclusively ours).
        var paged = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/reports/ng-list?date={TodayWib()}&page=1&pageSize=2");
        Assert.True(paged.GetProperty("total").GetInt32() >= 3);
        Assert.Equal(1, paged.GetProperty("page").GetInt32());
        Assert.Equal(2, paged.GetProperty("items").GetArrayLength());

        // Filter by station A: only its 2 fails.
        var filtered = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/reports/ng-list?date={TodayWib()}&stationId={ctx.StationAId}");
        Assert.Equal(2, filtered.GetProperty("total").GetInt32());
        foreach (var item in filtered.GetProperty("items").EnumerateArray())
        {
            Assert.StartsWith("SN-REP-N1", item.GetProperty("sn").GetString());
            Assert.Equal("ST-REP-AN1", item.GetProperty("stationCode").GetString());
            Assert.Equal(RepNgCode, item.GetProperty("ngCode").GetString());
            Assert.False(string.IsNullOrEmpty(item.GetProperty("notes").GetString()));
            Assert.NotEqual(default, item.GetProperty("checkedAtUtc").GetDateTime());
        }

        // Defaults: no page/pageSize params still returns page 1.
        var defaults = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/reports/ng-list?date={TodayWib()}&stationId={ctx.StationBId}");
        Assert.Equal(1, defaults.GetProperty("total").GetInt32());
        Assert.Equal(1, defaults.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Line_Grid_Includes_All_Lines_With_Counters_And_Status()
    {
        var (admin, _) = await ClientsAsync();
        var ctx = await SeedLineAsync("G1");
        var now = DateTime.UtcNow;
        // Recent event at A -> active; B untouched -> idle with zero counters.
        var u1 = await AddUnitWithTxAsync("G1", 1, ctx.StationAId, ctx.UserId, now.AddMinutes(-5));
        await AddQcFailAsync(u1, ctx.StationAId, ctx.UserId, ctx.NgCodeId, now.AddMinutes(-4), "grid ng");

        var res = await admin.GetAsync("/api/reports/line-grid");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        var lines = json.GetProperty("lines");

        var g1 = lines.EnumerateArray().First(l => l.GetProperty("lineCode").GetString() == "L-REP-G1");
        var stations = g1.GetProperty("stations");
        var a = stations.EnumerateArray().First(s => s.GetProperty("stationCode").GetString() == "ST-REP-AG1");
        Assert.Equal(1, a.GetProperty("outputToday").GetInt32());
        Assert.Equal(1, a.GetProperty("ngToday").GetInt32());
        Assert.Equal("active", a.GetProperty("status").GetString());
        Assert.True(a.TryGetProperty("lastEventAtUtc", out var lastA) && lastA.ValueKind != JsonValueKind.Null);

        var b = stations.EnumerateArray().First(s => s.GetProperty("stationCode").GetString() == "ST-REP-BG1");
        Assert.Equal(0, b.GetProperty("outputToday").GetInt32());
        Assert.Equal(0, b.GetProperty("ngToday").GetInt32());
        Assert.Equal("idle", b.GetProperty("status").GetString());

        // Empty seeded line still appears.
        await SeedLineAsync("G2"); // second line, no events
        var refreshed = await admin.GetFromJsonAsync<JsonElement>("/api/reports/line-grid");
        Assert.Contains(refreshed.GetProperty("lines").EnumerateArray(),
            l => l.GetProperty("lineCode").GetString() == "L-REP-G2");
    }

    [Fact]
    public async Task Anonymous_Cannot_Access_Reports_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anon.GetAsync("/api/reports/station-summary?stationId=1&date=2026-08-26")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anon.GetAsync("/api/reports/ng-list?date=2026-08-26")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anon.GetAsync("/api/reports/line-grid")).StatusCode);
    }
}
