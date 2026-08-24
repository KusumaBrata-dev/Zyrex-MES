using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Tests;

public class AuditLogTests(CustomWebAppFactory factory) : IClassFixture<CustomWebAppFactory>
{
    [Fact]
    public async Task Non_Get_Request_Writes_Audit_Row()
    {
        var before = CountLogs(factory);
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Adm1n!pwd" });
        Assert.True(CountLogs(factory) > before);
    }

    [Fact]
    public async Task Update_On_Audit_Log_Is_Rejected_By_Database()
    {
        using var db = factory.CreateDb();
        var row = await db.AuditLogs.OrderBy(a => a.Id).FirstAsync();
        var ex = Assert.Throws<PostgresException>(() =>
        {
            db.Database.ExecuteSql($"UPDATE audit_logs SET \"Action\"='HACKED' WHERE \"Id\"={row.Id}");
        });
        Assert.Contains("append-only", ex.MessageText);
    }

    private static int CountLogs(CustomWebAppFactory f)
    {
        using var db = f.CreateDb();
        return db.AuditLogs.Count();
    }
}
