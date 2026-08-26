using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ZyrexMES.Api.Modules.Production;
using ZyrexMES.Domain.Entities;

namespace ZyrexMES.Api.Tests;

public class QualityEndpointTests : IClassFixture<ScanFactory>, IDisposable
{
    private const string LineCode = "L-QC";
    private const string StationCode = "ST-QC-1";
    private const string Sku = "SKU-QC";
    private const string NgCode = "NG-QC-TEST";

    private readonly ScanFactory _factory;
    private readonly HttpClient _anon;

    public QualityEndpointTests(ScanFactory factory)
    {
        _factory = factory;
        _anon = Prepare(factory);
    }

    private static HttpClient Prepare(ScanFactory factory)
    {
        // Cleanup order respects FKs; each phase commits before the next.
        using var db = factory.CreateDb();
        db.Database.Migrate();

        var unitIds = db.Units.Where(u => u.SerialNumber.StartsWith("SN-QC-")).Select(u => u.Id).ToList();
        foreach (var r in db.Repairs.Where(r => unitIds.Contains(r.UnitId)).ToList())
            db.Repairs.Remove(r);
        foreach (var q in db.QcResults.Where(q => unitIds.Contains(q.UnitId)).ToList())
            db.QcResults.Remove(q);
        db.SaveChanges();
        foreach (var u in db.Units.Where(u => u.SerialNumber.StartsWith("SN-QC-")).ToList())
            db.Units.Remove(u);
        db.SaveChanges();
        foreach (var n in db.NgCodes.Where(n => n.Code == NgCode || n.Code == "NG-QC-INACTIVE").ToList())
            db.NgCodes.Remove(n);
        foreach (var p in db.Products.Where(x => x.Sku == Sku).ToList())
            db.Products.Remove(p);
        foreach (var s in db.Stations.Where(x => x.Code == StationCode).ToList())
            db.Stations.Remove(s);
        foreach (var l in db.Lines.Where(x => x.Code == LineCode).ToList())
            db.Lines.Remove(l);
        db.SaveChanges();
        return factory.CreateClient();
    }

    /// <summary>Seeds line/station/product/unit + active and inactive NG codes.</summary>
    private void SeedFixture()
    {
        using var db = _factory.CreateDb();
        var line = new Line { Code = LineCode, Name = "QC Line", IsActive = true };
        db.Lines.Add(line);
        db.SaveChanges();
        db.Stations.Add(new Station { LineId = line.Id, Code = StationCode, Name = "QC Station", IsEnabled = true });
        var product = new Product { Sku = Sku, Name = "QC Model", IsActive = true };
        db.Products.Add(product);
        db.SaveChanges();
        db.Units.Add(new Unit { SerialNumber = "SN-QC-001", ProductId = product.Id, Status = UnitStatus.InProgress, CreatedAtUtc = DateTime.UtcNow });
        db.NgCodes.AddRange(
            new NgCode { Code = NgCode, Description = "QC test defect", IsActive = true },
            new NgCode { Code = "NG-QC-INACTIVE", Description = "retired code", IsActive = false });
        db.SaveChanges();
    }

    private async Task<HttpClient> AsAsync(string username, string password, UserRole role)
    {
        _factory.EnsureSeedUsers();
        using (var db = _factory.CreateDb())
        {
            if (!db.Users.Any(u => u.Username == username))
                db.Users.Add(new AppUser
                {
                    Username = username,
                    PasswordHash = ZyrexMES.Infrastructure.Security.PasswordHasher.Hash(password),
                    FullName = username,
                    Role = role,
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

    private Task<HttpClient> QaAsync() => AsAsync("qa1", "Qa!pwd1234", UserRole.Qa);

    [Fact]
    public async Task Pass_Verdict_Creates_QcResult_Without_Repair_And_Broadcasts_Accepted()
    {
        SeedFixture();
        var qa = await QaAsync();

        var res = await qa.PostAsJsonAsync("/api/quality/results",
            new { serialNumber = "SN-QC-001", stationId = GetStationId(), verdict = "Pass", notes = (string?)"looks fine" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("PASS", body.GetProperty("result").GetString());
        Assert.True(body.GetProperty("qcResultId").GetInt32() > 0);
        Assert.False(body.TryGetProperty("repairId", out var repairId) && repairId.ValueKind == JsonValueKind.Number);

        using var db = _factory.CreateDb();
        var unit = db.Units.Single(u => u.SerialNumber == "SN-QC-001");
        var qc = Assert.Single(db.QcResults.Where(q => q.UnitId == unit.Id));
        Assert.Equal(QcVerdict.Pass, qc.Verdict);
        Assert.Null(qc.NgCodeId);
        Assert.False(db.Repairs.Any(r => r.UnitId == unit.Id));

        var arrived = _factory.Broadcaster.Calls.Count > 0
            || await _factory.Broadcaster.WaitForCallAsync(TimeSpan.FromSeconds(2));
        Assert.True(arrived, "no broadcast within 2s");
        Assert.Contains(_factory.Broadcaster.Calls, c => c.StartsWith($"ScanAccepted|SN-QC-001|{StationCode}|{LineCode}|PASS"));
    }

    [Fact]
    public async Task Fail_Without_NgCode_Returns_400_With_Exact_Message()
    {
        SeedFixture();
        var qa = await QaAsync();

        var res = await qa.PostAsJsonAsync("/api/quality/results",
            new { serialNumber = "SN-QC-001", stationId = GetStationId(), verdict = "Fail" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ng_code required for fail verdict", body.GetProperty("error").GetString());

        using var db = _factory.CreateDb();
        var unit = db.Units.Single(u => u.SerialNumber == "SN-QC-001");
        Assert.False(db.QcResults.Any(q => q.UnitId == unit.Id)); // nothing persisted
        Assert.False(db.Repairs.Any(r => r.UnitId == unit.Id));
    }

    [Fact]
    public async Task Fail_With_NgCode_Creates_QcResult_And_Open_Repair()
    {
        SeedFixture();
        var qa = await QaAsync();

        var ngCodeId = GetNgCodeId(NgCode);
        var res = await qa.PostAsJsonAsync("/api/quality/results",
            new { serialNumber = "SN-QC-001", stationId = GetStationId(), verdict = "Fail", ngCodeId, notes = "LCD cracked" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("FAIL", body.GetProperty("result").GetString());
        Assert.True(body.GetProperty("qcResultId").GetInt32() > 0);
        Assert.True(body.GetProperty("repairId").GetInt32() > 0);

        using var db = _factory.CreateDb();
        var unit = db.Units.Single(u => u.SerialNumber == "SN-QC-001");
        var qc = Assert.Single(db.QcResults.Where(q => q.UnitId == unit.Id));
        Assert.Equal(QcVerdict.Fail, qc.Verdict);
        Assert.Equal(ngCodeId, qc.NgCodeId);
        var repair = Assert.Single(db.Repairs.Where(r => r.UnitId == unit.Id));
        Assert.Equal(RepairStatus.Open, repair.Status);
        Assert.Equal("LCD cracked", repair.ProblemDescription);

        var arrived = _factory.Broadcaster.Calls.Count > 0
            || await _factory.Broadcaster.WaitForCallAsync(TimeSpan.FromSeconds(2));
        Assert.True(arrived, "no broadcast within 2s");
        Assert.Contains(_factory.Broadcaster.Calls, c => c.StartsWith($"ScanRejected|SN-QC-001|{StationCode}|{LineCode}|qc fail: {NgCode}"));
    }

    [Fact]
    public async Task Fail_Without_Notes_Defaults_Problem_Description()
    {
        SeedFixture();
        var qa = await QaAsync();

        var res = await qa.PostAsJsonAsync("/api/quality/results",
            new { serialNumber = "SN-QC-001", stationId = GetStationId(), verdict = "Fail", ngCodeId = GetNgCodeId(NgCode) });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var db = _factory.CreateDb();
        var unit = db.Units.Single(u => u.SerialNumber == "SN-QC-001");
        Assert.Equal("QC fail", db.Repairs.Single(r => r.UnitId == unit.Id).ProblemDescription);
    }

    [Fact]
    public async Task Unknown_NgCode_Returns_400()
    {
        SeedFixture();
        var qa = await QaAsync();

        var missing = await qa.PostAsJsonAsync("/api/quality/results",
            new { serialNumber = "SN-QC-001", stationId = GetStationId(), verdict = "Fail", ngCodeId = 999999 });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var inactive = await qa.PostAsJsonAsync("/api/quality/results",
            new { serialNumber = "SN-QC-001", stationId = GetStationId(), verdict = "Fail", ngCodeId = GetNgCodeId("NG-QC-INACTIVE") });
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
    }

    [Fact]
    public async Task Invalid_Verdict_Returns_400()
    {
        SeedFixture();
        var qa = await QaAsync();

        var res = await qa.PostAsJsonAsync("/api/quality/results",
            new { serialNumber = "SN-QC-001", stationId = GetStationId(), verdict = "Maybe" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Unknown_Serial_Number_Is_Rejected_422()
    {
        SeedFixture();
        var qa = await QaAsync();

        var res = await qa.PostAsJsonAsync("/api/quality/results",
            new { serialNumber = "SN-GHOST", stationId = GetStationId(), verdict = "Pass" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("REJECTED", body.GetProperty("result").GetString());
        Assert.Equal("unknown serial number", body.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Unknown_Station_Returns_404()
    {
        SeedFixture();
        var qa = await QaAsync();

        var res = await qa.PostAsJsonAsync("/api/quality/results",
            new { serialNumber = "SN-QC-001", stationId = 999999, verdict = "Pass" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Operator_Is_Forbidden_403()
    {
        SeedFixture();
        _factory.EnsureSeedUsers();
        var login = await _anon.PostAsJsonAsync("/api/auth/login", new { username = "op1", password = "Op!pwd123" });
        var json = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync());
        var op = _factory.CreateClient();
        op.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());

        var res = await op.PostAsJsonAsync("/api/quality/results",
            new { serialNumber = "SN-QC-001", stationId = GetStationId(), verdict = "Pass" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    private int GetStationId()
    {
        using var db = _factory.CreateDb();
        return db.Stations.Single(s => s.Code == StationCode).Id;
    }

    private int GetNgCodeId(string code)
    {
        using var db = _factory.CreateDb();
        return db.NgCodes.Single(n => n.Code == code).Id;
    }

    public void Dispose() => _factory.Broadcaster.Calls.Clear();
}
