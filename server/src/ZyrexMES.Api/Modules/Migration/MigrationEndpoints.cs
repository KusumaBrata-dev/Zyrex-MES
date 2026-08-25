using Microsoft.AspNetCore.Builder;
using ZyrexMES.Api.Common;
using ZyrexMES.Infrastructure.Legacy;

namespace ZyrexMES.Api.Modules.Migration;

public static class MigrationEndpoints
{
    public static void MapMigrationEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/migration");

        // Synchronous on purpose: staging volumes are small; no async job needed.
        g.MapPost("/import-master", async (LegacyImportService importer, CancellationToken ct) =>
            Results.Ok(await importer.ImportMasterDataAsync(ct)))
         .RequireRoles(Roles.Admin);
    }
}
