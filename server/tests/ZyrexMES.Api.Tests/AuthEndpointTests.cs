using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZyrexMES.Domain.Entities;

namespace ZyrexMES.Api.Tests;

public class AuthEndpointTests : IClassFixture<CustomWebAppFactory>, IDisposable
{
    private readonly CustomWebAppFactory _factory;
    private readonly HttpClient _client;

    public AuthEndpointTests(CustomWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        factory.EnsureSeedUsers();
    }

    [Fact]
    public async Task Login_Valid_Credentials_Returns_Token_And_Role()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Adm1n!pwd" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("token").GetString()));
        Assert.Equal("Admin", json.RootElement.GetProperty("user").GetProperty("role").GetString());
    }

    [Fact]
    public async Task Login_Wrong_Password_Returns_401()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "salah" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Login_Unknown_User_Returns_401()
    {
        // Regression guard for the timing-uniformity change: unknown users must still get 401.
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { username = "no-such-user", password = "whatever" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public void Jwt_Options_Explicitly_Validate_Lifetime()
    {
        var opts = _factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.True(opts.TokenValidationParameters.ValidateLifetime);
    }

    [Fact]
    public void Verify_Malformed_Hash_Returns_False_Without_Throwing()
    {
        Assert.False(Infrastructure.Security.PasswordHasher.Verify("x", "argon2id$$$"));
        // Invalid base64 payload: must be caught, not thrown out of Verify.
        Assert.False(Infrastructure.Security.PasswordHasher.Verify("x", "argon2id$a!b$c!d"));
    }

    [Fact]
    public async Task Me_Without_Token_Returns_401()
    {
        var res = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Me_With_Token_Returns_Profile()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { username = "op1", password = "Op!pwd123" });
        var json = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync());
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());
        var me = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = await JsonDocument.ParseAsync(await me.Content.ReadAsStreamAsync());
        Assert.Equal("Operator", body.RootElement.GetProperty("role").GetString());
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>
/// Isolated host with a strict login rate limit (2 requests / 5 s) so the throttle test
/// never interferes with other test classes sharing the default factory.
/// </summary>
public class LoginRateLimitFactory : CustomWebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:LoginRateLimit:PermitLimit"] = "2",
                ["Auth:LoginRateLimit:WindowSeconds"] = "5",
            }));
    }
}

public class LoginRateLimitTests(LoginRateLimitFactory factory) : IClassFixture<LoginRateLimitFactory>
{
    [Fact]
    public async Task Login_Exceeding_Permit_Limit_Returns_429()
    {
        factory.EnsureSeedUsers();
        using var client = factory.CreateClient();
        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 3; i++) // permit limit 2 within a 5 s window → 3rd call throttled
        {
            last = (await client.PostAsJsonAsync("/api/auth/login",
                new { username = "admin", password = "Adm1n!pwd" })).StatusCode;
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }
}
