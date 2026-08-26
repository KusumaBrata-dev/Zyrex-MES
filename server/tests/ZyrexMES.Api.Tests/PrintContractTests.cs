using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Tests;

/// <summary>
/// HTTP-contract tests for the printing endpoints, mirroring what the Print
/// Agent's ApiClient does: claim and ack are POST (guards against the client
/// and server verbs drifting apart).
/// </summary>
public class PrintContractTests(CustomWebAppFactory factory) : IClassFixture<CustomWebAppFactory>
{
    private const string LineCode = "L-PCT";
    private const string StationCode = "ST-PCT-1";
    private const int ForeignStationId = 999777; // no jobs seeded there

    private readonly HttpClient _anon = Prepare(factory);

    private static HttpClient Prepare(CustomWebAppFactory factory)
    {
        using var db = factory.CreateDb();
        db.Database.Migrate();
        var line = db.Lines.FirstOrDefault(l => l.Code == LineCode);
        if (line is not null)
        {
            var unitIds = db.Units.Where(u => u.SerialNumber.StartsWith("SN-PCT")).Select(u => u.Id).ToList();
            db.PrintJobs.RemoveRange(db.PrintJobs.Where(j => unitIds.Contains(j.UnitId)));
            db.SaveChanges();
            foreach (var u in db.Units.Where(u => u.SerialNumber.StartsWith("SN-PCT")).ToList())
                db.Units.Remove(u);
            db.SaveChanges();
            foreach (var p in db.Products.Where(p => p.Sku == "SKU-PCT").ToList())
                db.Products.Remove(p); // cascades routings
            db.Lines.Remove(line); // cascades stations
            db.SaveChanges();
        }
        return factory.CreateClient();
    }

    private async Task<HttpClient> AgentAsync()
    {
        factory.EnsureSeedUsers();
        var login = await _anon.PostAsJsonAsync("/api/auth/login", new { username = "agent1", password = "Agent!pwd123" });
        var json = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync());
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());
        return c;
    }

    /// <summary>Seeds one dummy Pending job; returns its id.</summary>
    private async Task<long> SeedPendingJobAsync()
    {
        using var db = factory.CreateDb();
        var line = new Line { Code = LineCode, Name = "Contract Line" };
        var station = new Station { Line = line, Code = StationCode, Name = "Contract S1" };
        var product = new Product { Sku = "SKU-PCT", Name = "Contract Product" };
        db.Lines.Add(line);
        db.Stations.Add(station);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var unit = new Unit { SerialNumber = "SN-PCT-1", ProductId = product.Id, Status = UnitStatus.Created, CreatedAtUtc = DateTime.UtcNow };
        db.Units.Add(unit);
        await db.SaveChangesAsync();
        var job = new PrintJob
        {
            UnitId = unit.Id,
            StationId = station.Id,
            TemplateCode = "SN_LABEL",
            PayloadJson = "{}",
            Status = "Pending",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.PrintJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    [Fact]
    public async Task Claim_Post_Returns_200_With_Array()
    {
        var agent = await AgentAsync();

        var res = await agent.PostAsync($"/api/print/jobs/claim?stationId={ForeignStationId}", null);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(JsonValueKind.Array,
            (await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync())).RootElement.ValueKind);
    }

    [Fact]
    public async Task Claim_Get_Is_Not_Allowed()
    {
        var agent = await AgentAsync();

        var res = await agent.GetAsync($"/api/print/jobs/claim?stationId={ForeignStationId}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, res.StatusCode);
    }

    [Fact]
    public async Task Ack_Post_On_Dummy_Job_Returns_200_Printed()
    {
        var jobId = await SeedPendingJobAsync();
        var agent = await AgentAsync();

        var res = await agent.PostAsJsonAsync($"/api/print/jobs/{jobId}/ack", new { ok = true });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        Assert.Equal("Printed", body.RootElement.GetProperty("status").GetString());
    }
}
