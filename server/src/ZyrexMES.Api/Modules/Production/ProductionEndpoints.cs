using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ZyrexMES.Api.Common;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Modules.Production;

public static class ProductionEndpoints
{
    /// <summary>True when the update failed on a unique-constraint violation
    /// (Postgres 23505) — i.e. a concurrent scan won the TOCTOU race against
    /// the app-level duplicate check and the DB guard rejected the insert.</summary>
    public static bool IsDuplicateConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" };

    public static void MapProductionEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/production");

        // Operator and above (all authenticated roles).
        g.MapPost("/scan", async (ScanRequest req, AppDbContext db, IScanResultBroadcaster broadcaster, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdValue, out var userId))
            {
                return Results.Unauthorized();
            }

            var atUtc = DateTime.UtcNow;

            var station = await db.Stations.Include(s => s.Line)
                .FirstOrDefaultAsync(s => s.Id == req.StationId, ct);
            if (station is null)
            {
                return Results.NotFound(new { error = "station not found" });
            }
            var lineCode = station.Line.Code;

            // 1. Unknown serial number.
            var unit = await db.Units.FirstOrDefaultAsync(u => u.SerialNumber == req.SerialNumber, ct);
            if (unit is null)
            {
                return await RejectAsync(broadcaster, req.SerialNumber, station.Code, lineCode, "unknown serial number", atUtc, ct);
            }

            // 2/3. Active routing of the unit's product must contain this station.
            var routing = await db.Routings
                .Include(r => r.Steps)
                .Where(r => r.ProductId == unit.ProductId && r.IsActive)
                .FirstOrDefaultAsync(ct);
            var steps = routing?.Steps.OrderBy(st => st.Sequence).ToList();
            var index = steps?.FindIndex(st => st.StationId == station.Id) ?? -1;
            if (index < 0)
            {
                return await RejectAsync(broadcaster, req.SerialNumber, station.Code, lineCode, "station not in routing", atUtc, ct);
            }

            // 4. Routing order: the immediately preceding step must already be done.
            if (index > 0)
            {
                var previousStationId = steps![index - 1].StationId;
                var previousDone = await db.UnitTransactions
                    .AnyAsync(t => t.UnitId == unit.Id && t.StationId == previousStationId, ct);
                if (!previousDone)
                {
                    var expectedCode = await db.Stations
                        .Where(st => st.Id == previousStationId)
                        .Select(st => st.Code)
                        .FirstAsync(ct);
                    return await RejectAsync(broadcaster, req.SerialNumber, station.Code, lineCode,
                        $"routing order violation: expected station {expectedCode}", atUtc, ct);
                }
            }

            // 5. Duplicate scan at the same station.
            var duplicate = await db.UnitTransactions
                .AnyAsync(t => t.UnitId == unit.Id && t.StationId == station.Id, ct);
            if (duplicate)
            {
                return await RejectAsync(broadcaster, req.SerialNumber, station.Code, lineCode,
                    "duplicate transaction at this station", atUtc, ct);
            }

            // 6. Append-only transaction + unit status transition, single commit.
            var tx = new UnitTransaction
            {
                UnitId = unit.Id,
                StationId = station.Id,
                UserId = userId,
                ScannedAtUtc = atUtc,
                Result = QcVerdict.Pass,
                Notes = null,
            };
            db.UnitTransactions.Add(tx);
            unit.Status = index == steps!.Count - 1 ? UnitStatus.Completed : UnitStatus.InProgress;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsDuplicateConstraintViolation(ex))
            {
                // Concurrent scan won the TOCTOU race; the unique guard
                // (unit, station, scanned_at_utc) rejected the insert.
                await broadcaster.BroadcastRejectedAsync(req.SerialNumber, station.Code, lineCode,
                    "duplicate transaction at this station", atUtc, ct);
                return Results.UnprocessableEntity(new { result = "REJECTED", reason = "duplicate transaction at this station" });
            }

            string? nextStationCode = null;
            if (index < steps.Count - 1)
            {
                nextStationCode = await db.Stations
                    .Where(st => st.Id == steps[index + 1].StationId)
                    .Select(st => st.Code)
                    .FirstAsync(ct);
            }

            // Broadcast only after a successful commit.
            await broadcaster.BroadcastAcceptedAsync(req.SerialNumber, station.Code, lineCode, "PASS", atUtc, ct);

            return Results.Ok(new { result = "PASS", unitId = unit.Id, transactionId = tx.Id, nextStationCode });
        }).RequireRoles(Roles.Operator, Roles.Leader, Roles.Supervisor, Roles.Admin);
    }

    private static async Task<IResult> RejectAsync(
        IScanResultBroadcaster broadcaster, string serialNumber, string stationCode, string lineCode,
        string reason, DateTime atUtc, CancellationToken ct)
    {
        await broadcaster.BroadcastRejectedAsync(serialNumber, stationCode, lineCode, reason, atUtc, ct);
        return Results.UnprocessableEntity(new { result = "REJECTED", reason });
    }
}
