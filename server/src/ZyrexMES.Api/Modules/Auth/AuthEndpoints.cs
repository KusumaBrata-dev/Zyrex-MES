using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace ZyrexMES.Api.Modules.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", async (LoginRequest req, AuthService auth, CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(req.Username?.Trim() ?? "", req.Password ?? "", ct);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        }).AllowAnonymous().RequireRateLimiting("login");

        app.MapGet("/api/auth/me", (ClaimsPrincipal principal) =>
        {
            var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var name = principal.FindFirstValue(ClaimTypes.GivenName);
            var role = principal.FindFirstValue(ClaimTypes.Role);
            return Results.Ok(new { username = name, userId = int.Parse(id!), role });
        }).RequireAuthorization();
    }
}
