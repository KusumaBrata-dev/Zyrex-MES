using Microsoft.EntityFrameworkCore;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Infrastructure.Legacy;

public sealed record ImportSummary(
    int LinesCreated,
    int StationsCreated,
    int ProductsCreated,
    int RoutingsCreated,
    int SkippedDuplicates,
    int SkippedMissingStation);

/// <summary>
/// Imports legacy staging snapshots (legacy_* tables) into the core master
/// tables. Guard: every imported code gets the "LGCY-" prefix so manual data is
/// never overwritten; imported rows are flagged Source="Legacy". The operation
/// is idempotent — existing rows are counted as duplicates, never rewritten.
/// </summary>
public sealed class LegacyImportService(AppDbContext db)
{
    public const string LegacySource = "Legacy";
    public const string CodePrefix = "LGCY-";

    public async Task<ImportSummary> ImportMasterDataAsync(CancellationToken ct = default)
    {
        // Save after each phase so later phases' existence checks see earlier
        // phases' inserts (DB queries do not return unsaved Added entities).
        var lines = await ImportLinesAsync(ct);
        await db.SaveChangesAsync(ct);
        var stations = await ImportStationsAsync(ct);
        await db.SaveChangesAsync(ct);
        var products = await ImportProductsAsync(ct);
        await db.SaveChangesAsync(ct);
        var routings = await ImportRoutingsAsync(ct);
        await db.SaveChangesAsync(ct);

        return new ImportSummary(
            lines.Created,
            stations.Created,
            products.Created,
            routings.Created,
            lines.Duplicates + stations.Duplicates + products.Duplicates + routings.Duplicates,
            stations.MissingReference + routings.MissingReference);
    }

    private async Task<(int Created, int Duplicates)> ImportLinesAsync(CancellationToken ct)
    {
        int created = 0, duplicates = 0;
        foreach (var s in await db.LegacyLineSnapshots.ToListAsync(ct))
        {
            var code = CodePrefix + s.LegacyCode;
            if (await db.Lines.AnyAsync(l => l.Code == code, ct))
            {
                duplicates++;
                continue;
            }
            db.Lines.Add(new Line { Code = code, Name = s.LegacyName, IsActive = true, Source = LegacySource });
            created++;
        }
        return (created, duplicates);
    }

    private async Task<(int Created, int Duplicates, int MissingReference)> ImportStationsAsync(CancellationToken ct)
    {
        int created = 0, duplicates = 0, missingReference = 0;
        foreach (var s in await db.LegacyStationSnapshots.ToListAsync(ct))
        {
            var code = CodePrefix + s.LegacyCode;
            if (await db.Stations.AnyAsync(x => x.Code == code, ct))
            {
                duplicates++;
                continue;
            }
            var line = await db.Lines.FirstOrDefaultAsync(l => l.Code == CodePrefix + s.LegacyLineCode, ct);
            if (line is null)
            {
                missingReference++; // staging references a line that was never staged/imported
                continue;
            }
            db.Stations.Add(new Station
            {
                LineId = line.Id,
                Code = code,
                Name = s.LegacyName,
                ProcessType = s.ProcessType,
                IsEnabled = true,
                Source = LegacySource,
            });
            created++;
        }
        return (created, duplicates, missingReference);
    }

    private async Task<(int Created, int Duplicates)> ImportProductsAsync(CancellationToken ct)
    {
        int created = 0, duplicates = 0;
        foreach (var s in await db.LegacyProductSnapshots.ToListAsync(ct))
        {
            var sku = CodePrefix + s.LegacySku;
            if (await db.Products.AnyAsync(p => p.Sku == sku, ct))
            {
                duplicates++;
                continue;
            }
            db.Products.Add(new Product { Sku = sku, Name = s.LegacyName, IsActive = true, Source = LegacySource });
            created++;
        }
        return (created, duplicates);
    }

    /// <summary>Row-level routing import: each staging row becomes one routing
    /// step under a per-SKU routing named "LGCY-&lt;sku&gt;". Idempotent at the
    /// (routing, sequence) grain.</summary>
    private async Task<(int Created, int Duplicates, int MissingReference)> ImportRoutingsAsync(CancellationToken ct)
    {
        int createdRows = 0, duplicates = 0, missingReference = 0;
        foreach (var s in await db.LegacyRoutingSnapshots.OrderBy(r => r.Sequence).ToListAsync(ct))
        {
            var product = await db.Products.FirstOrDefaultAsync(p => p.Sku == CodePrefix + s.LegacySku, ct);
            if (product is null)
            {
                missingReference++;
                continue;
            }
            var station = await db.Stations.FirstOrDefaultAsync(x => x.Code == CodePrefix + s.LegacyStationCode, ct);
            if (station is null)
            {
                missingReference++; // step's station absent from core — skip, never guess
                continue;
            }

            var routingName = CodePrefix + s.LegacySku;
            // Local first: a routing created earlier in THIS run is not yet in the DB.
            var routing = db.Routings.Local.FirstOrDefault(r => r.ProductId == product.Id && r.Name == routingName)
                ?? await db.Routings.Include(r => r.Steps)
                    .FirstOrDefaultAsync(r => r.ProductId == product.Id && r.Name == routingName, ct);
            if (routing is null)
            {
                routing = new Routing { ProductId = product.Id, Name = routingName, IsActive = true, Source = LegacySource };
                db.Routings.Add(routing);
            }

            if (routing.Steps.Any(st => st.Sequence == s.Sequence))
            {
                duplicates++;
                continue;
            }
            routing.Steps.Add(new RoutingStep { Sequence = s.Sequence, StationId = station.Id, RequireLabel = s.RequireLabel });
            createdRows++;
        }
        return (createdRows, duplicates, missingReference);
    }
}
