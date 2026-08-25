using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZyrexMES.Api.Common;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Modules.MasterData;

public static class ProductsEndpoints
{
    public static void MapProductsEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/products");

        g.MapGet("/", async (string? q, AppDbContext db) =>
            Results.Ok(await db.Products
                .Where(p => string.IsNullOrEmpty(q)
                            || p.Sku.ToLower().Contains(q.ToLower())
                            || p.Name.ToLower().Contains(q.ToLower()))
                .OrderBy(p => p.Sku)
                .Select(p => new ProductDto(p.Id, p.Sku, p.Name, p.Description, p.IsActive))
                .ToListAsync()))
         .RequireAuthorization();

        g.MapPost("/", async (CreateProductRequest req, AppDbContext db) =>
        {
            var err = Validate(req.Sku, req.Name, req.Description);
            if (err is not null) return Results.BadRequest(new { error = err });
            if (await db.Products.AnyAsync(p => p.Sku == req.Sku))
                return Results.Conflict(new { error = "product sku already exists" });
            var product = new Product
            {
                Sku = req.Sku!,
                Name = req.Name!,
                Description = req.Description,
                IsActive = true,
            };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            return Results.Created($"/api/products/{product.Id}",
                new ProductDto(product.Id, product.Sku, product.Name, product.Description, product.IsActive));
        }).RequireRoles(Roles.Leader, Roles.Supervisor, Roles.Admin);
    }

    internal static string? Validate(string? sku, string? name, string? description) =>
        string.IsNullOrWhiteSpace(sku) || sku.Length > 64 ? "invalid sku (1..64 chars)" :
        string.IsNullOrWhiteSpace(name) || name.Length > 100 ? "invalid name (1..100 chars)" :
        description is { Length: > 500 } ? "invalid description (max 500 chars)" : null;
}
