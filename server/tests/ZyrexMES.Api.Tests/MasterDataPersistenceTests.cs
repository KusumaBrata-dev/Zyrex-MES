using Microsoft.EntityFrameworkCore;
using Npgsql;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Tests;

public class MasterDataPersistenceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly NpgsqlConnection _conn;

    public MasterDataPersistenceTests()
    {
        _conn = new NpgsqlConnection(
            "Host=localhost;Port=5433;Database=zyrex_mes;Username=postgres;Password=mes_dev_pwd");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();

        // Clean up leftovers from previous failed runs so reruns stay idempotent.
        foreach (var l in _db.Lines.Where(x => new[] { "L-T2A", "L-T3A", "L-T4A", "L-MD1", "L-MD2", "L-MD4" }.Contains(x.Code)).ToList())
            _db.Lines.Remove(l);
        _db.SaveChanges();
    }

    [Fact]
    public void Line_Code_Is_Unique_And_Cascade_Deletes_Stations()
    {
        _db.Lines.Add(new Line { Code = "L-T2A", Name = "Line T2 A", IsActive = true });
        _db.SaveChanges();
        var line = _db.Lines.Single(l => l.Code == "L-T2A");
        _db.Stations.Add(new Station { LineId = line.Id, Code = "ST-T2-ICT", Name = "ICT", ProcessType = "ICT", IsEnabled = true });
        _db.SaveChanges();

        var dup = new Line { Code = "L-T2A", Name = "dup", IsActive = true };
        _db.Lines.Add(dup);
        Assert.ThrowsAny<Exception>(() => _db.SaveChanges()); // unique index

        _db.Entry(dup).State = EntityState.Detached;
        _db.Lines.Remove(line);
        _db.SaveChanges();
        Assert.False(_db.Stations.Any(s => s.Code == "ST-T2-ICT")); // cascade
    }

    public void Dispose() => _conn.Dispose();
}
