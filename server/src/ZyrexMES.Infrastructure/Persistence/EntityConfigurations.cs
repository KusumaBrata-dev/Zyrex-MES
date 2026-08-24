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
