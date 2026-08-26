using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ZyrexMES.Api.Common;
using ZyrexMES.Api.Modules.Production;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Modules.Printing;

public static class PrintingEndpoints
{
    private const int MaxClaimBatch = 10;
    private const int MaxAttempts = 3;

    public static void MapPrintingEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/print/jobs");

        // Atomic claim: SELECT ... FOR UPDATE SKIP LOCKED locks exactly the rows
        // this agent is about to take; concurrent agents skip them and pick the
        // next pending ones. The UPDATE runs in the same transaction, so a job
        // can never be handed to two agents (chosen over UPDATE..RETURNING,
        // which EF's ExecuteUpdate cannot surface).
        g.MapPost("/claim", async (int? stationId, AppDbContext db, CancellationToken ct) =>
        {
            await using var trx = await db.Database.BeginTransactionAsync(ct);
            var ids = await db.Database.SqlQuery<long>($"""
                SELECT "Id" AS "Value"
                FROM "PrintJobs"
                WHERE "Status" = 'Pending'
                  AND ({stationId} IS NULL OR "StationId" = {stationId})
                ORDER BY "Id"
                LIMIT {MaxClaimBatch}
                FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct);
            if (ids.Count > 0)
            {
                await db.PrintJobs
                    .Where(j => ids.Contains(j.Id))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.Status, "Sent")
                        .SetProperty(j => j.Attempts, j => j.Attempts + 1), ct);
            }
            await trx.CommitAsync(ct);

            var claimed = await db.PrintJobs
                .Where(j => ids.Contains(j.Id))
                .OrderBy(j => j.Id)
                .Select(j => new ClaimedJobDto(j.Id, j.TemplateCode, j.PayloadJson, j.Attempts))
                .ToListAsync(ct);
            return Results.Ok(claimed);
        }).RequireRoles(Roles.Agent);

        g.MapPost("/{id:long}/ack", async (long id, AckRequest req, AppDbContext db, IScanResultBroadcaster broadcaster, CancellationToken ct) =>
        {
            var job = await db.PrintJobs.FindAsync([id], ct);
            if (job is null)
                return Results.NotFound(new { error = "print job not found" });

            if (req.Ok)
            {
                job.Status = "Printed";
                job.CompletedAtUtc = DateTime.UtcNow;
            }
            else if (job.Attempts < MaxAttempts)
            {
                job.Status = "Pending"; // retry on next claim
            }
            else
            {
                job.Status = "Failed";
                job.CompletedAtUtc = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct);

            if (job.Status == "Failed")
                await broadcaster.BroadcastAlertAsync("print_failed", job.Id, req.Error, ct);
            return Results.Ok(new { status = job.Status });
        }).RequireRoles(Roles.Agent);
    }
}

public sealed record ClaimedJobDto(long Id, string TemplateCode, string PayloadJson, int Attempts);

public sealed record AckRequest(bool Ok, string? Error);
