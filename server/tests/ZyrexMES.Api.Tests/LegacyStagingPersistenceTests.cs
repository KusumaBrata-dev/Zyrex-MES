using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Tests;

public class LegacyStagingPersistenceTests : IDisposable
{
    private const string Prefix = "LGCY-";
    private readonly AppDbContext _db;
    private readonly NpgsqlConnection _conn;

    public LegacyStagingPersistenceTests()
    {
        _conn = new NpgsqlConnection("Host=localhost;Port=5433;Database=zyrex_mes;Username=postgres;Password=mes_dev_pwd");
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_conn).Options);
        _db.Database.Migrate();

        // Clean up leftovers from previous runs so reruns stay idempotent.
        // Staging tables are FK-free flat copies, so deletion order does not matter.
        foreach (var t in _db.LegacyTransactionSnapshots.Where(x => x.SN.StartsWith(Prefix)).ToList())
            _db.LegacyTransactionSnapshots.Remove(t);
        foreach (var r in _db.LegacyRoutingSnapshots.Where(x => x.LegacySku.StartsWith(Prefix)).ToList())
            _db.LegacyRoutingSnapshots.Remove(r);
        foreach (var p in _db.LegacyProductSnapshots.Where(x => x.LegacySku.StartsWith(Prefix)).ToList())
            _db.LegacyProductSnapshots.Remove(p);
        foreach (var s in _db.LegacyStationSnapshots.Where(x => x.LegacyCode.StartsWith(Prefix)).ToList())
            _db.LegacyStationSnapshots.Remove(s);
        foreach (var l in _db.LegacyLineSnapshots.Where(x => x.LegacyCode.StartsWith(Prefix)).ToList())
            _db.LegacyLineSnapshots.Remove(l);
        _db.SaveChanges();
    }

    [Fact]
    public void Line_Snapshot_RoundTrips_With_Jsonb_RawJson()
    {
        const string raw = """{"code":"LGCY-L1","name":"Assy 1","extra":{"nested":true}}""";
        _db.LegacyLineSnapshots.Add(new LegacyLineSnapshot
        {
            LegacyCode = "LGCY-L1", LegacyName = "Assy 1", RawJson = raw, ImportedAtUtc = DateTime.UtcNow,
        });
        _db.SaveChanges();

        _db.ChangeTracker.Clear();
        var loaded = _db.LegacyLineSnapshots.Single(x => x.LegacyCode == "LGCY-L1");

        Assert.Equal("Assy 1", loaded.LegacyName);
        // jsonb normalizes formatting; compare semantically instead of raw text.
        Assert.True(JsonElement.DeepEquals(JsonDocument.Parse(loaded.RawJson).RootElement, JsonDocument.Parse(raw).RootElement));
    }

    [Fact]
    public void Line_Snapshot_LegacyCode_Is_Unique()
    {
        _db.LegacyLineSnapshots.Add(new LegacyLineSnapshot
        {
            LegacyCode = "LGCY-DUP", LegacyName = "first", RawJson = "{}", ImportedAtUtc = DateTime.UtcNow,
        });
        _db.SaveChanges();

        _db.ChangeTracker.Clear();
        _db.LegacyLineSnapshots.Add(new LegacyLineSnapshot
        {
            LegacyCode = "LGCY-DUP", LegacyName = "second", RawJson = "{}", ImportedAtUtc = DateTime.UtcNow,
        });
        Assert.ThrowsAny<Exception>(() => _db.SaveChanges());
    }

    [Fact]
    public void Transaction_Snapshot_Composite_Key_Is_Unique()
    {
        var scannedAt = new DateTime(2026, 8, 25, 1, 2, 3, DateTimeKind.Utc);
        _db.LegacyTransactionSnapshots.Add(NewTx("LGCY-TX1", scannedAt));
        _db.SaveChanges();

        _db.ChangeTracker.Clear();
        _db.LegacyTransactionSnapshots.Add(NewTx("LGCY-TX1", scannedAt));
        Assert.ThrowsAny<Exception>(() => _db.SaveChanges());
    }

    [Fact]
    public void All_Five_Snapshot_Tables_Persist()
    {
        var utc = DateTime.UtcNow;
        _db.LegacyLineSnapshots.Add(new LegacyLineSnapshot
        {
            LegacyCode = "LGCY-L2", LegacyName = "Line 2", RawJson = "{}", ImportedAtUtc = utc,
        });
        _db.LegacyStationSnapshots.Add(new LegacyStationSnapshot
        {
            LegacyLineCode = "LGCY-L2", LegacyCode = "LGCY-ST1", LegacyName = "ICT",
            ProcessType = "ICT", RawJson = "{}", ImportedAtUtc = utc,
        });
        _db.LegacyProductSnapshots.Add(new LegacyProductSnapshot
        {
            LegacySku = "LGCY-P001", LegacyName = "Model X", RawJson = "{}", ImportedAtUtc = utc,
        });
        _db.LegacyRoutingSnapshots.Add(new LegacyRoutingSnapshot
        {
            LegacySku = "LGCY-P001", Sequence = 10, LegacyStationCode = "LGCY-ST1",
            RequireLabel = true, RawJson = "{}", ImportedAtUtc = utc,
        });
        _db.LegacyTransactionSnapshots.Add(NewTx("LGCY-TX2", utc));
        _db.SaveChanges();

        Assert.True(_db.LegacyStationSnapshots.Any(x => x.LegacyCode == "LGCY-ST1" && x.ProcessType == "ICT"));
        Assert.True(_db.LegacyProductSnapshots.Any(x => x.LegacySku == "LGCY-P001"));
        Assert.True(_db.LegacyRoutingSnapshots.Any(x => x.LegacySku == "LGCY-P001" && x.Sequence == 10 && x.RequireLabel));
        Assert.True(_db.LegacyTransactionSnapshots.Any(x => x.SN == "LGCY-TX2" && x.ResultChar == "P" && x.OperatorCode == "OP1"));
    }

    private static LegacyTransactionSnapshot NewTx(string sn, DateTime scannedAt) => new()
    {
        SN = sn,
        StationCode = "LGCY-ST1",
        ResultChar = "P",
        ScannedAtUtc = scannedAt,
        OperatorCode = "OP1",
        RawJson = "{}",
        ImportedAtUtc = DateTime.UtcNow,
    };

    public void Dispose() => _conn.Dispose();
}
