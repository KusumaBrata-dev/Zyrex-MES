using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZyrexMES.Api.Common;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Modules.MasterData;

public static class StationsEndpoints
{
    public static void MapStationsEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/stations");

        g.MapGet("/", async (int? lineId, AppDbContext db) =>
            Results.Ok(await db.Stations
                .Where(s => lineId == null || s.LineId == lineId)
                .OrderBy(s => s.Code)
                .Select(s => new StationDto(s.Id, s.LineId, s.Code, s.Name, s.ProcessType, s.IsEnabled))
                .ToListAsync()))
         .RequireAuthorization();

        g.MapPost("/", async (CreateStationRequest req, AppDbContext db) =>
        {
            var err = Validate(req.Code, req.Name, req.ProcessType);
            if (err is not null) return Results.BadRequest(new { error = err });
            if (await db.Lines.FindAsync(req.LineId) is null)
                return Results.NotFound(new { error = "line not found" });
            if (await db.Stations.AnyAsync(s => s.LineId == req.LineId && s.Code == req.Code))
                return Results.Conflict(new { error = "station code already exists for this line" });
            var station = new Station
            {
                LineId = req.LineId,
                Code = req.Code!,
                Name = req.Name!,
                ProcessType = req.ProcessType,
                IsEnabled = true,
            };
            db.Stations.Add(station);
            await db.SaveChangesAsync();
            return Results.Created($"/api/stations/{station.Id}",
                new StationDto(station.Id, station.LineId, station.Code, station.Name, station.ProcessType, station.IsEnabled));
        }).RequireRoles(Roles.Leader, Roles.Supervisor, Roles.Admin);

        g.MapPut("/{id:int}", async (int id, UpdateStationRequest req, AppDbContext db) =>
        {
            var station = await db.Stations.FindAsync(id);
            if (station is null) return Results.NotFound();
            var err = Validate(req.Code, req.Name, req.ProcessType);
            if (err is not null) return Results.BadRequest(new { error = err });
            station.Code = req.Code!; station.Name = req.Name!;
            station.ProcessType = req.ProcessType; station.IsEnabled = req.IsEnabled;
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireRoles(Roles.Leader, Roles.Supervisor, Roles.Admin);

        g.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var station = await db.Stations.FindAsync(id);
            if (station is null) return Results.NotFound();
            station.IsEnabled = false;                   // soft delete — trail preserved (ISO)
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireRoles(Roles.Admin);
    }

    internal static string? Validate(string? code, string? name, string? processType) =>
        string.IsNullOrWhiteSpace(code) || code.Length > 64 ? "invalid code (1..64 chars)" :
        string.IsNullOrWhiteSpace(name) || name.Length > 100 ? "invalid name (1..100 chars)" :
        processType is { Length: > 32 } ? "invalid processType (max 32 chars)" : null;
}
