# MES Foundation (Phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fondasi MES Zyrexindo yang jalan end-to-end: PostgreSQL+pgvector di Docker, skema domain lengkap, Auth JWT+Argon2id dengan RBAC 5 role, audit log append-only (ISO 9001), CRUD master data, dan SignalR hub siap pakai.

**Architecture:** Modular monolith ASP.NET Core 8 (`Domain` pure entities → `Infrastructure` EF Core/Npgsql → `Api` hosts/controllers/hubs). Satu database PostgreSQL 16 dengan ekstensi `vector`. Semua transaksi lewat REST; event realtime via SignalR `/hubs/production`.

**Tech Stack:** .NET 8 · EF Core 8 + Npgsql + pgvector · xUnit + `WebApplicationFactory` · Konscious.Argon2 · JWT Bearer · SignalR · Docker Compose.

**Spec:** `docs/specs/2026-08-24-mes-production-system-design.md` (AC-14, AC-15, AC-17 dijawab oleh plan ini; AC lainnya ada di Plan 2–5.)

## Global Constraints

- Prasyarat setiap sesi test/integration: `docker compose up -d db` harus sudah berjalan.
- Connection string dev: `Host=localhost;Port=5432;Database=zyrex_mes;Username=postgres;Password=mes_dev_pwd`.
- Port API dev: `http://localhost:8080`. Port DB: `5432`. Jangan ubah tanpa update semua task.
- Bahasa kode Inggris; komentar hanya bila trade-off non-obvious.
- Semua timestamp disimpan UTC (`DateTime.UtcNow`), tipe `timestamptz`.
- Secret tidak boleh hardcode di source: nilai dev via `appsettings.Development.json` + env `Jwt__Key`; produksi nanti env-only.
- Coverage target keseluruhan ≥ 80% (AC-17) — tiap task menyertakan tesnya.
- Commit conventional: `feat:`/`test:`/`chore:`/`fix:`/`docs:`.
- EF migrations dibuat dengan: `dotnet ef migrations add <Name> --project server/src/ZyrexMES.Infrastructure --startup-project server/src/ZyrexMES.Api`
- Nama solusi/proyek dikunci: `ZyrexMES.sln`, proyek `ZyrexMES.Domain`, `ZyrexMES.Infrastructure`, `ZyrexMES.Api`, `ZyrexMES.Api.Tests`.

---

### Task 1: Repo scaffold + Docker Compose + API hello-health

**Files:**
- Create: `.gitignore`, `README.md`, `docker-compose.yml`, `server/src/ZyrexMES.Api/ZyrexMES.Api.csproj`, `server/src/ZyrexMES.Api/Program.cs`, `server/src/ZyrexMES.Api/appsettings.json`, `server/src/ZyrexMES.Api/appsettings.Development.json`, `server/src/ZyrexMES.Domain/ZyrexMES.Domain.csproj`, `server/src/ZyrexMES.Infrastructure/ZyrexMES.Infrastructure.csproj`, `server/tests/ZyrexMES.Api.Tests/ZyrexMES.Api.Tests.csproj`
- Create: `server/ZyrexMES.sln`, `server/tests/ZyrexMES.Api.Tests/HealthEndpointTests.cs`

**Interfaces:**
- Produces: `GET /health` → `200 {"status":"ok"}`; `WebApplicationFactory<Program>` pattern untuk semua tes selanjutnya; `AppDbContext` tersedia via DI.

- [ ] **Step 1: Buat struktur + docker-compose**

`docker-compose.yml`:
```yaml
services:
  db:
    image: pgvector/pgvector:pg16
    environment:
      POSTGRES_DB: zyrex_mes
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: mes_dev_pwd
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 5s
      timeout: 3s
      retries: 10
volumes:
  pgdata:
```

`.gitignore`: standar Visual Studio (bin/, obj/, .vs/, *.user, appsettings.*.local.json, node_modules/ dist/ .next/).

`README.md`: cara run (`docker compose up -d db`, `dotnet run --project server/src/ZyrexMES.Api`) + prasyarat (.NET SDK 8, Docker).

- [ ] **Step 2: Scaffold solusi**

```powershell
cd D:\Code\Zyrex-MES\server
dotnet new sln -n ZyrexMES
dotnet new webapi -n ZyrexMES.Api -o src/ZyrexMES.Api --use-controllers false
dotnet new classlib -n ZyrexMES.Domain -o src/ZyrexMES.Domain
dotnet new classlib -n ZyrexMES.Infrastructure -o src/ZyrexMES.Infrastructure
dotnet new xunit -n ZyrexMES.Api.Tests -o tests/ZyrexMES.Api.Tests
dotnet sln add src/ZyrexMES.Api src/ZyrexMES.Domain src/ZyrexMES.Infrastructure tests/ZyrexMES.Api.Tests
dotnet add src/ZyrexMES.Infrastructure reference src/ZyrexMES.Domain
dotnet add src/ZyrexMES.Api reference src/ZyrexMES.Infrastructure
dotnet add tests/ZyrexMES.Api.Tests reference src/ZyrexMES.Api
```

Package (jalankan dari folder masing-masing proyek):
```powershell
# Infrastructure
dotnet add src/ZyrexMES.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/ZyrexMES.Infrastructure package Microsoft.EntityFrameworkCore.Design
# Api
dotnet add src/ZyrexMES.Api package Swashbuckle.AspNetCore
dotnet add src/ZyrexMES.Api package Microsoft.EntityFrameworkCore.Design
# Tests
dotnet add tests/ZyrexMES.Api.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/ZyrexMES.Api.Tests package Microsoft.EntityFrameworkCore.Design
```

Hapus file contoh bawaan template (`WeatherForecast*`, `Class1.cs`).

- [ ] **Step 3: Program.cs minimal + config**

`server/src/ZyrexMES.Api/Program.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using ZyrexMES.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();

public partial class Program { }
```

`appsettings.json`:
```json
{
  "ConnectionStrings": { "Default": "Host=localhost;Port=5432;Database=zyrex_mes;Username=postgres;Password=mes_dev_pwd" },
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*"
}
```
`appsettings.Development.json`: sama + `"Logging": { "LogLevel": { "Default": "Debug", "Microsoft.EntityFrameworkCore.Database.Command": "Information" } }`.

Buat stub `server/src/ZyrexMES.Infrastructure/Persistence/AppDbContext.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace ZyrexMES.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Entitas ditambahkan pada task berikutnya.
}
```

- [ ] **Step 4: Tulis tes health (ganti UnitTest1.cs)**

`server/tests/ZyrexMES.Api.Tests/HealthEndpointTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ZyrexMES.Api.Tests;

public class HealthEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Get_Health_Returns_Ok()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("ok", body!["status"]);
    }
}
```
Catatan: tes ini tidak membutuhkan DB karena `/health` tidak menyentuh `DbContext`.

- [ ] **Step 5: Jalankan tes**

Run: `dotnet test server/tests/ZyrexMES.Api.Tests -v n`
Expected: PASS (tes ini tidak butuh DB karena /health tidak menyentuh DbContext).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(api): solution scaffold, docker compose postgres+pgvector, health endpoint"
```

---

### Task 2: Entitas organisasi (Line, Station) + migrasi pertama

**Files:**
- Create: `server/src/ZyrexMES.Domain/Entities/Line.cs`, `server/src/ZyrexMES.Domain/Entities/Station.cs`
- Modify: `server/src/ZyrexMES.Infrastructure/Persistence/AppDbContext.cs`
- Create: `server/src/ZyrexMES.Infrastructure/Persistence/EntityConfigurations.cs`
- Create: migrasi `InitialOrganisation`
- Test: `server/tests/ZyrexMES.Api.Tests/MasterDataPersistenceTests.cs`

**Interfaces:**
- Produces:
  - `Line { int Id; string Code; string Name; bool IsActive; ICollection<Station> Stations }`
  - `Station { int Id; int LineId; string Code; string Name; string? ProcessType; bool IsEnabled; Line Line }`
  - DbSet: `DbSet<Line> Lines`, `DbSet<Station> Stations`

- [ ] **Step 1: Tulis failing test persistensi**

`MasterDataPersistenceTests.cs`:
```csharp
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
            "Host=localhost;Port=5432;Database=zyrex_mes;Username=postgres;Password=mes_dev_pwd");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();

        // Bersihkan sisa run gagal sebelumnya agar tes idempotent terhadap rerun.
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
```
Catatan: tes memakai DB nyata (prasyarat Global Constraints). `using Microsoft.Data.SqlClient;` tidak diperlukan — hapus baris itu saat menulis.

- [ ] **Step 2: Run → gagal compile (entitas belum ada)**

Run: `dotnet build server/ZyrexMES.sln`
Expected: BUILD ERROR (`Line` not found).

- [ ] **Step 3: Implementasi entitas + konfigurasi**

`server/src/ZyrexMES.Domain/Entities/Line.cs`:
```csharp
namespace ZyrexMES.Domain.Entities;

public class Line
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public ICollection<Station> Stations { get; set; } = new List<Station>();
}
```

`server/src/ZyrexMES.Domain/Entities/Station.cs`:
```csharp
namespace ZyrexMES.Domain.Entities;

public class Station
{
    public int Id { get; set; }
    public int LineId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? ProcessType { get; set; }
    public bool IsEnabled { get; set; } = true;
    public Line Line { get; set; } = null!;
}
```

Modify `AppDbContext.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using ZyrexMES.Domain.Entities;

namespace ZyrexMES.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Line> Lines => Set<Line>();
    public DbSet<Station> Stations => Set<Station>();

    protected override void OnModelCreating(ModelBuilder b) => b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
```

`server/src/ZyrexMES.Infrastructure/Persistence/EntityConfigurations.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZyrexMES.Domain.Entities;

namespace ZyrexMES.Infrastructure.Persistence;

internal class LineConfig : IEntityTypeConfiguration<Line>
{
    public void Configure(EntityTypeBuilder<Line> b)
    {
        b.Property(x => x.Code).HasMaxLength(32);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

internal class StationConfig : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> b)
    {
        b.Property(x => x.Code).HasMaxLength(64);
        b.Property(x => x.ProcessType).HasMaxLength(32);
        b.HasIndex(x => new { x.LineId, x.Code }).IsUnique();
        b.HasOne(x => x.Line).WithMany(l => l.Stations).OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 4: Buat migrasi + apply**

```powershell
cd D:\Code\Zyrex-MES\server
dotnet ef migrations add InitialOrganisation --project src/ZyrexMES.Infrastructure --startup-project src/ZyrexMES.Api
dotnet test tests/ZyrexMES.Api.Tests -v n
```
Expected: PASS (migrasi otomatis di-apply oleh `Database.Migrate()` dalam tes).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(domain): line & station entities with unique indexes + initial migration"
```

---

### Task 3: Entitas produk & routing

**Files:**
- Create: `server/src/ZyrexMES.Domain/Entities/Product.cs`, `BomItem.cs`, `Routing.cs`, `RoutingStep.cs`
- Modify: `AppDbContext.cs` (4 DbSet baru), `EntityConfigurations.cs`
- Migrasi: `AddProductRouting`
- Test: tambah metode ke `MasterDataPersistenceTests.cs`

**Interfaces:**
- Produces:
  - `Product { int Id; string Sku; string Name; string? Description; bool IsActive; ICollection<BomItem> BomItems; ICollection<Routing> Routings }`
  - `BomItem { int Id; int ProductId; string ComponentPart; decimal QtyPerUnit; Product Product }`
  - `Routing { int Id; int ProductId; string Name; bool IsActive; Product Product; ICollection<RoutingStep> Steps }`
  - `RoutingStep { int Id; int RoutingId; int Sequence; int StationId; bool RequireLabel; Station Station; Routing Routing }`
  - DbSet: `Products`, `BomItems`, `Routings`, `RoutingSteps`

- [ ] **Step 1: Failing test**

Tambahkan ke `MasterDataPersistenceTests.cs`:
```csharp
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

    routing.Steps.Add(new RoutingStep { Routing = routing, Sequence = 10, StationId = station.Id }); // duplikat urutan
    _db.RoutingSteps.Add(routing.Steps.Last());
    Assert.ThrowsAny<Exception>(() => _db.SaveChanges());
    _db.Entry(routing.Steps.Last()).State = EntityState.Detached;
}
```

- [ ] **Step 2: Run → FAIL** (`Product` not found, build error)

- [ ] **Step 3: Implementasi**

`Product.cs`:
```csharp
namespace ZyrexMES.Domain.Entities;

public class Product
{
    public int Id { get; set; }
    public string Sku { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<BomItem> BomItems { get; set; } = new List<BomItem>();
    public ICollection<Routing> Routings { get; set; } = new List<Routing>();
}
```

`BomItem.cs`:
```csharp
namespace ZyrexMES.Domain.Entities;

public class BomItem
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ComponentPart { get; set; } = null!;
    public decimal QtyPerUnit { get; set; }
    public Product Product { get; set; } = null!;
}
```

`Routing.cs`:
```csharp
namespace ZyrexMES.Domain.Entities;

public class Routing
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public Product Product { get; set; } = null!;
    public ICollection<RoutingStep> Steps { get; set; } = new List<RoutingStep>();
}
```

`RoutingStep.cs`:
```csharp
namespace ZyrexMES.Domain.Entities;

public class RoutingStep
{
    public int Id { get; set; }
    public int RoutingId { get; set; }
    public int Sequence { get; set; }
    public int StationId { get; set; }
    public bool RequireLabel { get; set; }
    public Routing Routing { get; set; } = null!;
    public Station Station { get; set; } = null!;
}
```

Tambahkan DbSet di `AppDbContext`:
```csharp
public DbSet<Product> Products => Set<Product>();
public DbSet<BomItem> BomItems => Set<BomItem>();
public DbSet<Routing> Routings => Set<Routing>();
public DbSet<RoutingStep> RoutingSteps => Set<RoutingStep>();
```

Tambahkan konfigurasi di `EntityConfigurations.cs`:
```csharp
internal class ProductConfig : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(x => x.Sku).HasMaxLength(64);
        b.HasIndex(x => x.Sku).IsUnique();
    }
}

internal class BomItemConfig : IEntityTypeConfiguration<BomItem>
{
    public void Configure(EntityTypeBuilder<BomItem> b)
    {
        b.Property(x => x.ComponentPart).HasMaxLength(64);
        b.HasOne(x => x.Product).WithMany(p => p.BomItems).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class RoutingConfig : IEntityTypeConfiguration<Routing>
{
    public void Configure(EntityTypeBuilder<Routing> b)
    {
        b.Property(x => x.Name).HasMaxLength(64);
        b.HasIndex(x => new { x.ProductId, x.Name }).IsUnique();
        b.HasOne(x => x.Product).WithMany(p => p.Routings).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class RoutingStepConfig : IEntityTypeConfiguration<RoutingStep>
{
    public void Configure(EntityTypeBuilder<RoutingStep> b)
    {
        b.HasIndex(x => new { x.RoutingId, x.Sequence }).IsUnique();
        b.HasOne(x => x.Routing).WithMany(r => r.Steps).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Station).WithMany().OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 4: Migrasi + test hijau**

```powershell
dotnet ef migrations add AddProductRouting --project src/ZyrexMES.Infrastructure --startup-project src/ZyrexMES.Api
dotnet test tests/ZyrexMES.Api.Tests -v n
```
Expected: PASS.

- [ ] **Step 5: Commit** — `git commit -m "feat(domain): product, bom, routing entities"`

---

### Task 4: Entitas unit produksi & QC (traceability core)

**Files:**
- Create: `server/src/ZyrexMES.Domain/Enums.cs`, `Entities/Unit.cs`, `Entities/UnitTransaction.cs`, `Entities/QcResult.cs`, `Entities/NgCode.cs`, `Entities/Repair.cs`
- Modify: `AppDbContext.cs`, `EntityConfigurations.cs`
- Migrasi: `AddProductionTraceability`
- Test: `server/tests/ZyrexMES.Api.Tests/TraceabilityPersistenceTests.cs`

**Interfaces:**
- Produces:
  - Enums: `enum UnitStatus { Created, InProgress, Completed, Scrapped }`, `enum QcVerdict { Pass, Fail }`, `enum RepairStatus { Open, InRepair, Verified, Closed }`
  - `Unit { int Id; string SerialNumber; int ProductId; UnitStatus Status; DateTime CreatedAtUtc; Product Product }` — SN unik global
  - `UnitTransaction { int Id; int UnitId; int StationId; int UserId; DateTime ScannedAtUtc; QcVerdict Result; string? Notes; Unit Unit }`
  - `QcResult { int Id; int UnitId; int StationId; int UserId; QcVerdict Verdict; int? NgCodeId; string? Notes; DateTime CheckedAtUtc; NgCode? NgCode }`
  - `NgCode { int Id; string Code; string Description; bool IsActive }` — Code unik
  - `Repair { int Id; int UnitId; string ProblemDescription; string? RootCause; RepairStatus Status; int ReportedByUserId; DateTime ReportedAtUtc; DateTime? ResolvedAtUtc; }`
  - DbSet: `Units`, `UnitTransactions`, `QcResults`, `NgCodes`, `Repairs`
- Ketentuan ISO: tabel transaksi **tidak punya Update/Delete di aplikasi** — entitas tidak diekspos untuk modifikasi (di-enforce lebih lanjut di Task 6/7).

- [ ] **Step 1: Failing test**

`TraceabilityPersistenceTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Tests;

public class TraceabilityPersistenceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly NpgsqlConnection _conn;

    public TraceabilityPersistenceTests()
    {
        _conn = new NpgsqlConnection("Host=localhost;Port=5432;Database=zyrex_mes;Username=postgres;Password=mes_dev_pwd");
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_conn).Options);
        _db.Database.Migrate();
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
        // Mandiri: tidak bergantung data dari tes lain.
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
        // User entity baru hadir di Task 5; sementara pakai id=1 tanpa FK agar tes fokus pada rantai unit.
        return 1;
    }

    public void Dispose() => _conn.Dispose();
}
```
Catatan desain sementara: `UnitTransaction.UserId` & `QcResult.UserId`/`ReportedByUserId` **tanpa FK constraint** ke users hingga Task 5 membuat entitas `AppUser`; Task 5 menambahkan FK via migrasi `AddUsersFk`.

- [ ] **Step 2: Run → FAIL (build error: Unit not found)**

- [ ] **Step 3: Implementasi**

`server/src/ZyrexMES.Domain/Enums.cs`:
```csharp
namespace ZyrexMES.Domain.Entities;

public enum UnitStatus { Created, InProgress, Completed, Scrapped }
public enum QcVerdict { Pass, Fail }
public enum RepairStatus { Open, InRepair, Verified, Closed }
```

`Unit.cs`:
```csharp
namespace ZyrexMES.Domain.Entities;

public class Unit
{
    public int Id { get; set; }
    public string SerialNumber { get; set; } = null!;
    public int ProductId { get; set; }
    public UnitStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Product Product { get; set; } = null!;
}
```

`UnitTransaction.cs`:
```csharp
namespace ZyrexMES.Domain.Entities;

public class UnitTransaction
{
    public int Id { get; set; }
    public int UnitId { get; set; }
    public int StationId { get; set; }
    public int UserId { get; set; }
    public DateTime ScannedAtUtc { get; set; }
    public QcVerdict Result { get; set; }
    public string? Notes { get; set; }
    public Unit Unit { get; set; } = null!;
}
```

`QcResult.cs`:
```csharp
namespace ZyrexMES.Domain.Entities;

public class QcResult
{
    public int Id { get; set; }
    public int UnitId { get; set; }
    public int StationId { get; set; }
    public int UserId { get; set; }
    public QcVerdict Verdict { get; set; }
    public int? NgCodeId { get; set; }
    public string? Notes { get; set; }
    public DateTime CheckedAtUtc { get; set; }
    public NgCode? NgCode { get; set; }
}
```

`NgCode.cs`:
```csharp
namespace ZyrexMES.Domain.Entities;

public class NgCode
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string Description { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}
```

`Repair.cs`:
```csharp
namespace ZyrexMES.Domain.Entities;

public class Repair
{
    public int Id { get; set; }
    public int UnitId { get; set; }
    public string ProblemDescription { get; set; } = null!;
    public string? RootCause { get; set; }
    public RepairStatus Status { get; set; }
    public int ReportedByUserId { get; set; }
    public DateTime ReportedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}
```

DbSet + konfigurasi:
```csharp
// AppDbContext
public DbSet<Unit> Units => Set<Unit>();
public DbSet<UnitTransaction> UnitTransactions => Set<UnitTransaction>();
public DbSet<QcResult> QcResults => Set<QcResult>();
public DbSet<NgCode> NgCodes => Set<NgCode>();
public DbSet<Repair> Repairs => Set<Repair>();
```

```csharp
internal class UnitConfig : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> b)
    {
        b.Property(x => x.SerialNumber).HasMaxLength(64);
        b.HasIndex(x => x.SerialNumber).IsUnique();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.CreatedAtUtc).HasColumnType("timestamptz");
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class UnitTransactionConfig : IEntityTypeConfiguration<UnitTransaction>
{
    public void Configure(EntityTypeBuilder<UnitTransaction> b)
    {
        b.HasIndex(x => new { x.UnitId, x.ScannedAtUtc });
        b.Property(x => x.Result).HasConversion<string>().HasMaxLength(8);
        b.Property(x => x.ScannedAtUtc).HasColumnType("timestamptz");
        b.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class QcResultConfig : IEntityTypeConfiguration<QcResult>
{
    public void Configure(EntityTypeBuilder<QcResult> b)
    {
        b.Property(x => x.Verdict).HasConversion<string>().HasMaxLength(8);
        b.Property(x => x.CheckedAtUtc).HasColumnType("timestamptz");
        b.HasOne(x => x.NgCode).WithMany().HasForeignKey(x => x.NgCodeId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal class NgCodeConfig : IEntityTypeConfiguration<NgCode>
{
    public void Configure(EntityTypeBuilder<NgCode> b)
    {
        b.Property(x => x.Code).HasMaxLength(32);
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Description).HasMaxLength(256);
    }
}

internal class RepairConfig : IEntityTypeConfiguration<Repair>
{
    public void Configure(EntityTypeBuilder<Repair> b)
    {
        b.Property(x => x.ProblemDescription).HasMaxLength(1024);
        b.Property(x => x.RootCause).HasMaxLength(1024);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.ReportedAtUtc).HasColumnType("timestamptz");
        b.Property(x => x.ResolvedAtUtc).HasColumnType("timestamptz");
    }
}
```

- [ ] **Step 4: Migrasi + test**

```powershell
dotnet ef migrations add AddProductionTraceability --project src/ZyrexMES.Infrastructure --startup-project src/ZyrexMES.Api
dotnet test tests/ZyrexMES.Api.Tests -v n
```
Expected: PASS.

- [ ] **Step 5: Commit** — `git commit -m "feat(domain): unit, transaction, qc, ng-code, repair entities for traceability"`

---

### Task 5: Users + Argon2id + JWT Auth endpoints

**Files:**
- Create: `server/src/ZyrexMES.Domain/Entities/AppUser.cs`
- Create: `server/src/ZyrexMES.Infrastructure/Security/PasswordHasher.cs`
- Create: `server/src/ZyrexMES.Api/Modules/Auth/AuthEndpoints.cs`, `AuthService.cs`, `TokenService.cs`, `LoginRequest.cs`
- Modify: `Program.cs` (register auth), `appsettings.json` (Jwt section), `EntityConfigurations.cs` (UserConfig + FK dari transaksi)
- Migrasi: `AddUsersAndFk`
- Test: `server/tests/ZyrexMES.Api.Tests/AuthEndpointTests.cs`

**Interfaces:**
- Produces:
  - `AppUser { int Id; string Username; string PasswordHash; string FullName; UserRole Role; bool IsActive; }`, `enum UserRole { Operator, Leader, Qa, Supervisor, Admin }` (di `ZyrexMES.Domain.Entities`)
  - `POST /api/auth/login` body `{ "username": "...", "password": "..." }` → `200 {"token":"<jwt>","user":{"username":"...","fullName":"...","role":"Admin"}}` atau `401`
  - `GET /api/auth/me` (bearer) → `{"username":"...","role":"..."}` atau `401`
  - `PasswordHasher.Hash(string)` / `.Verify(string password, string encodedHash)` statis
  - Konfigurasi Jwt: `"Jwt": { "Issuer": "zyrex-mes", "Audience": "zyrex-mes-clients", "ExpiryHours": 12 }`, key dari `Jwt__Key` env (fallback dev key HANYA di Development).
  - Claim types: `nameidentifier`=user id, `role`=UserRole string, `given_name`=FullName.
- FK: `UnitTransaction.UserId`, `QcResult.UserId`, `Repair.ReportedByUserId` → `users.id` (Restrict).

- [ ] **Step 1: Failing test**

`AuthEndpointTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ZyrexMES.Domain.Entities;

namespace ZyrexMES.Api.Tests;

public class AuthEndpointTests : IClassFixture<CustomWebAppFactory>, IDisposable
{
    private readonly CustomWebAppFactory _factory;
    private readonly HttpClient _client;

    public AuthEndpointTests(CustomWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        factory.EnsureSeedUsers();
    }

    [Fact]
    public async Task Login_Valid_Credentials_Returns_Token_And_Role()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Adm1n!pwd" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("token").GetString()));
        Assert.Equal("Admin", json.RootElement.GetProperty("user").GetProperty("role").GetString());
    }

    [Fact]
    public async Task Login_Wrong_Password_Returns_401()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "salah" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Me_Without_Token_Returns_401()
    {
        var res = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Me_With_Token_Returns_Profile()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { username = "op1", password = "Op!pwd123" });
        var json = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync());
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());
        var me = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = await JsonDocument.ParseAsync(await me.Content.ReadAsStreamAsync());
        Assert.Equal("Operator", body.RootElement.GetProperty("role").GetString());
    }

    public void Dispose() => _client.Dispose();
}
```

Create juga fixture bersama `CustomWebAppFactory.cs` (dipakai banyak tes):
```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Tests;

public class CustomWebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] =
                    "Host=localhost;Port=5432;Database=zyrex_mes;Username=postgres;Password=mes_dev_pwd",
                ["Jwt:Key"] = "unit-test-signing-key-0123456789abcdef-unit-test",
                ["Jwt:Issuer"] = "zyrex-mes-test",
                ["Jwt:Audience"] = "zyrex-mes-clients",
                ["Jwt:ExpiryHours"] = "12",
            }));
    }

    public AppDbContext CreateDb()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    /// <summary>Idempotent: dipanggil konstruktor tiap kelas tes yang butuh akun.</summary>
    public void EnsureSeedUsers()
    {
        using var db = CreateDb();
        db.Database.Migrate();
        void Ensure(string u, string p, string fullName, Domain.Entities.UserRole role)
        {
            if (!db.Users.Any(x => x.Username == u))
                db.Users.Add(new Domain.Entities.AppUser
                {
                    Username = u,
                    PasswordHash = Infrastructure.Security.PasswordHasher.Hash(p),
                    FullName = fullName,
                    Role = role,
                    IsActive = true,
                });
        }
        Ensure("admin", "Adm1n!pwd", "Sys Admin", Domain.Entities.UserRole.Admin);
        Ensure("leader1", "Lead!pwd12", "Leader Satu", Domain.Entities.UserRole.Leader);
        Ensure("op1", "Op!pwd123", "Operator Satu", Domain.Entities.UserRole.Operator);
        db.SaveChanges();
    }
}
```

- [ ] **Step 2: Run → FAIL** (endpoints belum ada → 404)

- [ ] **Step 3: Implementasi**

`AppUser.cs`:
```csharp
namespace ZyrexMES.Domain.Entities;

public enum UserRole { Operator, Leader, Qa, Supervisor, Admin }

public class AppUser
{
    public int Id { get; set; }
    public string Username { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
}
```

`PasswordHasher.cs` (package: `dotnet add src/ZyrexMES.Infrastructure package Konscious.Security.Cryptography.Argon2`):
```csharp
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace ZyrexMES.Infrastructure.Security;

public static class PasswordHasher
{
    // Format simpan: argon2id$<salt-b64>$<hash-b64>; param: m=19456,t=2,p=4 (OWASP baseline)
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Compute(password, salt);
        return $"argon2id${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encoded)
    {
        var parts = encoded.Split('$');
        if (parts.Length != 3 || parts[0] != "argon2id") return false;
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Compute(password, Convert.FromBase64String(parts[1]));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static byte[] Compute(string password, byte[] salt) =>
        new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt, MemorySize = 19456, Iterations = 2, DegreeOfParallelism = 4,
        }.GetBytes(32);
}
```

`TokenService.cs` (package Api: `Microsoft.AspNetCore.Authentication.JwtBearer`):
```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ZyrexMES.Domain.Entities;

namespace ZyrexMES.Api.Modules.Auth;

public interface ITokenService
{
    (string Token, DateTimeOffset ExpiresAt) Issue(AppUser user);
}

public class TokenService(IConfiguration cfg) : ITokenService
{
    public (string Token, DateTimeOffset ExpiresAt) Issue(AppUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTimeOffset.UtcNow.AddHours(double.Parse(cfg["Jwt:ExpiryHours"] ?? "12"));
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(ClaimTypes.GivenName, user.FullName),
        };
        var jwt = new JwtSecurityToken(cfg["Jwt:Issuer"], cfg["Jwt:Audience"], claims,
            expires: expires.UtcDateTime, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}
```

`AuthService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;
using ZyrexMES.Infrastructure.Security;

namespace ZyrexMES.Api.Modules.Auth;

public record LoginResponse(string Token, UserProfile User);
public record UserProfile(string Username, string FullName, string Role);

public class AuthService(AppDbContext db, ITokenService tokens)
{
    public async Task<LoginResponse?> LoginAsync(string username, string password, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive, ct);
        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash)) return null;
        var (token, _) = tokens.Issue(user);
        return new LoginResponse(token, new UserProfile(user.Username, user.FullName, user.Role.ToString()));
    }
}
```

`AuthEndpoints.cs` (minimal-API style, konsisten dipakai modul lain):
```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace ZyrexMES.Api.Modules.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", async (LoginRequest req, AuthService auth, CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(req.Username?.Trim() ?? "", req.Password ?? "", ct);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        }).AllowAnonymous();

        app.MapGet("/api/auth/me", (ClaimsPrincipal principal) =>
        {
            var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var name = principal.FindFirstValue(ClaimTypes.GivenName);
            var role = principal.FindFirstValue(ClaimTypes.Role);
            return Results.Ok(new { username = name, userId = int.Parse(id!), role });
        }).RequireAuthorization();
    }
}

public record LoginRequest(string? Username, string? Password);
```

`Program.cs` — tambahkan sebelum `builder.Build()`:
```csharp
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ZyrexMES.Api.Modules.Auth;

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();
```
dan setelah `app.UseSwaggerUI();`:
```csharp
app.UseAuthentication();
app.UseAuthorization();
app.MapAuthEndpoints();
```

`appsettings.json` — tambah:
```json
"Jwt": { "Issuer": "zyrex-mes", "Audience": "zyrex-mes-clients", "ExpiryHours": 12 }
```
Dev key via `appsettings.Development.json`:
```json
"Jwt:Key": "dev-only-key-do-not-use-in-prod-0123456789abcdef"
```

`UserConfig` + FK:
```csharp
internal class AppUserConfig : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("users");
        b.Property(x => x.Username).HasMaxLength(64);
        b.HasIndex(x => x.Username).IsUnique();
        b.Property(x => x.PasswordHash).HasMaxLength(512);
        b.Property(x => x.FullName).HasMaxLength(128);
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(16);
    }
}
```
Dan pada `UnitTransactionConfig`/`QcResultConfig`/`RepairConfig` tambahkan:
```csharp
b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
// Repair pakai HasForeignKey(x => x.ReportedByUserId)
```
DbSet: `public DbSet<AppUser> Users => Set<AppUser>();`

Migrasi: `dotnet ef migrations add AddUsersAndFk --project src/ZyrexMES.Infrastructure --startup-project src/ZyrexMES.Api`

- [ ] **Step 4: Run tests → PASS**

Run: `dotnet test server/tests/ZyrexMES.Api.Tests -v n`
Expected: PASS (semua 4 tes auth).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(auth): argon2id hashing, jwt login/me endpoints, rbac roles, user fk on traceability tables"
```

---

### Task 6: RBAC policies + proteksi endpoint admin (AC-15)

**Files:**
- Create: `server/src/ZyrexMES.Api/Common/AuthPolicies.cs`
- Test: `server/tests/ZyrexMES.Api.Tests/RbacAuthorizationTests.cs`

**Interfaces:**
- Produces: konstanta `Roles.Operator/Leader/Qa/Supervisor/Admin` (string); policy `RequireAuth` (semua role aktif); helper `endpoint.RequireRoles(Roles.Admin, Roles.Supervisor)`; perilaku: role tak sesuai → HTTP 403 (bukan 401).

- [ ] **Step 1: Failing test**

`RbacAuthorizationTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ZyrexMES.Api.Common;

namespace ZyrexMES.Api.Tests;

public class RbacAuthorizationTests(CustomWebAppFactory factory) : IClassFixture<CustomWebAppFactory>
{
    private async Task<string> TokenAsync(string u, string p)
    {
        factory.EnsureSeedUsers();
    {
        var c = factory.CreateClient();
        var res = await c.PostAsJsonAsync("/api/auth/login", new { username = u, password = p });
        var json = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private HttpClient Client(string token)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task Operator_Calling_AdminOnly_Endpoint_Gets_403()
    {
        var token = await TokenAsync("op1", "Op!pwd123"); // dari AuthEndpointTests seed
        var res = await Client(token).PostAsJsonAsync("/api/admin/ping", new { });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Admin_Calling_AdminOnly_Endpoint_Gets_200()
    {
        var token = await TokenAsync("admin", "Adm1n!pwd");
        var res = await Client(token).PostAsJsonAsync("/api/admin/ping", new { });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public void Role_Names_Match_Spec_Vocabulary()
    {
        Assert.Equal(["Operator", "Leader", "Qa", "Supervisor", "Admin"], Roles.All);
    }
}
```

- [ ] **Step 2: Run → FAIL (404 karena endpoint belum ada)**

- [ ] **Step 3: Implementasi**

`AuthPolicies.cs`:
```csharp
using Microsoft.AspNetCore.Authorization;

namespace ZyrexMES.Api.Common;

public static class Roles
{
    public const string Operator = "Operator";
    public const string Leader = "Leader";
    public const string Qa = "Qa";
    public const string Supervisor = "Supervisor";
    public const string Admin = "Admin";
    public static readonly string[] All = [Operator, Leader, Qa, Supervisor, Admin];
}

public static class EndpointRouteExtensions
{
    /// <summary>Izinkan hanya role tertentu; selain itu 403.</summary>
    public static RouteHandlerBuilder RequireRoles(this RouteHandlerBuilder b, params string[] roles)
        => b.RequireAuthorization(new AuthorizeAttribute { Roles = string.Join(',', roles) });
}
```

Di `Program.cs` (setelah MapAuthEndpoints) — endpoint probe sengaja kecil, hanya untuk gate RBAC:
```csharp
app.MapPost("/api/admin/ping", () => Results.Ok(new { pong = true }))
   .RequireRoles(Roles.Admin);
```
Catatan: endpoint `/api/admin/*` riil akan lahir di task CRUD; pola `.RequireRoles(...)` inilah yang dipakai semua endpoint berikutnya.

- [ ] **Step 4: Run → PASS**

- [ ] **Step 5: Commit** — `git commit -m "feat(auth): rbac role constants + RequireRoles policy, 403 semantics verified"`

---

### Task 7: Audit log append-only + middleware (AC-14)

**Files:**
- Create: `server/src/ZyrexMES.Domain/Entities/AuditLog.cs`
- Create: `server/src/ZyrexMES.Api/Common/AuditMiddleware.cs`
- Modify: `AppDbContext.cs` (DbSet), `EntityConfigurations.cs` (AuditLogConfig), `Program.cs` (middleware), migrasi `AddAuditLogAppendOnly` (berisi SQL trigger)
- Test: `server/tests/ZyrexMES.Api.Tests/AuditLogTests.cs`

**Interfaces:**
- Produces:
  - `AuditLog { long Id; int? UserId; string Action; string Method; string Path; int StatusCode; string? UserName; DateTime AtUtc }`
  - Middleware mencatat **setiap request non-GET** (action=HTTP verb) beserta user (jika terautentikasi) & status code.
  - DB-level guard: trigger `audit_logs_no_mutation` menolak `UPDATE`/`DELETE` dengan exception `audit_logs is append-only`.

- [ ] **Step 1: Failing test**

`AuditLogTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Tests;

public class AuditLogTests(CustomWebAppFactory factory) : IClassFixture<CustomWebAppFactory>
{
    [Fact]
    public async Task Non_Get_Request_Writes_Audit_Row()
    {
        var before = CountLogs(factory);
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Adm1n!pwd" });
        Assert.True(CountLogs(factory) > before);
    }

    [Fact]
    public async Task Update_On_Audit_Log_Is_Rejected_By_Database()
    {
        using var db = factory.CreateDb();
        var row = await db.AuditLogs.OrderBy(a => a.Id).FirstAsync();
        var ex = Assert.Throws<PostgresException>(() =>
            db.Database.ExecuteSql($"UPDATE audit_logs SET action='HACKED' WHERE id={row.Id}"));
        Assert.Contains("append-only", ex.MessageText);
    }

    private static int CountLogs(CustomWebAppFactory f)
    {
        using var db = f.CreateDb();
        return db.AuditLogs.Count();
    }
}
```

- [ ] **Step 2: Run → FAIL** (DbSet belum ada)

- [ ] **Step 3: Implementasi**

`AuditLog.cs`:
```csharp
namespace ZyrexMES.Domain.Entities;

public class AuditLog
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string Action { get; set; } = null!;     // HTTP verb atau nama operasi bisnis
    public string Method { get; set; } = null!;
    public string Path { get; set; } = null!;
    public int StatusCode { get; set; }
    public string? UserName { get; set; }
    public DateTime AtUtc { get; set; }
}
```

`AuditMiddleware.cs`:
```csharp
using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Common;

/// <summary>Mencatat request non-GET sebagai audit trail append-only.</summary>
public class AuditMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, AppDbContext db)
    {
        if (HttpMethods.IsGet(ctx.Request.Method))
        {
            await next(ctx);
            return;
        }

        var sw = Stopwatch.StartNew();
        Exception? failure = null;
        try { await next(ctx); }
        catch (Exception ex) { failure = ex; throw; }
        finally
        {
            sw.Stop();
            try
            {
                db.AuditLogs.Add(new Domain.Entities.AuditLog
                {
                    UserId = TryUserId(ctx),
                    Action = ctx.Request.Method,
                    Method = ctx.Request.Method,
                    Path = ctx.Request.Path.Value ?? "",
                    StatusCode = failure is null ? ctx.Response.StatusCode : 500,
                    UserName = ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value,
                    AtUtc = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }
            catch { /* audit tidak boleh mematikan request; logging via ILogger di production hardening */ }
        }
    }

    private static int? TryUserId(HttpContext ctx)
    {
        var v = ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(v, out var id) ? id : null;
    }
}
```
`Program.cs`: `app.UseMiddleware<AuditMiddleware>();` tepat setelah `app.UseAuthentication();`.

`AuditLogConfig` + trigger SQL di migrasi:
```csharp
internal class AuditLogConfig : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs");
        b.Property(x => x.Action).HasMaxLength(16);
        b.Property(x => x.Method).HasMaxLength(8);
        b.Property(x => x.Path).HasMaxLength(256);
        b.Property(x => x.UserName).HasMaxLength(128);
        b.Property(x => x.AtUtc).HasColumnType("timestamptz");
    }
}
```
Migrasi `AddAuditLogAppendOnly` — setelah scaffold, edit method `Up` tambahkan di akhir:
```csharp
migrationBuilder.Sql("""
CREATE OR REPLACE FUNCTION prevent_audit_mutation() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'audit_logs is append-only';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS audit_logs_no_mutation ON audit_logs;
CREATE TRIGGER audit_logs_no_mutation
BEFORE UPDATE OR DELETE ON audit_logs
FOR EACH ROW EXECUTE FUNCTION prevent_audit_mutation();
""");
```
dan di `Down`:
```csharp
migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_logs_no_mutation ON audit_logs; DROP FUNCTION IF EXISTS prevent_audit_mutation();");
```

- [ ] **Step 4: Migrasi + run → PASS**

```powershell
dotnet ef migrations add AddAuditLogAppendOnly --project src/ZyrexMES.Infrastructure --startup-project src/ZyrexMES.Api
dotnet test tests/ZyrexMES.Api.Tests -v n
```

- [ ] **Step 5: Commit** — `git commit -m "feat(audit): append-only audit_logs with db trigger guard + request middleware (iso 9001)"`

---

### Task 8: Master data CRUD endpoints (lines, stations, products, ng-codes)

**Files:**
- Create: `server/src/ZyrexMES.Api/Modules/MasterData/LinesEndpoints.cs`, `StationsEndpoints.cs`, `ProductsEndpoints.cs`, `NgCodesEndpoints.cs`, `Dtos.cs`
- Test: `server/tests/ZyrexMES.Api.Tests/MasterDataEndpointsTests.cs`

**Interfaces:**
- Produces (semua JSON, prefix `/api`):
  - Lines: `GET /lines` (list, query `?active=true`), `POST /lines` `{code,name}` → `201 {id,...}`, `PUT /lines/{id}` `{code,name,isActive}`, `DELETE /lines/{id}` = soft-deactivate (`isActive=false`) → `204`
  - Stations: `GET /stations?lineId=`, `POST /stations` `{lineId,code,name,processType}`, `PUT /stations/{id}`, `DELETE /stations/{id}`
  - Products: `GET /products?q=`, `POST /products` `{sku,name,description}`
  - NG codes: `GET /ng-codes`, `POST /ng-codes` `{code,description}`
  - Aturan validasi (400 dengan pesan): field kosong/melebihi panjang kolom; `lineId`/`productId` tak ditemukan → 404.
  - Hak akses: GET semua role login; POST/PUT/DELETE hanya `Leader, Supervisor, Admin`.
  - Duplikat code/SKU → HTTP 409.
- Dipakai Plan 2 (import station legacy + transaksi) dan Plan 3 (kiosk memuat konfigurasi station).

- [ ] **Step 1: Failing test**

`MasterDataEndpointsTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ZyrexMES.Api.Tests;

public class MasterDataEndpointsTests(CustomWebAppFactory factory) : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _anon = factory.CreateClient();

    private async Task<(HttpClient leader, HttpClient op)> ClientsAsync()
    {
        factory.EnsureSeedUsers();
        async Task<HttpClient> As(string u, string p)
        {
            var res = await _anon.PostAsJsonAsync("/api/auth/login", new { username = u, password = p });
            var json = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
            var c = factory.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());
            return c;
        }
        return (await As("leader1", "Lead!pwd12"), await As("op1", "Op!pwd123"));
    }

    [Fact]
    public async Task Leader_Can_Create_Line_And_Read_Back()
    {
        var (leader, _) = await ClientsAsync();
        var create = await leader.PostAsJsonAsync("/api/lines", new { code = "L-MD1", name = "MD One" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var list = await leader.GetFromJsonAsync<JsonElement>("/api/lines");
        Assert.Contains(list.EnumerateArray(), e => e.GetProperty("code").GetString() == "L-MD1");
    }

    [Fact]
    public async Task Duplicate_Line_Code_Returns_409()
    {
        var (leader, _) = await ClientsAsync();
        await leader.PostAsJsonAsync("/api/lines", new { code = "L-MD2", name = "MD Two" });
        var dup = await leader.PostAsJsonAsync("/api/lines", new { code = "L-MD2", name = "again" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task Operator_Cannot_Create_Line_403()
    {
        var (_, op) = await ClientsAsync();
        var res = await op.PostAsJsonAsync("/api/lines", new { code = "L-MD3", name = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Cannot_List_Lines_401()
    {
        var res = await _anon.GetAsync("/api/lines");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Delete_Line_Deactivates_Softly()
    {
        var (leader, _) = await ClientsAsync();
        var create = await leader.PostAsJsonAsync("/api/lines", new { code = "L-MD4", name = "MD Four" });
        var body = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetInt32();
        var del = await leader.DeleteAsync($"/api/lines/{id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        var list = await leader.GetFromJsonAsync<JsonElement>("/api/lines?active=true");
        Assert.DoesNotContain(list.EnumerateArray(), e => e.GetProperty("code").GetString() == "L-MD4");
    }

    [Fact]
    public async Task Create_Station_Under_Line_Works_And_404_For_Bad_Line()
    {
        var (leader, _) = await ClientsAsync();
        var ok = await leader.PostAsJsonAsync("/api/stations", new { lineId = 1, code = "ST-MD-QC", name = "QC MD", processType = "QC" });
        Assert.True(ok.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict); // idempotent utk rerun
        var bad = await leader.PostAsJsonAsync("/api/stations", new { lineId = 999999, code = "ST-X", name = "X" });
        Assert.Equal(HttpStatusCode.NotFound, bad.StatusCode);
    }
}
```

Catatan: `EnsureSeedUsers()` pada fixture bersifat idempotent sehingga semua kelas tes aman berjalan dalam urutan apa pun.

- [ ] **Step 2: Run → FAIL (404 semua)**

- [ ] **Step 3: Implementasi**

`Dtos.cs`:
```csharp
namespace ZyrexMES.Api.Modules.MasterData;

public record LineDto(int Id, string Code, string Name, bool IsActive);
public record CreateLineRequest(string? Code, string? Name);
public record UpdateLineRequest(string? Code, string? Name, bool IsActive);

public record StationDto(int Id, int LineId, string Code, string Name, string? ProcessType, bool IsEnabled);
public record CreateStationRequest(int LineId, string? Code, string? Name, string? ProcessType);
public record UpdateStationRequest(string? Code, string? Name, string? ProcessType, bool IsEnabled);

public record ProductDto(int Id, string Sku, string Name, string? Description, bool IsActive);
public record CreateProductRequest(string? Sku, string? Name, string? Description);

public record NgCodeDto(int Id, string Code, string Description, bool IsActive);
public record CreateNgCodeRequest(string? Code, string? Description);
```

`LinesEndpoints.cs`:
```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZyrexMES.Api.Common;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Modules.MasterData;

public static class LinesEndpoints
{
    public static void MapLinesEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/lines");

        g.MapGet("/", async (bool active, AppDbContext db) =>
            Results.Ok(await db.Lines
                .Where(l => !active || l.IsActive)
                .OrderBy(l => l.Code)
                .Select(l => new LineDto(l.Id, l.Code, l.Name, l.IsActive))
                .ToListAsync()))
         .RequireAuthorization();

        g.MapPost("/", async (CreateLineRequest req, AppDbContext db) =>
        {
            var err = Validate(req.Code, req.Name);
            if (err is not null) return Results.BadRequest(new { error = err });
            if (await db.Lines.AnyAsync(l => l.Code == req.Code))
                return Results.Conflict(new { error = "line code already exists" });
            var line = new Line { Code = req.Code!, Name = req.Name!, IsActive = true };
            db.Lines.Add(line);
            await db.SaveChangesAsync();
            return Results.Created($"/api/lines/{line.Id}",
                new LineDto(line.Id, line.Code, line.Name, line.IsActive));
        }).RequireRoles(Roles.Leader, Roles.Supervisor, Roles.Admin);

        g.MapPut("/{id:int}", async (int id, UpdateLineRequest req, AppDbContext db) =>
        {
            var line = await db.Lines.FindAsync(id);
            if (line is null) return Results.NotFound();
            var err = Validate(req.Code, req.Name);
            if (err is not null) return Results.BadRequest(new { error = err });
            line.Code = req.Code!; line.Name = req.Name!; line.IsActive = req.IsActive;
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireRoles(Roles.Leader, Roles.Supervisor, Roles.Admin);

        g.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var line = await db.Lines.FindAsync(id);
            if (line is null) return Results.NotFound();
            line.IsActive = false;                       // soft delete — jejak tetap (ISO)
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireRoles(Roles.Admin);
    }

    internal static string? Validate(string? code, string? name) =>
        string.IsNullOrWhiteSpace(code) || code.Length > 32 ? "invalid code (1..32 chars)" :
        string.IsNullOrWhiteSpace(name) || name.Length > 100 ? "invalid name (1..100 chars)" : null;
}
```

Tiga endpoint group lainnya WAJIB dibuat dengan struktur kode yang sama persis dengan `LinesEndpoints` (group + GET list + POST + PUT + DELETE soft-state + helper `Validate`). Kontrak eksplisit per group:

| Group | Route & Verb | Body (request) | Validasi khusus | Status sukses |
|---|---|---|---|---|
| `/api/stations` | `GET ?lineId=` | — | filter opsional by lineId | 200 |
| | `POST` | `{lineId, code, name, processType}` | line ada? → else **404**; panjang: code≤64, name≤100, processType≤32; unik `(LineId, Code)` → **409** | 201 |
| | `PUT /{id}` | `{code, name, processType, isEnabled}` | id tak ada → 404; validasi sama | 204 |
| | `DELETE /{id}` | — | soft: `IsEnabled=false`; role Admin saja | 204 |
| `/api/products` | `GET ?q=` | — | `q` = contains pada sku/name (case-insensitive) | 200 |
| | `POST` | `{sku, name, description}` | sku≤64 unik→409; name≤100; description≤500 | 201 |
| `/api/ng-codes` | `GET` | — | hanya aktif+nonaktif semua (list polos) | 200 |
| | `POST` | `{code, description}` | code≤32 unik→409; description≤256 | 201 |

Semua POST/PUT/DELETE memakai `.RequireRoles(Roles.Leader, Roles.Supervisor, Roles.Admin)` kecuali DELETE station/product/ng-code = Admin. Semua GET = `.RequireAuthorization()`. DTO record didefinisikan lengkap di `Dtos.cs` (sudah ditulis di atas).

`Program.cs`:
```csharp
using ZyrexMES.Api.Modules.MasterData;
app.MapLinesEndpoints();
app.MapStationsEndpoints();
app.MapProductsEndpoints();
app.MapNgCodesEndpoints();
```

- [ ] **Step 4: Run → PASS**

Run: `dotnet test server/tests/ZyrexMES.Api.Tests -v n`
Expected: PASS semua (rerun aman: tes pakai code unik per-run bila perlu; 409-or-Created sudah mengantisipasi rerun).

- [ ] **Step 5: Commit** — `git commit -m "feat(masterdata): crud endpoints lines/stations/products/ng-codes with rbac + conflict handling"`

---

### Task 9: SignalR ProductionHub + seed 9 line

**Files:**
- Create: `server/src/ZyrexMES.Api/Hubs/ProductionHub.cs`
- Create: `server/src/ZyrexMES.Infrastructure/Persistence/Seeder.cs`
- Modify: `Program.cs` (MapHub + seeder call), `appsettings.json` (`Seed:DefaultLines`)
- Test: `server/tests/ZyrexMES.Api.Tests/SeedTests.cs`

**Interfaces:**
- Produces:
  - Hub path `/hubs/production`; method klien yang akan dipanggil Plan 3–4: `ScanAccepted(UnitScanEventDto)`, `ScanRejected(UnitScanEventDto)`, `AlertRaised(AlertDto)` — DTO di Plan 2/4; Task ini cukup kontrak nama + tes koneksi.
  - `Seeder.SeedDefaults(AppDbContext, IConfiguration)`: membuat 9 line `L01..L09` (nama `Line 01..09`) bila tabel lines kosong; membaca daftar dari `Seed:DefaultLines` (default `["L01","L02","L03","L04","L05","L06","L07","L08","L09"]`).
  - Dipanggil sekali saat startup (`if (db.Lines.Count() == 0)`), idempotent.

- [ ] **Step 1: Failing test**

`SeedTests.cs`:
```csharp
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;

namespace ZyrexMES.Api.Tests;

public class SeedTests(CustomWebAppFactory factory) : IClassFixture<CustomWebAppFactory>
{
    [Fact]
    public void Startup_Seeds_Nine_Lines_Exactly_Once()
    {
        using var db = factory.CreateDb();
        Assert.True(db.Lines.Count() >= 9);
        Assert.Equal(9, db.Lines.Count(l => l.Code.StartsWith('L') && l.Code.Length == 3 && char.IsDigit(l.Code[1]) && char.IsDigit(l.Code[2])));
    }

    [Fact]
    public async Task SignalR_Hub_Is_Reachable()
    {
        var baseUri = factory.ClientOptions.BaseAddress!.ToString().Replace("http://", "ws://");
        var conn = new HubConnectionBuilder().WithUrl($"{baseUri}hubs/production").Build();
        await conn.StartAsync();
        Assert.Equal(HubConnectionState.Connected, conn.State);
        await conn.DisposeAsync();
    }
}
```

- [ ] **Step 2: Run → FAIL**

- [ ] **Step 3: Implementasi**

`ProductionHub.cs` (package Api: `Microsoft.AspNetCore.SignalR` sudah bawaan framework):
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ZyrexMES.Api.Hubs;

[Authorize]
public class ProductionHub : Hub
{
    // Klien (dashboard/kiosk) join group per line utk filtering murah di sisi server.
    public Task JoinLine(string lineCode) => Groups.AddToGroupAsync(Context.ConnectionId, $"line:{lineCode}");
    public Task LeaveLine(string lineCode) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"line:{lineCode}");
}
```

`Seeder.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ZyrexMES.Domain.Entities;

namespace ZyrexMES.Infrastructure.Persistence;

public static class Seeder
{
    public static void SeedDefaults(AppDbContext db, IConfiguration cfg)
    {
        if (db.Lines.Any()) return;
        var codes = cfg.GetSection("Seed:DefaultLines").Get<string[]>()
                   ?? ["L01", "L02", "L03", "L04", "L05", "L06", "L07", "L08", "L09"];
        db.Lines.AddRange(codes.Select((c, i) => new Line { Code = c, Name = $"Line {i + 1:00}", IsActive = true }));
        db.SaveChanges();
    }
}
```

`Program.cs` — setelah build:
```csharp
using Microsoft.AspNetCore.SignalR;
using ZyrexMES.Api.Hubs;
using ZyrexMES.Infrastructure.Persistence;

app.MapHub<Hubs.ProductionHub>("/hubs/production");
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    Seeder.SeedDefaults(db, app.Configuration);
}
```
`appsettings.json`:
```json
"Seed": { "DefaultLines": ["L01","L02","L03","L04","L05","L06","L07","L08","L09"] }
```

- [ ] **Step 4: Run → PASS**

Run: `dotnet test server/tests/ZyrexMES.Api.Tests -v n` → PASS semua suite.

- [ ] **Step 5: Commit** — `git commit -m "feat(realtime): production signalr hub + idempotent nine-line seeding"`

---

### Task 10: Smoke manual + README finalisasi + tag

**Files:**
- Modify: `README.md` (bagian "Quick start" lengkap + troubleshooting)

**Interfaces:** — (deliverable dokumentasi + bukti smoke)

- [ ] **Step 1: Full clean run**

```powershell
docker compose down -v; docker compose up -d db
Start-Sleep -Seconds 8
dotnet build server/ZyrexMES.sln
dotnet test server/tests/ZyrexMES.Api.Tests -v n
dotnet run --project server/src/ZyrexMES.Api
# browser kedua: http://localhost:8080/health → {"status":"ok"}
# http://localhost:8080/swagger → semua grup endpoint tampak
```
Expected: build 0 error, semua tes hijau, swagger hidup.

- [ ] **Step 2: Tulis Quick Start di README**

Isi minimal (verbatim):
```markdown
## Quick Start
1. Prasyarat: .NET SDK 8, Docker Desktop, Git.
2. `docker compose up -d db`  (PostgreSQL 16 + pgvector di localhost:5432)
3. `dotnet ef database update --project server/src/ZyrexMES.Infrastructure --startup-project server/src/ZyrexMES.Api`
4. `dotnet run --project server/src/ZyrexMES.Api`  → http://localhost:8080/swagger
5. Tes: `dotnet test server/tests/ZyrexMES.Api.Tests`
Akun seed awal dibuat lewat endpoint register admin di Plan 2; untuk dev gunakan SQL insert manual sesuai docs/dev-seed.md.
```

- [ ] **Step 3: Commit + tag milestone**

```bash
git add README.md
git commit -m "docs(readme): quick start and prerequisites"
git tag phase1-foundation-complete
```

---

## Peta Lanjutan (plan berikutnya — jangan dieksekusi dari plan ini)

- **Plan 2 — Legacy Migration & Station Transactions**: ETL baca DB MES lama (line/station/product/routing/unit historis) → rekonsiliasi row-count + sampling 100 SN (AC-07, AC-08); endpoint scan `/api/production/scan` dengan validasi routing & duplikat (AC-01..AC-03); QC submit dengan ng_code wajib (AC-04); broadcast SignalR ScanAccepted/Rejected ≤2 detik (AC-06); overlay offline di sisi kiosk menyusul Plan 3.
- **Plan 3 — Print Agent & Station Kiosk**: Windows service .NET 8 polling/WSS job cetak → BarTender CLI/SDK (Honeywell/Panda/Zebra) retry 3× + alert (AC-05); Next.js kiosk PWA + overlay SERVER OFFLINE blocking (AC-13).
- **Plan 4 — Dashboard Realtime & Reports**: monitoring 9 line, yield/throughput/WIP/NG Pareto, Andon TV, export Excel/PDF, alert anomali threshold (AC-12).
- **Plan 5 — Local AI Assistant**: Ollama + Qwen2.5-14B Q4 di server; RAG SOP pgvector dengan sitasi versi (AC-10); NL→SQL guarded read-only whitelist (AC-11); isolasi outbound nol (AC-09); analisa akar masalah repair.
