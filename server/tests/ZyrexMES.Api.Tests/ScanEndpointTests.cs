using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZyrexMES.Api.Modules.Production;
using ZyrexMES.Domain.Entities;

namespace ZyrexMES.Api.Tests;

/// <summary>Test double capturing broadcasts; signals waiters on every call.</summary>
public sealed class FakeScanBroadcaster : IScanResultBroadcaster
{
    public List<string> Calls { get; } = [];
    private readonly List<TaskCompletionSource> _waiters = [];

    public async Task<bool> WaitForCallAsync(TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_waiters) _waiters.Add(tcs);
        var winner = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
        return winner == tcs.Task;
    }

    private void Record(string call)
    {
        lock (Calls) Calls.Add(call);
        lock (_waiters)
        {
            foreach (var w in _waiters) w.TrySetResult();
            _waiters.Clear();
        }
    }

    public Task BroadcastAcceptedAsync(string serialNumber, string stationCode, string lineCode, string result, DateTime atUtc, CancellationToken ct = default)
    {
        Record($"ScanAccepted|{serialNumber}|{stationCode}|{lineCode}|{result}");
        return Task.CompletedTask;
    }

    public Task BroadcastRejectedAsync(string serialNumber, string stationCode, string lineCode, string reason, DateTime atUtc, CancellationToken ct = default)
    {
        Record($"ScanRejected|{serialNumber}|{stationCode}|{lineCode}|{reason}");
        return Task.CompletedTask;
    }

    public Task BroadcastAlertAsync(string type, long jobId, string? error, CancellationToken ct = default)
    {
        Record($"AlertRaised|{type}|{jobId}|{error}");
        return Task.CompletedTask;
    }
}

/// <summary>Factory variant with the real SignalR broadcaster replaced by the fake.</summary>
public sealed class ScanFactory : CustomWebAppFactory
{
    public FakeScanBroadcaster Broadcaster { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IScanResultBroadcaster>();
            services.AddSingleton<IScanResultBroadcaster>(Broadcaster);
        });
    }
}

public class ScanEndpointTests : IClassFixture<ScanFactory>, IDisposable
{
    private const string LineCode = "L-SCAN";
    private const string Station10 = "ST-SCAN-10";
    private const string Station20 = "ST-SCAN-20";
    private const string Station30 = "ST-SCAN-30"; // exists but NOT in the routing
    private const string Sku = "SKU-SCAN";

    private readonly ScanFactory _factory;
    private readonly HttpClient _anon;

    public ScanEndpointTests(ScanFactory factory)
    {
        _factory = factory;
        _anon = Prepare(factory);
    }

    private static HttpClient Prepare(ScanFactory factory)
    {
        // Cleanup order respects FKs; each phase commits before the next.
        using var db = factory.CreateDb();
        db.Database.Migrate();

        var unitIds = db.Units.Where(u => u.SerialNumber.StartsWith("SN-SCAN-")).Select(u => u.Id).ToList();
        foreach (var t in db.UnitTransactions.Where(t => unitIds.Contains(t.UnitId)).ToList())
            db.UnitTransactions.Remove(t);
        db.SaveChanges();
        foreach (var u in db.Units.Where(u => u.SerialNumber.StartsWith("SN-SCAN-")).ToList())
            db.Units.Remove(u);
        db.SaveChanges();

        foreach (var p in db.Products.Where(x => x.Sku == Sku).ToList())
            db.Products.Remove(p); // cascades routings + steps
        foreach (var s in db.Stations.Where(x => x.Code.StartsWith("ST-SCAN-")).ToList())
            db.Stations.Remove(s);
        foreach (var l in db.Lines.Where(x => x.Code == LineCode).ToList())
            db.Lines.Remove(l);
        db.SaveChanges();
        return factory.CreateClient();
    }

    /// <summary>Seeds line + 3 stations + product + active routing (2 steps) +
    /// one fresh unit; returns station ids keyed by code.</summary>
    private Dictionary<string, int> SeedRoutingFixture()
    {
        using var db = _factory.CreateDb();
        var line = new Line { Code = LineCode, Name = "Scan Line", IsActive = true };
        db.Lines.Add(line);
        db.SaveChanges();
        var stations = new List<Station>
        {
            new() { LineId = line.Id, Code = Station10, Name = "Scan S10", IsEnabled = true },
            new() { LineId = line.Id, Code = Station20, Name = "Scan S20", IsEnabled = true },
            new() { LineId = line.Id, Code = Station30, Name = "Scan S30", IsEnabled = true },
        };
        db.Stations.AddRange(stations);
        var product = new Product { Sku = Sku, Name = "Scan Model", IsActive = true };
        db.Products.Add(product);
        db.SaveChanges();
        var routing = new Routing { ProductId = product.Id, Name = "RT-SCAN", IsActive = true };
        db.Routings.Add(routing);
        db.SaveChanges();
        db.RoutingSteps.AddRange(
            new RoutingStep { RoutingId = routing.Id, Sequence = 10, StationId = stations[0].Id, RequireLabel = false },
            new RoutingStep { RoutingId = routing.Id, Sequence = 20, StationId = stations[1].Id, RequireLabel = false });
        db.Units.Add(new Unit { SerialNumber = "SN-SCAN-001", ProductId = product.Id, Status = UnitStatus.Created, CreatedAtUtc = DateTime.UtcNow });
        db.SaveChanges();
        return stations.ToDictionary(s => s.Code, s => s.Id);
    }

    private async Task<HttpClient> OperatorAsync()
    {
        _factory.EnsureSeedUsers();
        var login = await _anon.PostAsJsonAsync("/api/auth/login", new { username = "op1", password = "Op!pwd123" });
        var json = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync());
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());
        return c;
    }

    [Fact]
    public async Task First_Step_Scan_Persists_Transaction_And_Broadcasts_Accepted()
    {
        var stations = SeedRoutingFixture();
        var op = await OperatorAsync();

        var res = await op.PostAsJsonAsync("/api/production/scan", new { serialNumber = "SN-SCAN-001", stationId = stations[Station10] });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("PASS", body.GetProperty("result").GetString());
        Assert.True(body.GetProperty("unitId").GetInt32() > 0);
        Assert.True(body.GetProperty("transactionId").GetInt32() > 0);

        using var db = _factory.CreateDb();
        var unit = db.Units.Single(u => u.SerialNumber == "SN-SCAN-001");
        Assert.Equal(UnitStatus.InProgress, unit.Status);
        var tx = await db.UnitTransactions.SingleAsync(t => t.UnitId == unit.Id);
        Assert.Equal(QcVerdict.Pass, tx.Result);
        Assert.Null(tx.Notes);

        // Broadcast fires during the request; it may already have happened.
        var arrived = _factory.Broadcaster.Calls.Count > 0
            || await _factory.Broadcaster.WaitForCallAsync(TimeSpan.FromSeconds(2));
        Assert.True(arrived, "no broadcast within 2s");
        Assert.Contains(_factory.Broadcaster.Calls, c => c.StartsWith($"ScanAccepted|SN-SCAN-001|{Station10}|{LineCode}|PASS"));
    }

    [Fact]
    public async Task Full_Route_Second_Step_Completes_Unit_And_Next_Is_Null()
    {
        var stations = SeedRoutingFixture();
        var op = await OperatorAsync();

        var first = await op.PostAsJsonAsync("/api/production/scan", new { serialNumber = "SN-SCAN-001", stationId = stations[Station10] });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await op.PostAsJsonAsync("/api/production/scan", new { serialNumber = "SN-SCAN-001", stationId = stations[Station20] });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("PASS", body.GetProperty("result").GetString());

        using var db = _factory.CreateDb();
        var unit = db.Units.Single(u => u.SerialNumber == "SN-SCAN-001");
        Assert.Equal(UnitStatus.Completed, unit.Status);
        Assert.Equal(2, db.UnitTransactions.Count(t => t.UnitId == unit.Id));
    }

    [Fact]
    public async Task Skipping_First_Step_Is_Rejected_As_Order_Violation()
    {
        var stations = SeedRoutingFixture();
        var op = await OperatorAsync();

        var res = await op.PostAsJsonAsync("/api/production/scan", new { serialNumber = "SN-SCAN-001", stationId = stations[Station20] });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("REJECTED", body.GetProperty("result").GetString());
        Assert.Contains("routing order violation: expected station", body.GetProperty("reason").GetString());
        Assert.Contains(Station10, body.GetProperty("reason").GetString());

        var rejectedArrived = _factory.Broadcaster.Calls.Count > 0
            || await _factory.Broadcaster.WaitForCallAsync(TimeSpan.FromSeconds(2));
        Assert.True(rejectedArrived, "no broadcast within 2s");
        Assert.Contains(_factory.Broadcaster.Calls, c => c.StartsWith($"ScanRejected|SN-SCAN-001|{Station20}|{LineCode}"));

        using var db = _factory.CreateDb();
        var unit = db.Units.Single(u => u.SerialNumber == "SN-SCAN-001");
        Assert.False(db.UnitTransactions.Any(t => t.UnitId == unit.Id)); // nothing persisted
    }

    [Fact]
    public async Task Duplicate_Scan_At_Same_Station_Is_Rejected()
    {
        var stations = SeedRoutingFixture();
        var op = await OperatorAsync();

        var first = await op.PostAsJsonAsync("/api/production/scan", new { serialNumber = "SN-SCAN-001", stationId = stations[Station10] });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var dup = await op.PostAsJsonAsync("/api/production/scan", new { serialNumber = "SN-SCAN-001", stationId = stations[Station10] });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, dup.StatusCode);
        var body = JsonDocument.Parse(await dup.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("duplicate transaction at this station", body.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Unknown_Serial_Number_Is_Rejected_422()
    {
        var stations = SeedRoutingFixture();
        var op = await OperatorAsync();

        var res = await op.PostAsJsonAsync("/api/production/scan", new { serialNumber = "SN-UNKNOWN", stationId = stations[Station10] });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("REJECTED", body.GetProperty("result").GetString());
        Assert.Equal("unknown serial number", body.GetProperty("reason").GetString());

        var rejectedArrived = _factory.Broadcaster.Calls.Count > 0
            || await _factory.Broadcaster.WaitForCallAsync(TimeSpan.FromSeconds(2));
        Assert.True(rejectedArrived, "no broadcast within 2s");
        Assert.Contains(_factory.Broadcaster.Calls, c => c.StartsWith($"ScanRejected|SN-UNKNOWN|{Station10}|{LineCode}"));
    }

    [Fact]
    public async Task Unknown_Station_Returns_404_Without_Broadcast()
    {
        SeedRoutingFixture();
        var op = await OperatorAsync();

        var res = await op.PostAsJsonAsync("/api/production/scan", new { serialNumber = "SN-SCAN-001", stationId = 999999 });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Empty(_factory.Broadcaster.Calls);
    }

    [Fact]
    public async Task Station_Not_In_Routing_Is_Rejected()
    {
        var stations = SeedRoutingFixture();
        var op = await OperatorAsync();

        var res = await op.PostAsJsonAsync("/api/production/scan", new { serialNumber = "SN-SCAN-001", stationId = stations[Station30] });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("station not in routing", body.GetProperty("reason").GetString());
    }

    /// <summary>Integration: a real authenticated SignalR subscriber on the line
    /// group receives ScanAccepted from the REAL broadcaster within 2 seconds.</summary>
    [Fact]
    public async Task Hub_Subscriber_Receives_ScanAccepted_Within_Two_Seconds()
    {
        var stations = SeedRoutingFixture();
        using var plainFactory = new CustomWebAppFactory(); // real broadcaster
        plainFactory.EnsureSeedUsers();

        var login = await plainFactory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Adm1n!pwd" });
        var token = JsonDocument.Parse(await login.Content.ReadAsStreamAsync())
            .RootElement.GetProperty("token").GetString()!;

        await using var conn = new HubConnectionBuilder()
            .WithUrl($"{plainFactory.ClientOptions.BaseAddress}hubs/production", o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => plainFactory.Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.On<JsonElement>("ScanAccepted", payload => received.TrySetResult(payload));
        await conn.StartAsync();
        await conn.InvokeAsync("JoinLine", LineCode);

        var http = plainFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var scanStart = DateTime.UtcNow;
        var scanRes = await http.PostAsJsonAsync("/api/production/scan", new { serialNumber = "SN-SCAN-001", stationId = stations[Station10] });
        Assert.Equal(HttpStatusCode.OK, scanRes.StatusCode);

        var winner = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.True(winner == received.Task, "ScanAccepted not received within 2 seconds");
        var elapsed = DateTime.UtcNow - scanStart;
        Assert.True(elapsed <= TimeSpan.FromSeconds(2), $"broadcast took {elapsed.TotalMilliseconds}ms");

        var payload = await received.Task;
        Assert.Equal("SN-SCAN-001", payload.GetProperty("serialNumber").GetString());
        Assert.Equal("PASS", payload.GetProperty("result").GetString());
        Assert.Equal(LineCode, payload.GetProperty("lineCode").GetString());
    }

    public void Dispose()
    {
        // Clear captured calls between tests sharing the class fixture.
        _factory.Broadcaster.Calls.Clear();
    }
}
