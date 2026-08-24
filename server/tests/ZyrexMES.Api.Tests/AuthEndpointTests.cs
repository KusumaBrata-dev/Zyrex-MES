using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
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
