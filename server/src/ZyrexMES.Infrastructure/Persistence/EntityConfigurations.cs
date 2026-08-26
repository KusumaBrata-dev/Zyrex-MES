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
        b.Property(x => x.Source).HasMaxLength(16).HasDefaultValue("Manual");
    }
}

internal class StationConfig : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> b)
    {
        b.Property(x => x.Code).HasMaxLength(64);
        b.Property(x => x.ProcessType).HasMaxLength(32);
        b.HasIndex(x => new { x.LineId, x.Code }).IsUnique();
        b.Property(x => x.Source).HasMaxLength(16).HasDefaultValue("Manual");
        b.HasOne(x => x.Line).WithMany(l => l.Stations).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class ProductConfig : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(x => x.Sku).HasMaxLength(64);
        b.HasIndex(x => x.Sku).IsUnique();
        b.Property(x => x.Source).HasMaxLength(16).HasDefaultValue("Manual");
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
        b.Property(x => x.Source).HasMaxLength(16).HasDefaultValue("Manual");
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
        // Duplicate-scan guard for concurrent scans (TOCTOU backstop for the
        // app-level check). Deliberately NOT unique(UnitId, StationId): legacy
        // data may hold several transactions per pair at different times.
        b.HasIndex(x => new { x.UnitId, x.StationId, x.ScannedAtUtc }).IsUnique();
        b.Property(x => x.Result).HasConversion<string>().HasMaxLength(8);
        b.Property(x => x.ScannedAtUtc).HasColumnType("timestamptz");
        b.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class QcResultConfig : IEntityTypeConfiguration<QcResult>
{
    public void Configure(EntityTypeBuilder<QcResult> b)
    {
        b.Property(x => x.Verdict).HasConversion<string>().HasMaxLength(8);
        b.Property(x => x.CheckedAtUtc).HasColumnType("timestamptz");
        b.HasOne(x => x.NgCode).WithMany().HasForeignKey(x => x.NgCodeId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
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
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ReportedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

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

// Legacy MES staging snapshots (hybrid migration): flat FK-free copies of the
// vendor system; RawJson keeps the original payload as jsonb.
internal class LegacyLineSnapshotConfig : IEntityTypeConfiguration<LegacyLineSnapshot>
{
    public void Configure(EntityTypeBuilder<LegacyLineSnapshot> b)
    {
        b.ToTable("legacy_line_snapshots");
        b.Property(x => x.LegacyCode).HasMaxLength(64);
        b.Property(x => x.LegacyName).HasMaxLength(128);
        b.HasIndex(x => x.LegacyCode).IsUnique();
        b.Property(x => x.RawJson).HasColumnType("jsonb");
        b.Property(x => x.ImportedAtUtc).HasColumnType("timestamptz");
    }
}

internal class LegacyStationSnapshotConfig : IEntityTypeConfiguration<LegacyStationSnapshot>
{
    public void Configure(EntityTypeBuilder<LegacyStationSnapshot> b)
    {
        b.ToTable("legacy_station_snapshots");
        b.Property(x => x.LegacyLineCode).HasMaxLength(64);
        b.Property(x => x.LegacyCode).HasMaxLength(64);
        b.Property(x => x.LegacyName).HasMaxLength(128);
        b.Property(x => x.ProcessType).HasMaxLength(64);
        b.HasIndex(x => x.LegacyCode).IsUnique();
        b.Property(x => x.RawJson).HasColumnType("jsonb");
        b.Property(x => x.ImportedAtUtc).HasColumnType("timestamptz");
    }
}

internal class LegacyProductSnapshotConfig : IEntityTypeConfiguration<LegacyProductSnapshot>
{
    public void Configure(EntityTypeBuilder<LegacyProductSnapshot> b)
    {
        b.ToTable("legacy_product_snapshots");
        b.Property(x => x.LegacySku).HasMaxLength(64);
        b.Property(x => x.LegacyName).HasMaxLength(128);
        b.HasIndex(x => x.LegacySku).IsUnique();
        b.Property(x => x.RawJson).HasColumnType("jsonb");
        b.Property(x => x.ImportedAtUtc).HasColumnType("timestamptz");
    }
}

internal class LegacyRoutingSnapshotConfig : IEntityTypeConfiguration<LegacyRoutingSnapshot>
{
    public void Configure(EntityTypeBuilder<LegacyRoutingSnapshot> b)
    {
        b.ToTable("legacy_routing_snapshots");
        b.Property(x => x.LegacySku).HasMaxLength(64);
        b.Property(x => x.LegacyStationCode).HasMaxLength(64);
        b.Property(x => x.RawJson).HasColumnType("jsonb");
        b.Property(x => x.ImportedAtUtc).HasColumnType("timestamptz");
    }
}

internal class LegacyTransactionSnapshotConfig : IEntityTypeConfiguration<LegacyTransactionSnapshot>
{
    public void Configure(EntityTypeBuilder<LegacyTransactionSnapshot> b)
    {
        b.ToTable("legacy_transaction_snapshots");
        b.Property(x => x.SN).HasMaxLength(64);
        b.Property(x => x.StationCode).HasMaxLength(64);
        b.Property(x => x.ResultChar).HasMaxLength(1);
        b.Property(x => x.OperatorCode).HasMaxLength(64);
        b.HasIndex(x => new { x.SN, x.StationCode, x.ScannedAtUtc }).IsUnique();
        b.Property(x => x.RawJson).HasColumnType("jsonb");
        b.Property(x => x.ScannedAtUtc).HasColumnType("timestamptz");
        b.Property(x => x.ImportedAtUtc).HasColumnType("timestamptz");
    }
}
