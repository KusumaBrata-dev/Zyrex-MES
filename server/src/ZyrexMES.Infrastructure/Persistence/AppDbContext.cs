using Microsoft.EntityFrameworkCore;
using ZyrexMES.Domain.Entities;

namespace ZyrexMES.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Line> Lines => Set<Line>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<BomItem> BomItems => Set<BomItem>();
    public DbSet<Routing> Routings => Set<Routing>();
    public DbSet<RoutingStep> RoutingSteps => Set<RoutingStep>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<UnitTransaction> UnitTransactions => Set<UnitTransaction>();
    public DbSet<QcResult> QcResults => Set<QcResult>();
    public DbSet<NgCode> NgCodes => Set<NgCode>();
    public DbSet<Repair> Repairs => Set<Repair>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<LegacyLineSnapshot> LegacyLineSnapshots => Set<LegacyLineSnapshot>();
    public DbSet<LegacyStationSnapshot> LegacyStationSnapshots => Set<LegacyStationSnapshot>();
    public DbSet<LegacyProductSnapshot> LegacyProductSnapshots => Set<LegacyProductSnapshot>();
    public DbSet<LegacyRoutingSnapshot> LegacyRoutingSnapshots => Set<LegacyRoutingSnapshot>();
    public DbSet<LegacyTransactionSnapshot> LegacyTransactionSnapshots => Set<LegacyTransactionSnapshot>();

    protected override void OnModelCreating(ModelBuilder b) => b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
