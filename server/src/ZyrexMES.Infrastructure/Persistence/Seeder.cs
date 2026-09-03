using Microsoft.Extensions.Configuration;
using ZyrexMES.Domain.Entities;

namespace ZyrexMES.Infrastructure.Persistence;

public static class Seeder
{
    /// <summary>
    /// Ensures the default production lines exist. Idempotent: only codes missing from the
    /// table are inserted, so it never duplicates and never clashes with test/other data.
    /// </summary>
    public static void SeedDefaults(AppDbContext db, IConfiguration cfg)
    {
        var codes = cfg.GetSection("Seed:DefaultLines").GetChildren()
                    .Select(c => c.Value).OfType<string>().ToArray();
        if (codes.Length == 0)
            codes = ["L01", "L02", "L03", "L04", "L05", "L06", "L07", "L08", "L09"];
        codes = codes.Distinct().ToArray();
        var existing = db.Lines.Select(l => l.Code).ToHashSet();
        var missing = codes.Where(c => !existing.Contains(c)).ToList();
        if (missing.Count == 0) return;
        db.Lines.AddRange(codes
            .Select((c, i) => (Code: c, Index: i))
            .Where(x => missing.Contains(x.Code))
            .Select(x => new Line { Code = x.Code, Name = $"Line {x.Index + 1:00}", IsActive = true }));
        db.SaveChanges();
    }
}
