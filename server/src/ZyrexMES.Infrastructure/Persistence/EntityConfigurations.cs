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
