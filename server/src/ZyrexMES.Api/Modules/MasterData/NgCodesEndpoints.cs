using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZyrexMES.Api.Common;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Modules.MasterData;

public static class NgCodesEndpoints
{
    public static void MapNgCodesEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/ng-codes");

        g.MapGet("/", async (AppDbContext db) =>
            Results.Ok(await db.NgCodes
                .OrderBy(n => n.Code)
                .Select(n => new NgCodeDto(n.Id, n.Code, n.Description, n.IsActive))
                .ToListAsync()))
         .RequireAuthorization();

        g.MapPost("/", async (CreateNgCodeRequest req, AppDbContext db) =>
        {
            var err = Validate(req.Code, req.Description);
            if (err is not null) return Results.BadRequest(new { error = err });
            if (await db.NgCodes.AnyAsync(n => n.Code == req.Code))
                return Results.Conflict(new { error = "ng code already exists" });
            var ngCode = new NgCode { Code = req.Code!, Description = req.Description!, IsActive = true };
            db.NgCodes.Add(ngCode);
            await db.SaveChangesAsync();
            return Results.Created($"/api/ng-codes/{ngCode.Id}",
                new NgCodeDto(ngCode.Id, ngCode.Code, ngCode.Description, ngCode.IsActive));
        }).RequireRoles(Roles.Leader, Roles.Supervisor, Roles.Admin);
    }

    internal static string? Validate(string? code, string? description) =>
        string.IsNullOrWhiteSpace(code) || code.Length > 32 ? "invalid code (1..32 chars)" :
        string.IsNullOrWhiteSpace(description) || description.Length > 256 ? "invalid description (1..256 chars)" : null;
}
