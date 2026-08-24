using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;

namespace ZyrexMES.Api.Tests;

public class SeedTests(CustomWebAppFactory factory) : IClassFixture<CustomWebAppFactory>
{
    [Fact]
    public void Startup_Seeds_Nine_Lines_Exactly_Once()
    {
        using var db = factory.CreateDb();
        var lines = db.Lines.ToList(); // pattern predicates below are not translatable to SQL
        Assert.True(lines.Count >= 9);
        Assert.Equal(9, lines.Count(l => l.Code.StartsWith('L') && l.Code.Length == 3 && char.IsDigit(l.Code[1]) && char.IsDigit(l.Code[2])));
    }

    [Fact]
    public async Task SignalR_Hub_Is_Reachable()
    {
        // The in-memory TestServer has no real TCP port, so the SignalR client is routed
        // through the server's handler with long polling. The hub is [Authorize], so an
        // admin token is obtained via the login API first.
        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Adm1n!pwd" });
        var json = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync());
        var token = json.RootElement.GetProperty("token").GetString()!;

        var conn = new HubConnectionBuilder()
            .WithUrl($"{factory.ClientOptions.BaseAddress}hubs/production", o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        await conn.StartAsync();
        Assert.Equal(HubConnectionState.Connected, conn.State);
        await conn.DisposeAsync();
    }
}
