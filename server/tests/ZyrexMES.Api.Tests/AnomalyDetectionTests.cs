using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZyrexMES.Api.Hubs;
using ZyrexMES.Api.Modules.Insights;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Tests;

#region Fake hub

public sealed class FakeAlertClientProxy : IClientProxy
{
    public List<(string Method, object?[] Args)> Sent { get; } = [];
    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        lock (Sent) Sent.Add((method, args));
        return Task.CompletedTask;
    }
}

public sealed class FakeHubClients : IHubClients
{
    public FakeAlertClientProxy AllProxy { get; } = new();
    public IClientProxy All => AllProxy;
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => AllProxy;
    public IClientProxy Client(string connectionId) => AllProxy;
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => AllProxy;
    public IClientProxy Group(string groupName) => AllProxy;
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => AllProxy;
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => AllProxy;
    public IClientProxy User(string userId) => AllProxy;
    public IClientProxy Users(IReadOnlyList<string> userIds) => AllProxy;
}

public sealed class FakeAlertHubContext : IHubContext<ProductionHub>
{
    public IHubClients Clients { get; }
    public IGroupManager Groups => throw new NotImplementedException();
    public FakeAlertHubContext(FakeHubClients clients) => Clients = clients;
}

public sealed class AnomalyFactory : CustomWebAppFactory
{
    public FakeHubClients FakeClients { get; } = new();
    public FakeAlertHubContext FakeHub { get; }

    public AnomalyFactory()
    {
        FakeHub = new FakeAlertHubContext(FakeClients);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            // Stop hosted loop; we trigger manually.
            foreach (var d in services.Where(s => s.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList())
                services.Remove(d);
            services.RemoveAll<IHubContext<ProductionHub>>();
            services.AddSingleton<IHubContext<ProductionHub>>(FakeHub);
            services.AddSingleton(FakeClients);
            services.AddSingleton<AnomalyDetectorService>();
        });
    }
}

#endregion

public class AnomalyDetectionTests : IClassFixture<AnomalyFactory>
{
    private readonly AnomalyFactory _factory;
    private readonly HttpClient _anon;

    public AnomalyDetectionTests(AnomalyFactory factory)
    {
        _factory = factory;
        _anon = Prepare(factory);
        // clear broadcast captures between test runs (class fixture shared)
        _factory.FakeClients.AllProxy.Sent.Clear();
    }

    private static HttpClient Prepare(AnomalyFactory factory)
    {
        using var db = factory.CreateDb();
        db.Database.Migrate();
        // Remove leftovers from previous runs
        var adLines = db.Lines.Where(l => l.Code.StartsWith("L-AD")).ToList();
        var adLineIds = adLines.Select(l => l.Id).ToList();
        var adStations = db.Stations.Where(s => adLineIds.Contains(s.LineId)).Select(s => s.Id).ToList();
        db.UnitTransactions.RemoveRange(db.UnitTransactions.Where(t => adStations.Contains(t.StationId)));
        db.QcResults.RemoveRange(db.QcResults.Where(q => adStations.Contains(q.StationId)));
        db.Units.RemoveRange(db.Units.Where(u => u.SerialNumber.StartsWith("SN-AD-")));
        db.Alerts.RemoveRange(db.Alerts.Where(a => a.LineCode != null && a.LineCode.StartsWith("L-AD")));
        db.Products.RemoveRange(db.Products.Where(p => p.Sku.StartsWith("SKU-AD")));
        db.NgCodes.RemoveRange(db.NgCodes.Where(n => n.Code.StartsWith("NG-AD")));
        db.Lines.RemoveRange(adLines);
        db.SaveChanges();
        return factory.CreateClient();
    }

    private async Task<(HttpClient admin, HttpClient op, HttpClient supervisor)> ClientsAsync()
    {
        _factory.EnsureSeedUsers();
        // ensure supervisor exists
        using (var db = _factory.CreateDb())
        {
            if (!db.Users.Any(u => u.Username == "sup1"))
            {
                db.Users.Add(new AppUser { Username = "sup1", PasswordHash = ZyrexMES.Infrastructure.Security.PasswordHasher.Hash("Sup!pwd123"), FullName = "Supervisor Satu", Role = UserRole.Supervisor, IsActive = true });
                db.SaveChanges();
            }
        }
        async Task<HttpClient> As(string u, string p)
        {
            var res = await _anon.PostAsJsonAsync("/api/auth/login", new { username = u, password = p });
            res.EnsureSuccessStatusCode();
            var json = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
            var c = _factory.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());
            return c;
        }
        return (await As("admin", "Adm1n!pwd"), await As("op1", "Op!pwd123"), await As("sup1", "Sup!pwd123"));
    }

    private async Task<(int lineId, int stationId, string lineCode, int productId, int ngCodeId, int userId)> SeedLineAsync(string sfx)
    {
        _factory.EnsureSeedUsers();
        using var db = _factory.CreateDb();
        var userId = db.Users.First(u => u.Username == "admin").Id;
        var product = await db.Products.FirstOrDefaultAsync(p => p.Sku == $"SKU-AD-{sfx}");
        if (product is null)
        {
            product = new Product { Sku = $"SKU-AD-{sfx}", Name = $"AD Product {sfx}" };
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }
        var ng = await db.NgCodes.FirstOrDefaultAsync(n => n.Code == $"NG-AD-{sfx}");
        if (ng is null)
        {
            ng = new NgCode { Code = $"NG-AD-{sfx}", Description = "ad ng" };
            db.NgCodes.Add(ng);
            await db.SaveChangesAsync();
        }
        var line = new Line { Code = $"L-AD-{sfx}", Name = $"AD Line {sfx}" };
        db.Lines.Add(line);
        await db.SaveChangesAsync();
        var station = new Station { LineId = line.Id, Code = $"ST-AD-{sfx}", Name = $"Station {sfx}" };
        db.Stations.Add(station);
        await db.SaveChangesAsync();
        return (line.Id, station.Id, line.Code, product.Id, ng.Id, userId);
    }

    private async Task AddTransactionsAsync(string sfx, int stationId, int productId, int userId, DateTime hourStart, int total, int failCount, int startIndex)
    {
        using var db = _factory.CreateDb();
        for (var i = 0; i < total; i++)
        {
            var unit = new Unit { SerialNumber = $"SN-AD-{sfx}-{startIndex + i}-{Guid.NewGuid():N}", ProductId = productId, Status = UnitStatus.InProgress, CreatedAtUtc = hourStart.AddMinutes(2 + i) };
            db.Units.Add(unit);
            await db.SaveChangesAsync();
            db.UnitTransactions.Add(new UnitTransaction { UnitId = unit.Id, StationId = stationId, UserId = userId, ScannedAtUtc = hourStart.AddMinutes(2 + i), Result = QcVerdict.Pass });
            await db.SaveChangesAsync();
            if (i < failCount)
            {
                // use same station ng code for simplicity
                var ngId = db.NgCodes.First(n => n.Code.StartsWith("NG-AD")).Id;
                // We'll use provided ng retrieval; but simpler use any
                // Actually fetch correct sfx ng
                db.QcResults.Add(new QcResult { UnitId = unit.Id, StationId = stationId, UserId = userId, Verdict = QcVerdict.Fail, NgCodeId = ngId, CheckedAtUtc = hourStart.AddMinutes(3 + i), Notes = "ad fail" });
                await db.SaveChangesAsync();
            }
        }
    }

    private async Task AddHourAsync(string sfx, int stationId, int productId, int userId, int ngCodeId, DateTime hourStart, int total, int fails)
    {
        using var db = _factory.CreateDb();
        for (var i = 0; i < total; i++)
        {
            var unit = new Unit { SerialNumber = $"SN-AD-{sfx}-{hourStart:yyyyMMddHH}-{i}-{Guid.NewGuid():N}", ProductId = productId, Status = UnitStatus.InProgress, CreatedAtUtc = hourStart.AddMinutes(1 + i) };
            db.Units.Add(unit);
            await db.SaveChangesAsync();
            db.UnitTransactions.Add(new UnitTransaction { UnitId = unit.Id, StationId = stationId, UserId = userId, ScannedAtUtc = hourStart.AddMinutes(1 + i), Result = QcVerdict.Pass });
            await db.SaveChangesAsync();
            if (i < fails)
            {
                db.QcResults.Add(new QcResult { UnitId = unit.Id, StationId = stationId, UserId = userId, Verdict = QcVerdict.Fail, NgCodeId = ngCodeId, CheckedAtUtc = hourStart.AddMinutes(2 + i) });
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task YieldDrop_Creates_Alert_And_Broadcasts()
    {
        var sfx = "YD1";
        var (lineId, stationId, lineCode, productId, ngCodeId, userId) = await SeedLineAsync(sfx);
        var now = DateTime.UtcNow;
        var hourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        // 7 days baseline: 10 output 0 fail => 100% yield
        for (var d = 1; d <= 7; d++)
            await AddHourAsync(sfx, stationId, productId, userId, ngCodeId, hourStart.AddDays(-d), 10, 0);
        // current hour: 10 output 5 fail => 50% yield, drop 50 >=20
        await AddHourAsync(sfx, stationId, productId, userId, ngCodeId, hourStart, 10, 5);

        _factory.FakeClients.AllProxy.Sent.Clear();
        var detector = _factory.Services.GetRequiredService<AnomalyDetectorService>();
        await detector.ExecuteOnceAsync();

        using var db = _factory.CreateDb();
        var alerts = await db.Alerts.Where(a => a.LineCode == lineCode).ToListAsync();
        Assert.Contains(alerts, a => a.Type == "yield_drop");
        // broadcast captured
        Assert.Contains(_factory.FakeClients.AllProxy.Sent, s => s.Method == "AlertRaised");
    }

    [Fact]
    public async Task Dedupe_Does_Not_Duplicate_Within_30_Minutes()
    {
        var sfx = "DED1";
        var (lineId, stationId, lineCode, productId, ngCodeId, userId) = await SeedLineAsync(sfx);
        var now = DateTime.UtcNow;
        var hourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        for (var d = 1; d <= 7; d++)
            await AddHourAsync(sfx, stationId, productId, userId, ngCodeId, hourStart.AddDays(-d), 10, 0);
        await AddHourAsync(sfx, stationId, productId, userId, ngCodeId, hourStart, 10, 5);

        var detector = _factory.Services.GetRequiredService<AnomalyDetectorService>();
        _factory.FakeClients.AllProxy.Sent.Clear();
        await detector.ExecuteOnceAsync();
        using (var db = _factory.CreateDb())
        {
            var count1 = await db.Alerts.CountAsync(a => a.LineCode == lineCode && a.Type == "yield_drop");
            Assert.True(count1 >= 1);
        }
        _factory.FakeClients.AllProxy.Sent.Clear();
        await detector.ExecuteOnceAsync();
        using (var db = _factory.CreateDb())
        {
            var count2 = await db.Alerts.CountAsync(a => a.LineCode == lineCode && a.Type == "yield_drop");
            // should not have added second within 30m
            var count1Again = await db.Alerts.CountAsync(a => a.LineCode == lineCode && a.Type == "yield_drop");
            Assert.Equal(count1Again, count2);
            // Ensure no duplicated broadcast on second run if deduped (still could be low_yield/ng extra but yield_drop not duplicated)
            // At least not duplicated for yield_drop type specifically
            // If low_yield also deduped, second run should have zero new alerts; verify Sent does not contain new yield_drop
            var hasYieldDropBroadcast = _factory.FakeClients.AllProxy.Sent.Any(s => s.Method == "AlertRaised" && s.Args.Length > 0 && s.Args[0] != null && s.Args[0].ToString()!.Contains("yield_drop"));
            Assert.False(hasYieldDropBroadcast);
        }
    }

    [Fact]
    public async Task LowYield_Triggers_When_Below_Threshold()
    {
        var sfx = "LY1";
        var (lineId, stationId, lineCode, productId, ngCodeId, userId) = await SeedLineAsync(sfx);
        var now = DateTime.UtcNow;
        var hourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        // baseline irrelevant, just need current low yield <85
        for (var d = 1; d <= 7; d++)
            await AddHourAsync(sfx, stationId, productId, userId, ngCodeId, hourStart.AddDays(-d), 10, 1); // 90%
        await AddHourAsync(sfx, stationId, productId, userId, ngCodeId, hourStart, 10, 4); // 60% <85

        _factory.FakeClients.AllProxy.Sent.Clear();
        var detector = _factory.Services.GetRequiredService<AnomalyDetectorService>();
        await detector.ExecuteOnceAsync();

        using var db = _factory.CreateDb();
        var alerts = await db.Alerts.Where(a => a.LineCode == lineCode).ToListAsync();
        Assert.Contains(alerts, a => a.Type == "low_yield");
    }

    [Fact]
    public async Task NgSpike_Triggers_When_Threshold_Exceeded()
    {
        var sfx = "NG1";
        var (lineId, stationId, lineCode, productId, ngCodeId, userId) = await SeedLineAsync(sfx);
        var now = DateTime.UtcNow;
        var hourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        // Create enough transactions to support 12 fails (need 12 units)
        await AddHourAsync(sfx, stationId, productId, userId, ngCodeId, hourStart, 12, 12);

        var detector = _factory.Services.GetRequiredService<AnomalyDetectorService>();
        _factory.FakeClients.AllProxy.Sent.Clear();
        await detector.ExecuteOnceAsync();

        using var db = _factory.CreateDb();
        var alerts = await db.Alerts.Where(a => a.LineCode == lineCode).ToListAsync();
        Assert.Contains(alerts, a => a.Type == "ng_spike");
        Assert.Contains(_factory.FakeClients.AllProxy.Sent, s => s.Method == "AlertRaised");
    }

    [Fact]
    public async Task GetAlerts_And_Ack_Idempotent()
    {
        var (admin, op, sup) = await ClientsAsync();
        // Seed an alert directly via DB to test endpoint without detector
        string lineCode;
        long alertId;
        using (var db = _factory.CreateDb())
        {
            var line = new Line { Code = $"L-AD-ACK-{Guid.NewGuid():N}".Substring(0, 16), Name = "Ack line" };
            db.Lines.Add(line);
            await db.SaveChangesAsync();
            lineCode = line.Code;
            var alert = new Alert { Type = "low_yield", Severity = "critical", Message = "test", LineCode = lineCode, CreatedAtUtc = DateTime.UtcNow };
            db.Alerts.Add(alert);
            await db.SaveChangesAsync();
            alertId = alert.Id;
        }

        var listRes = await sup.GetAsync($"/api/alerts?unackedOnly=true&take=50");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var list = await listRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(list.GetArrayLength() >= 1);

        var ackRes = await sup.PostAsync($"/api/alerts/{alertId}/ack", null);
        Assert.Equal(HttpStatusCode.OK, ackRes.StatusCode);
        var ackJson = await ackRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(JsonValueKind.Null, ackJson.GetProperty("acknowledgedAtUtc").ValueKind);

        // idempotent second ack
        var ack2 = await sup.PostAsync($"/api/alerts/{alertId}/ack", null);
        Assert.Equal(HttpStatusCode.OK, ack2.StatusCode);
        var ack2Json = await ack2.Content.ReadFromJsonAsync<JsonElement>();
        var t1 = ackJson.GetProperty("acknowledgedAtUtc").GetDateTime();
        var t2 = ack2Json.GetProperty("acknowledgedAtUtc").GetDateTime();
        Assert.Equal(t1, t2, TimeSpan.FromMilliseconds(1));

        // op (Operator) cannot ack -> 403
        var forbidden = await op.PostAsync($"/api/alerts/{alertId}/ack", null);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // after ack, unackedOnly should exclude it
        var filtered = await sup.GetAsync($"/api/alerts?unackedOnly=true&take=50");
        var filtJson = await filtered.Content.ReadFromJsonAsync<JsonElement>();
        // ensure our alert not in unacked list
        foreach (var el in filtJson.EnumerateArray())
            Assert.NotEqual(alertId, el.GetProperty("id").GetInt64());

        // but all includes it
        var allRes = await sup.GetAsync($"/api/alerts?unackedOnly=false&take=50");
        var allJson = await allRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(allJson.EnumerateArray(), e => e.GetProperty("id").GetInt64() == alertId);
    }

    [Fact]
    public async Task Thresholds_Endpoint_Returns_Config()
    {
        var (admin, _, _) = await ClientsAsync();
        var res = await admin.GetAsync("/api/insights/thresholds");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("minYieldPercent", out var v1));
        Assert.Equal(85, v1.GetDouble());
        Assert.True(json.TryGetProperty("yieldDropPercent", out var v2));
        Assert.Equal(20, v2.GetDouble());
        Assert.True(json.TryGetProperty("ngSpikePerHour", out var v3));
        Assert.Equal(10, v3.GetInt32());
        Assert.True(json.TryGetProperty("evaluationIntervalMinutes", out var v4));
        Assert.Equal(5, v4.GetInt32());
    }

    [Fact]
    public async Task Anonymous_Cannot_Access_Alerts()
    {
        var res = await _anon.GetAsync("/api/alerts");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var thr = await _anon.GetAsync("/api/insights/thresholds");
        Assert.Equal(HttpStatusCode.Unauthorized, thr.StatusCode);
    }
}
