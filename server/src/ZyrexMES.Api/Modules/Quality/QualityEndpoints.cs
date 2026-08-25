using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using ZyrexMES.Api.Common;
using ZyrexMES.Api.Modules.Production;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Modules.Quality;

public static class QualityEndpoints
{
    private const int MaxNotesLength = 1024; // Repair.ProblemDescription column limit

    public static void MapQualityEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/quality");

        g.MapPost("/results", async (QcSubmitRequest req, AppDbContext db, IScanResultBroadcaster broadcaster, ClaimsPrincipal user, CancellationToken ct) =>
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

            var unit = await db.Units.FirstOrDefaultAsync(u => u.SerialNumber == req.SerialNumber, ct);
            if (unit is null)
            {
                await broadcaster.BroadcastRejectedAsync(req.SerialNumber, station.Code, lineCode, "unknown serial number", atUtc, ct);
                return Results.UnprocessableEntity(new { result = "REJECTED", reason = "unknown serial number" });
            }

            if (!TryParseVerdict(req.Verdict, out var verdict))
            {
                return Results.BadRequest(new { error = "verdict must be Pass or Fail" });
            }

            // AC-04: a fail verdict must name the defect.
            if (verdict == QcVerdict.Fail && req.NgCodeId is null)
            {
                return Results.BadRequest(new { error = "ng_code required for fail verdict" });
            }

            NgCode? ngCode = null;
            if (req.NgCodeId is not null)
            {
                ngCode = await db.NgCodes
                    .FirstOrDefaultAsync(n => n.Id == req.NgCodeId && n.IsActive, ct);
                if (ngCode is null)
                {
                    return Results.BadRequest(new { error = "ng_code not found or inactive" });
                }
            }

            if (req.Notes is { Length: > MaxNotesLength })
            {
                return Results.BadRequest(new { error = $"notes exceed {MaxNotesLength} characters" });
            }

            // Append-only QC result + repair queue entry in ONE commit.
            var qcResult = new QcResult
            {
                UnitId = unit.Id,
                StationId = station.Id,
                UserId = userId,
                Verdict = verdict,
                NgCodeId = ngCode?.Id,
                Notes = req.Notes,
                CheckedAtUtc = atUtc,
            };
            db.QcResults.Add(qcResult);

            Repair? repair = null;
            if (verdict == QcVerdict.Fail)
            {
                repair = new Repair
                {
                    UnitId = unit.Id,
                    ProblemDescription = req.Notes ?? "QC fail",
                    Status = RepairStatus.Open,
                    ReportedByUserId = userId,
                    ReportedAtUtc = atUtc,
                };
                db.Repairs.Add(repair);
            }

            await db.SaveChangesAsync(ct);

            // Broadcast only after a successful commit (consistent with scan flow).
            if (verdict == QcVerdict.Pass)
            {
                await broadcaster.BroadcastAcceptedAsync(req.SerialNumber, station.Code, lineCode, "PASS", atUtc, ct);
            }
            else
            {
                await broadcaster.BroadcastRejectedAsync(req.SerialNumber, station.Code, lineCode,
                    $"qc fail: {ngCode!.Code}", atUtc, ct);
            }

            return Results.Ok(new
            {
                result = verdict == QcVerdict.Pass ? "PASS" : "FAIL",
                qcResultId = qcResult.Id,
                repairId = repair?.Id,
            });
        }).RequireRoles(Roles.Qa, Roles.Leader, Roles.Supervisor, Roles.Admin);
    }

    private static bool TryParseVerdict(string? value, out QcVerdict verdict)
    {
        if (Enum.TryParse(value, ignoreCase: true, out verdict) && verdict is QcVerdict.Pass or QcVerdict.Fail)
        {
            return true;
        }
        verdict = default;
        return false;
    }
}
