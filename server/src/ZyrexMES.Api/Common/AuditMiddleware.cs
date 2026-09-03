using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Common;

/// <summary>Records every non-GET request as an append-only audit trail entry.</summary>
public class AuditMiddleware(RequestDelegate next, ILogger<AuditMiddleware> log)
{
    private readonly ILogger<AuditMiddleware> _log = log;

    public async Task InvokeAsync(HttpContext ctx, AppDbContext db)
    {
        if (HttpMethods.IsGet(ctx.Request.Method))
        {
            await next(ctx);
            return;
        }

        var sw = Stopwatch.StartNew();
        Exception? failure = null;
        try { await next(ctx); }
        catch (Exception ex) { failure = ex; throw; }
        finally
        {
            sw.Stop();
            try
            {
                db.AuditLogs.Add(new Domain.Entities.AuditLog
                {
                    UserId = TryUserId(ctx),
                    Action = ctx.Request.Method,
                    Method = ctx.Request.Method,
                    Path = ctx.Request.Path.Value ?? "",
                    StatusCode = failure is null ? ctx.Response.StatusCode : 500,
                    UserName = ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value,
                    AtUtc = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex) { _log.LogWarning(ex, "Audit log write failed"); }
        }
    }

    private static int? TryUserId(HttpContext ctx)
    {
        var v = ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(v, out var id) ? id : null;
    }
}
