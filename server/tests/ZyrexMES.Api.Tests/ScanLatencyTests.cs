using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit.Abstractions;
using ZyrexMES.Api.Modules.Production;
using ZyrexMES.Domain.Entities;

namespace ZyrexMES.Api.Tests;

/// <summary>AC-01 latency harness: sequential scan p95 must stay ≤ 500 ms,
/// plus regression coverage for the duplicate-scan DB guard.</summary>
public class ScanLatencyTests : IClassFixture<CustomWebAppFactory>, IDisposable
{
    private const string LineCode = "L-PERF";
    private const string Sku = "SKU-PERF";
    private static readonly string[] StationCodes = ["PERF-ST-10", "PERF-ST-20", "PERF-ST-30"];

    private readonly CustomWebAppFactory _factory;
    private readonly HttpClient _anon;
    private readonly ITestOutputHelper _output;

    public ScanLatencyTests(CustomWebAppFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
        _anon = Prepare(factory);
    }

    private static HttpClient Prepare(CustomWebAppFactory factory)
    {
        // Cleanup order respects FKs; each phase commits before the next.
        using var db = factory.CreateDb();
        db.Database.Migrate();

        var unitIds = db.Units.Where(u => u.SerialNumber.StartsWith("SN-PERF-")).Select(u => u.Id).ToList();
        foreach (var t in db.UnitTransactions.Where(t => unitIds.Contains(t.UnitId)).ToList())
            db.UnitTransactions.Remove(t);
        db.SaveChanges();
        foreach (var u in db.Units.Where(u => u.SerialNumber.StartsWith("SN-PERF-")).ToList())
            db.Units.Remove(u);
        db.SaveChanges();
        foreach (var p in db.Products.Where(x => x.Sku == Sku).ToList())
            db.Products.Remove(p); // cascades routing + steps
        foreach (var s in db.Stations.Where(x => StationCodes.Contains(x.Code)).ToList())
            db.Stations.Remove(s);
        foreach (var l in db.Lines.Where(x => x.Code == LineCode).ToList())
            db.Lines.Remove(l);
        db.SaveChanges();
        return factory.CreateClient();
    }

    /// <summary>Creates the shared line/stations/product/routing once, then a
    /// fresh batch of units; returns station ids and the unit serials in order.</summary>
    private (List<int> StationIds, List<string> Serials) SeedBatch(string suffix, int unitCount)
    {
        using var db = _factory.CreateDb();

        var line = db.Lines.FirstOrDefault(l => l.Code == LineCode);
        if (line is null)
        {
            line = new Line { Code = LineCode, Name = "Perf Line", IsActive = true };
            db.Lines.Add(line);
            db.SaveChanges();
        }

        var stationIds = new List<int>(3);
        foreach (var code in StationCodes)
        {
            var st = db.Stations.FirstOrDefault(s => s.Code == code);
            if (st is null)
            {
                st = new Station { LineId = line.Id, Code = code, Name = code, IsEnabled = true };
                db.Stations.Add(st);
                db.SaveChanges();
            }
            stationIds.Add(st.Id);
        }

        var product = db.Products.FirstOrDefault(p => p.Sku == Sku);
        if (product is null)
        {
            product = new Product { Sku = Sku, Name = "Perf Model", IsActive = true };
            db.Products.Add(product);
            db.SaveChanges();
            var routing = new Routing { ProductId = product.Id, Name = "RT-PERF", IsActive = true };
            db.Routings.Add(routing);
            db.SaveChanges();
            for (var i = 0; i < 3; i++)
                db.RoutingSteps.Add(new RoutingStep { RoutingId = routing.Id, Sequence = (i + 1) * 10, StationId = stationIds[i], RequireLabel = false });
            db.SaveChanges();
        }

        var serials = new List<string>(unitCount);
        for (var i = 1; i <= unitCount; i++)
        {
            var serial = $"SN-PERF-{suffix}-{i:D3}";
            db.Units.Add(new Unit { SerialNumber = serial, ProductId = product.Id, Status = UnitStatus.Created, CreatedAtUtc = DateTime.UtcNow });
            serials.Add(serial);
        }
        db.SaveChanges();
        return (stationIds, serials);
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

    /// <summary>Runs unitCount × 3 sequential scans (full route per unit) and
    /// returns the p95 of the per-call HTTP durations in milliseconds.</summary>
    private async Task<double> MeasureBatchP95Async(HttpClient op, string suffix, int unitCount)
    {
        var (stationIds, serials) = SeedBatch(suffix, unitCount);
        var durations = new List<double>(unitCount * 3);

        for (var i = 0; i < unitCount * 3; i++)
        {
            var sw = Stopwatch.StartNew();
            var res = await op.PostAsJsonAsync("/api/production/scan",
                new { serialNumber = serials[i / 3], stationId = stationIds[i % 3] });
            sw.Stop();
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            durations.Add(sw.Elapsed.TotalMilliseconds);
        }

        durations.Sort();
        var p95Index = (int)Math.Ceiling(0.95 * durations.Count) - 1;
        return durations[p95Index];
    }

    [Fact]
    public async Task Scan_P95_Stays_Under_500ms_In_Two_Consistent_Batches()
    {
        var op = await OperatorAsync();

        // JIT/connection warmup: one full route, untimed.
        var warm = SeedBatch("WARM", 1);
        for (var step = 0; step < 3; step++)
        {
            var res = await op.PostAsJsonAsync("/api/production/scan",
                new { serialNumber = warm.Serials[0], stationId = warm.StationIds[step] });
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        var batch1 = await MeasureBatchP95Async(op, "R1", 20);
        var batch2 = await MeasureBatchP95Async(op, "R2", 20);
        _output.WriteLine($"scan p95 batch1 = {batch1:F1} ms (60 samples)");
        _output.WriteLine($"scan p95 batch2 = {batch2:F1} ms (60 samples)");

        Assert.True(batch1 <= 500, $"batch1 p95={batch1:F1}ms exceeds 500ms");
        Assert.True(batch2 <= 500, $"batch2 p95={batch2:F1}ms exceeds 500ms");
    }

    [Fact]
    public void Duplicate_Triple_Insert_Is_Blocked_By_Unique_Guard()
    {
        var (stationIds, serials) = SeedBatch("GUARD", 1);
        var scannedAt = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

        int unitId;
        using (var db = _factory.CreateDb())
        {
            unitId = db.Units.Single(u => u.SerialNumber == serials[0]).Id;
            db.UnitTransactions.Add(new UnitTransaction
            {
                UnitId = unitId, StationId = stationIds[0], UserId = db.Users.OrderBy(u => u.Id).First().Id,
                ScannedAtUtc = scannedAt, Result = QcVerdict.Pass,
            });
            db.SaveChanges();
        }

        // Same (unit, station, scannedAtUtc) from a second context must hit the guard.
        using var db2 = _factory.CreateDb();
        db2.UnitTransactions.Add(new UnitTransaction
        {
            UnitId = unitId, StationId = stationIds[0], UserId = db2.Users.OrderBy(u => u.Id).First().Id,
            ScannedAtUtc = scannedAt, Result = QcVerdict.Pass,
        });
        var ex = Assert.Throws<DbUpdateException>(() => db2.SaveChanges());
        Assert.True(ProductionEndpoints.IsDuplicateConstraintViolation(ex),
            $"expected 23505 duplicate-guard violation, got: {ex.InnerException?.GetType().Name}");
    }

    [Fact]
    public async Task Concurrent_Same_Station_Scans_Never_Return_Server_Error()
    {
        var (stationIds, serials) = SeedBatch("RACE", 1);
        var op = await OperatorAsync();

        // Both scans race past the app-level check; whichever loses must map to
        // 422 REJECTED (never 500), whichever wins is 200.
        var task1 = op.PostAsJsonAsync("/api/production/scan", new { serialNumber = serials[0], stationId = stationIds[0] });
        var task2 = op.PostAsJsonAsync("/api/production/scan", new { serialNumber = serials[0], stationId = stationIds[0] });
        await Task.WhenAll(task1, task2);

        foreach (var res in new[] { task1.Result, task2.Result })
        {
            Assert.True(res.StatusCode is HttpStatusCode.OK or HttpStatusCode.UnprocessableEntity,
                $"unexpected status {(int)res.StatusCode}");
            if (res.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
                Assert.Equal("duplicate transaction at this station", body.GetProperty("reason").GetString());
            }
        }
    }

    public void Dispose() { /* fixture cleanup handled by ctor of next test run */ }
}
