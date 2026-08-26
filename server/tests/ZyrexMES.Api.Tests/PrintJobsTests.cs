using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZyrexMES.Api.Modules.Production;
using ZyrexMES.Domain.Entities;

namespace ZyrexMES.Api.Tests;

/// <summary>Factory variant with the SignalR broadcaster replaced by the shared fake.</summary>
public sealed class PrintFactory : CustomWebAppFactory
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

public class PrintJobsTests : IClassFixture<PrintFactory>, IDisposable
{
    private const string LineCode = "L-PRT";
    private const string Station10 = "ST-PRT-10"; // RequireLabel = true
    private const string Station20 = "ST-PRT-20"; // RequireLabel = false
    private const string Sku = "SKU-PRT";

    private readonly PrintFactory _factory;
    private readonly HttpClient _anon;

    public PrintJobsTests(PrintFactory factory)
    {
        _factory = factory;
        _anon = Prepare(factory);
    }

    private static HttpClient Prepare(PrintFactory factory)
    {
        // Cleanup order respects FKs; each phase commits before the next.
        using var db = factory.CreateDb();
        db.Database.Migrate();

        var unitIds = db.Units.Where(u => u.SerialNumber.StartsWith("SN-PRT")).Select(u => u.Id).ToList();
        db.PrintJobs.RemoveRange(db.PrintJobs.Where(j => unitIds.Contains(j.UnitId)));
        db.SaveChanges();
        foreach (var t in db.UnitTransactions.Where(t => unitIds.Contains(t.UnitId)).ToList())
            db.UnitTransactions.Remove(t);
        db.SaveChanges();
        foreach (var u in db.Units.Where(u => u.SerialNumber.StartsWith("SN-PRT")).ToList())
            db.Units.Remove(u);
        db.SaveChanges();

        foreach (var p in db.Products.Where(x => x.Sku == Sku).ToList())
            db.Products.Remove(p); // cascades routings + steps
        foreach (var s in db.Stations.Where(x => x.Code.StartsWith("ST-PRT")).ToList())
            db.Stations.Remove(s);
        foreach (var l in db.Lines.Where(x => x.Code == LineCode).ToList())
            db.Lines.Remove(l);
        db.SaveChanges();
        return factory.CreateClient();
    }

    /// <summary>Seeds line + 2 stations + product + active routing (step 10 requires a label,
    /// step 20 does not); returns station ids keyed by code.</summary>
    private Dictionary<string, int> SeedRoutingFixture()
    {
        using var db = _factory.CreateDb();
        var line = new Line { Code = LineCode, Name = "Print Line", IsActive = true };
        db.Lines.Add(line);
        db.SaveChanges();
        var stations = new List<Station>
        {
            new() { LineId = line.Id, Code = Station10, Name = "Print S10", IsEnabled = true },
            new() { LineId = line.Id, Code = Station20, Name = "Print S20", IsEnabled = true },
        };
        db.Stations.AddRange(stations);
        var product = new Product { Sku = Sku, Name = "Print Model", IsActive = true };
        db.Products.Add(product);
        db.SaveChanges();
        var routing = new Routing { ProductId = product.Id, Name = "RT-PRT", IsActive = true };
        db.Routings.Add(routing);
        db.SaveChanges();
        db.RoutingSteps.AddRange(
            new RoutingStep { RoutingId = routing.Id, Sequence = 10, StationId = stations[0].Id, RequireLabel = true },
            new RoutingStep { RoutingId = routing.Id, Sequence = 20, StationId = stations[1].Id, RequireLabel = false });
        db.SaveChanges();
        return stations.ToDictionary(s => s.Code, s => s.Id);
    }

    private int NewUnit(string serialNumber)
    {
        using var db = _factory.CreateDb();
        var unit = new Unit
        {
            SerialNumber = serialNumber,
            ProductId = db.Products.Single(p => p.Sku == Sku).Id,
            Status = UnitStatus.Created,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Units.Add(unit);
        db.SaveChanges();
        return unit.Id;
    }

    private async Task<HttpClient> AsAsync(string username, string password)
    {
        _factory.EnsureSeedUsers();
        var login = await _anon.PostAsJsonAsync("/api/auth/login", new { username, password });
        var json = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync());
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());
        return c;
    }

    private Task<HttpClient> AgentAsync() => AsAsync("agent1", "Agent!pwd123");
    private Task<HttpClient> OperatorAsync() => AsAsync("op1", "Op!pwd123");

    private async Task<HttpResponseMessage> ScanAsync(HttpClient client, string sn, int stationId)
    {
        var res = await client.PostAsJsonAsync("/api/production/scan", new { serialNumber = sn, stationId });
        Assert.True(res.IsSuccessStatusCode, $"scan failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return res;
    }

    [Fact]
    public async Task Labeled_Scan_Creates_PrintJob_Once_And_Rescan_Does_Not()
    {
        var stations = SeedRoutingFixture();
        NewUnit("SN-PRT-001");
        var op = await OperatorAsync();

        await ScanAsync(op, "SN-PRT-001", stations[Station10]);

        using (var db = _factory.CreateDb())
        {
            var unitId = db.Units.Single(u => u.SerialNumber == "SN-PRT-001").Id;
            var job = await db.PrintJobs.SingleAsync(j => j.UnitId == unitId);
            Assert.Equal("Pending", job.Status);
            Assert.Equal("SN_LABEL", job.TemplateCode);
            Assert.Equal(stations[Station10], job.StationId);
            Assert.Equal(0, job.Attempts);
            Assert.Null(job.CompletedAtUtc);
            // jsonb normalizes whitespace — parse instead of substring matching.
            var payload = JsonDocument.Parse(job.PayloadJson).RootElement;
            Assert.Equal("SN-PRT-001", payload.GetProperty("sn").GetString());
            Assert.Equal(Sku, payload.GetProperty("productSku").GetString());
            Assert.Equal(Station10, payload.GetProperty("stationCode").GetString());
            Assert.False(payload.GetProperty("scannedAtUtc").ValueKind is JsonValueKind.Null or JsonValueKind.Undefined);
        }

        // Rescan at the same station is rejected and must not add another job.
        var rescan = await op.PostAsJsonAsync("/api/production/scan",
            new { serialNumber = "SN-PRT-001", stationId = stations[Station10] });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rescan.StatusCode);
        using (var db = _factory.CreateDb())
        {
            var unitId = db.Units.Single(u => u.SerialNumber == "SN-PRT-001").Id;
            Assert.Equal(1, db.PrintJobs.Count(j => j.UnitId == unitId));
        }
    }

    [Fact]
    public async Task Unlabeled_Step_Creates_No_Extra_Job()
    {
        var stations = SeedRoutingFixture();
        NewUnit("SN-PRT-002");
        var op = await OperatorAsync();

        await ScanAsync(op, "SN-PRT-002", stations[Station10]);
        await ScanAsync(op, "SN-PRT-002", stations[Station20]);

        using var db = _factory.CreateDb();
        var unitId = db.Units.Single(u => u.SerialNumber == "SN-PRT-002").Id;
        Assert.Equal(1, db.PrintJobs.Count(j => j.UnitId == unitId)); // only the labeled step
    }

    [Fact]
    public async Task Claim_Marks_Sent_Increments_Attempts_And_Empties_Queue()
    {
        var stations = SeedRoutingFixture();
        NewUnit("SN-PRT-003");
        var op = await OperatorAsync();
        await ScanAsync(op, "SN-PRT-003", stations[Station10]);
        var agent = await AgentAsync();

        var res = await agent.PostAsync($"/api/print/jobs/claim?stationId={stations[Station10]}", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var items = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, items.GetArrayLength());
        var item = items[0];
        var jobId = item.GetProperty("id").GetInt64();
        Assert.True(jobId > 0);
        Assert.Equal("SN_LABEL", item.GetProperty("templateCode").GetString());
        Assert.Equal(1, item.GetProperty("attempts").GetInt32());

        using (var db = _factory.CreateDb())
        {
            var job = await db.PrintJobs.SingleAsync(j => j.Id == jobId);
            Assert.Equal("Sent", job.Status);
            Assert.Equal(1, job.Attempts);
        }

        // Queue drained: second claim returns nothing.
        var again = await agent.PostAsync($"/api/print/jobs/claim?stationId={stations[Station10]}", null);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(0, JsonDocument.Parse(await again.Content.ReadAsStringAsync()).RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Ack_Ok_Marks_Printed()
    {
        var stations = SeedRoutingFixture();
        NewUnit("SN-PRT-004");
        var op = await OperatorAsync();
        await ScanAsync(op, "SN-PRT-004", stations[Station10]);
        var agent = await AgentAsync();
        var claim = JsonDocument.Parse(await (await agent.PostAsync(
            $"/api/print/jobs/claim?stationId={stations[Station10]}", null)).Content.ReadAsStringAsync()).RootElement;
        var jobId = claim[0].GetProperty("id").GetInt64();

        var ack = await agent.PostAsJsonAsync($"/api/print/jobs/{jobId}/ack", new { ok = true });
        Assert.Equal(HttpStatusCode.OK, ack.StatusCode);

        using var db = _factory.CreateDb();
        var job = await db.PrintJobs.SingleAsync(j => j.Id == jobId);
        Assert.Equal("Printed", job.Status);
        Assert.NotNull(job.CompletedAtUtc);
    }

    [Fact]
    public async Task Ack_Fail_Three_Times_Fails_Job_And_Broadcasts_Alert()
    {
        var stations = SeedRoutingFixture();
        NewUnit("SN-PRT-005");
        var op = await OperatorAsync();
        await ScanAsync(op, "SN-PRT-005", stations[Station10]);
        var agent = await AgentAsync();

        long jobId = 0;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var claim = JsonDocument.Parse(await (await agent.PostAsync(
                $"/api/print/jobs/claim?stationId={stations[Station10]}", null)).Content.ReadAsStringAsync()).RootElement;
            jobId = claim[0].GetProperty("id").GetInt64();
            var ack = await agent.PostAsJsonAsync($"/api/print/jobs/{jobId}/ack", new { ok = false, error = "out of paper" });
            Assert.Equal(HttpStatusCode.OK, ack.StatusCode);

            using var db = _factory.CreateDb();
            var job = await db.PrintJobs.SingleAsync(j => j.Id == jobId);
            if (attempt < 3)
                Assert.Equal("Pending", job.Status); // retry scheduled
            else
            {
                Assert.Equal("Failed", job.Status);
                Assert.Equal(3, job.Attempts);
                Assert.NotNull(job.CompletedAtUtc);
            }
        }

        var arrived = _factory.Broadcaster.Calls.Any(c => c.StartsWith($"AlertRaised|print_failed|{jobId}|"))
            || await _factory.Broadcaster.WaitForCallAsync(TimeSpan.FromSeconds(2));
        Assert.True(arrived, "no print_failed alert within 2s");
        Assert.Single(_factory.Broadcaster.Calls, c => c.StartsWith($"AlertRaised|print_failed|{jobId}|"));

        // Re-acking a terminal job is idempotent: same status, no extra alert,
        // no mutation of Attempts/CompletedAtUtc.
        using (var db = _factory.CreateDb())
        {
            var before = await db.PrintJobs.SingleAsync(j => j.Id == jobId);
            var reAck = await agent.PostAsJsonAsync($"/api/print/jobs/{jobId}/ack", new { ok = false, error = "out of paper" });
            Assert.Equal(HttpStatusCode.OK, reAck.StatusCode);
            Assert.Equal("Failed", JsonDocument.Parse(await reAck.Content.ReadAsStringAsync()).RootElement.GetProperty("status").GetString());
            await db.Entry(before).ReloadAsync();
            Assert.Equal("Failed", before.Status);
            Assert.Equal(3, before.Attempts);
            Assert.NotNull(before.CompletedAtUtc);
            Assert.Single(_factory.Broadcaster.Calls, c => c.StartsWith($"AlertRaised|print_failed|{jobId}|"));
        }
    }

    [Fact]
    public async Task Claim_Returns_At_Most_Ten_Jobs_Per_Call()
    {
        var stations = SeedRoutingFixture();
        using (var db = _factory.CreateDb())
        {
            var productId = db.Products.Single(p => p.Sku == Sku).Id;
            for (var i = 0; i < 12; i++)
            {
                var unit = new Unit
                {
                    SerialNumber = $"SN-PRT-B{i}",
                    ProductId = productId,
                    Status = UnitStatus.Created,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                db.Units.Add(unit);
                await db.SaveChangesAsync(); // assign unit.Id first (no nav property)
                db.PrintJobs.Add(new PrintJob
                {
                    UnitId = unit.Id,
                    StationId = stations[Station10],
                    TemplateCode = "SN_LABEL",
                    PayloadJson = "{}",
                    Status = "Pending",
                    CreatedAtUtc = DateTime.UtcNow,
                });
            }
            await db.SaveChangesAsync();
        }

        var agent = await AgentAsync();
        var res = await agent.PostAsync($"/api/print/jobs/claim?stationId={stations[Station10]}", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var items = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(10, items.GetArrayLength());

        // Remaining 2 pending jobs are claimable on the next call.
        var second = JsonDocument.Parse(await (await agent.PostAsync(
            $"/api/print/jobs/claim?stationId={stations[Station10]}", null)).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(2, second.GetArrayLength());
    }

    [Fact]
    public async Task Claim_And_Ack_Require_Agent_Role()
    {
        var stations = SeedRoutingFixture();
        NewUnit("SN-PRT-006");
        var op = await OperatorAsync();
        await ScanAsync(op, "SN-PRT-006", stations[Station10]);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _anon.PostAsync($"/api/print/jobs/claim?stationId={stations[Station10]}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await op.PostAsync($"/api/print/jobs/claim?stationId={stations[Station10]}", null)).StatusCode);

        var agent = await AgentAsync();
        var claim = JsonDocument.Parse(await (await agent.PostAsync(
            $"/api/print/jobs/claim?stationId={stations[Station10]}", null)).Content.ReadAsStringAsync()).RootElement;
        var jobId = claim[0].GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await op.PostAsJsonAsync($"/api/print/jobs/{jobId}/ack", new { ok = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _anon.PostAsJsonAsync($"/api/print/jobs/{jobId}/ack", new { ok = true })).StatusCode);
    }

    public void Dispose() => _factory.Broadcaster.Calls.Clear();
}
