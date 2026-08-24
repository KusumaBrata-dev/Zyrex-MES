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
        // Products first: DB cascade removes Routings -> RoutingSteps, clearing the
        // RoutingSteps->Stations RESTRICT edge before Lines cascade into Stations.
        foreach (var p in _db.Products.Where(x => x.Sku == "ZX-TEST-001").ToList())
            _db.Products.Remove(p);
        _db.SaveChanges();
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

    [Fact]
    public void Routing_Steps_Are_Unique_Per_Routing_Sequence()
    {
        var product = new Product { Sku = "ZX-TEST-001", Name = "Test Model", IsActive = true };
        var line = new Line { Code = "L-T3A", Name = "T3 A", IsActive = true };
        var station = new Station { Line = line, Code = "ST-T3-ASM", Name = "Assembly", IsEnabled = true };
        _db.AddRange(product, line, station);
        _db.SaveChanges();

        var routing = new Routing { ProductId = product.Id, Name = "STD", IsActive = true };
        routing.Steps.Add(new RoutingStep { Routing = routing, Sequence = 10, StationId = station.Id, RequireLabel = false });
        routing.Steps.Add(new RoutingStep { Routing = routing, Sequence = 20, StationId = station.Id, RequireLabel = true });
        _db.Routings.Add(routing);
        _db.SaveChanges();
        Assert.Equal(2, _db.RoutingSteps.Count(s => s.RoutingId == routing.Id));

        routing.Steps.Add(new RoutingStep { Routing = routing, Sequence = 10, StationId = station.Id }); // duplicate sequence
        _db.RoutingSteps.Add(routing.Steps.Last());
        Assert.ThrowsAny<Exception>(() => _db.SaveChanges());
        _db.Entry(routing.Steps.Last()).State = EntityState.Detached;
    }

    public void Dispose() => _conn.Dispose();
}
