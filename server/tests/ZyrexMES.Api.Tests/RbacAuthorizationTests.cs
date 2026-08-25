using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ZyrexMES.Api.Common;

namespace ZyrexMES.Api.Tests;

public class RbacAuthorizationTests(CustomWebAppFactory factory) : IClassFixture<CustomWebAppFactory>
{
    private async Task<string> TokenAsync(string u, string p)
    {
        factory.EnsureSeedUsers();
        var c = factory.CreateClient();
        var res = await c.PostAsJsonAsync("/api/auth/login", new { username = u, password = p });
        var json = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private HttpClient Client(string token)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task Operator_Calling_AdminOnly_Endpoint_Gets_403()
    {
        var token = await TokenAsync("op1", "Op!pwd123"); // seeded via CustomWebAppFactory.EnsureSeedUsers
        var res = await Client(token).PostAsJsonAsync("/api/admin/ping", new { });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Admin_Calling_AdminOnly_Endpoint_Gets_200()
    {
        var token = await TokenAsync("admin", "Adm1n!pwd");
        var res = await Client(token).PostAsJsonAsync("/api/admin/ping", new { });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public void Role_Names_Match_Spec_Vocabulary()
    {
        Assert.Equal(["Operator", "Leader", "Qa", "Supervisor", "Admin"], Roles.All);
    }
}
