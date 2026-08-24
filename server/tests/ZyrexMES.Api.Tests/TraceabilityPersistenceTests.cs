using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ZyrexMES.Api.Tests;

public class TraceabilityPersistenceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly NpgsqlConnection _conn;

    public TraceabilityPersistenceTests()
    {
        _conn = new NpgsqlConnection("Host=localhost;Port=5433;Database=zyrex_mes;Username=postgres;Password=mes_dev_pwd");
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_conn).Options);
        _db.Database.Migrate();

        // Clean up leftovers from previous runs so reruns stay idempotent.
        foreach (var u in _db.Units.Where(x => x.SerialNumber == "SN-T4-0001").ToList())
            _db.Units.Remove(u);
        _db.SaveChanges();
    }

    [Fact]
    public void Unit_SerialNumber_Is_Globally_Unique()
    {
        var product = EnsureProduct("ZX-T4-001", "M4");
        _db.Units.Add(new Unit { SerialNumber = "SN-T4-0001", ProductId = product.Id, Status = UnitStatus.Created, CreatedAtUtc = DateTime.UtcNow });
        _db.SaveChanges();
        _db.Units.Add(new Unit { SerialNumber = "SN-T4-0001", ProductId = product.Id, Status = UnitStatus.Created, CreatedAtUtc = DateTime.UtcNow });
        Assert.ThrowsAny<Exception>(() => _db.SaveChanges());
        _db.Entry(_db.Units.Local.Last()).State = EntityState.Detached;
    }

    [Fact]
    public void Transaction_Full_Chain_Persists()
    {
        // Self-contained: does not depend on data created by other tests.
        var product = EnsureProduct("ZX-T4-002", "M4B");
        var line = new Line { Code = "L-T4A", Name = "T4 A", IsActive = true };
        _db.Lines.Add(line);
        _db.SaveChanges();
        if (!_db.Stations.Any(s => s.Code == "ST-T4-ICT"))
            _db.Stations.Add(new Station { LineId = line.Id, Code = "ST-T4-ICT", Name = "ICT T4", ProcessType = "ICT", IsEnabled = true });
        _db.SaveChanges();
        var station = _db.Stations.Single(s => s.Code == "ST-T4-ICT");

        Unit unit;
        if (!_db.Units.Any(u => u.SerialNumber == "SN-T4-0002"))
        {
            unit = new Unit { SerialNumber = "SN-T4-0002", ProductId = product.Id, Status = UnitStatus.InProgress, CreatedAtUtc = DateTime.UtcNow };
            _db.Units.Add(unit);
            _db.SaveChanges();
        }
        else
        {
            unit = _db.Units.First(u => u.SerialNumber == "SN-T4-0002");
        }

        var tx = new UnitTransaction
        {
            UnitId = unit.Id, StationId = station.Id, UserId = SeedUserId(),
            ScannedAtUtc = DateTime.UtcNow, Result = QcVerdict.Pass,
        };
        _db.UnitTransactions.Add(tx);
        NgCode ngCode;
        if (!_db.NgCodes.Any(n => n.Code == "NG-LCD-CRK"))
        {
            ngCode = new NgCode { Code = "NG-LCD-CRK", Description = "LCD crack", IsActive = true };
            _db.NgCodes.Add(ngCode);
            _db.SaveChanges();
        }
        else
        {
            ngCode = _db.NgCodes.First(n => n.Code == "NG-LCD-CRK");
        }
        _db.QcResults.Add(new QcResult
        {
            UnitId = unit.Id, StationId = station.Id, UserId = tx.UserId,
            Verdict = QcVerdict.Fail, NgCodeId = ngCode.Id, Notes = "cracked panel",
            CheckedAtUtc = DateTime.UtcNow,
        });
        _db.Repairs.Add(new Repair
        {
            UnitId = unit.Id, ProblemDescription = "LCD cracked on ICT",
            Status = RepairStatus.Open, ReportedByUserId = tx.UserId, ReportedAtUtc = DateTime.UtcNow,
        });
        _db.SaveChanges();
        Assert.True(_db.Repairs.Any(r => r.UnitId == unit.Id));
    }

    private Product EnsureProduct(string sku, string name)
    {
        var existing = _db.Products.FirstOrDefault(p => p.Sku == sku);
        if (existing is not null) return existing;
        var product = new Product { Sku = sku, Name = name, IsActive = true };
        _db.Products.Add(product);
        _db.SaveChanges();
        return product;
    }

    private int SeedUserId()
    {
        // User entity arrives in Task 5; use id=1 without FK so the test focuses on the unit chain.
        return 1;
    }

    public void Dispose() => _conn.Dispose();
}
