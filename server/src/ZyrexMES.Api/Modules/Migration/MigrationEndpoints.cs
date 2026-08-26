using Microsoft.AspNetCore.Builder;
using ZyrexMES.Api.Common;
using ZyrexMES.Infrastructure.Legacy;

namespace ZyrexMES.Api.Modules.Migration;

public sealed record ImportTransactionsRequest(DateTime FromUtc, DateTime ToUtc, string Source = "Staging", bool ConfirmLive = false);

public static class MigrationEndpoints
{
    public static void MapMigrationEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/migration");

        // Synchronous on purpose: staging volumes are small; no async job needed.
        g.MapPost("/import-master", async (LegacyImportService importer, CancellationToken ct) =>
            Results.Ok(await importer.ImportMasterDataAsync(ct)))
         .RequireRoles(Roles.Admin);

        g.MapPost("/import-transactions", async (ImportTransactionsRequest req, LegacyTransactionImporter importer, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await importer.ImportAsync(req.FromUtc, req.ToUtc, req.Source, req.ConfirmLive, ct));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (LiveImportNotApprovedException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        }).RequireRoles(Roles.Admin);
    }
}
