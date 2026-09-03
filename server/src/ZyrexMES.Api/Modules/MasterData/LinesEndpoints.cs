using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZyrexMES.Api.Common;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Modules.MasterData;

public static class LinesEndpoints
{
    public static void MapLinesEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/lines");

        g.MapGet("/", async (AppDbContext db, bool active = false) =>
            Results.Ok(await db.Lines
                .Where(l => !active || l.IsActive)
                .OrderBy(l => l.Code)
                .Select(l => new LineDto(l.Id, l.Code, l.Name, l.IsActive))
                .ToListAsync()))
         .RequireAuthorization();

        g.MapPost("/", async (CreateLineRequest req, AppDbContext db) =>
        {
            var err = Validate(req.Code, req.Name);
            if (err is not null) return Results.BadRequest(new { error = err });
            if (await db.Lines.AnyAsync(l => l.Code == req.Code))
                return Results.Conflict(new { error = "line code already exists" });
            var line = new Line { Code = req.Code!, Name = req.Name!, IsActive = true };
            db.Lines.Add(line);
            await db.SaveChangesAsync();
            return Results.Created($"/api/lines/{line.Id}",
                new LineDto(line.Id, line.Code, line.Name, line.IsActive));
        }).RequireRoles(Roles.Leader, Roles.Supervisor, Roles.Admin);

        g.MapPut("/{id:int}", async (int id, UpdateLineRequest req, AppDbContext db) =>
        {
            var line = await db.Lines.FindAsync(id);
            if (line is null) return Results.NotFound();
            var err = Validate(req.Code, req.Name);
            if (err is not null) return Results.BadRequest(new { error = err });
            if (await db.Lines.AnyAsync(l => l.Code == req.Code && l.Id != id))
                return Results.Conflict(new { error = "code already exists" });
            line.Code = req.Code!; line.Name = req.Name!; line.IsActive = req.IsActive;
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireRoles(Roles.Leader, Roles.Supervisor, Roles.Admin);

        g.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var line = await db.Lines.FindAsync(id);
            if (line is null) return Results.NotFound();
            line.IsActive = false;                       // soft delete — trail preserved (ISO)
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireRoles(Roles.Admin);
    }

    internal static string? Validate(string? code, string? name) =>
        string.IsNullOrWhiteSpace(code) || code.Length > 32 ? "invalid code (1..32 chars)" :
        string.IsNullOrWhiteSpace(name) || name.Length > 100 ? "invalid name (1..100 chars)" : null;
}
