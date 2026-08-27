using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ZyrexMES.Api.Tests;

public class MasterDataEndpointsTests(CustomWebAppFactory factory) : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _anon = Prepare(factory);

    private static HttpClient Prepare(CustomWebAppFactory factory)
    {
        // Clean up leftovers from previous runs so reruns stay idempotent.
        using var db = factory.CreateDb();
        foreach (var s in db.Stations.Where(x => x.Code == "ST-MD-QC" || x.Code == "ST-MD-QC2").ToList())
            db.Stations.Remove(s);
        foreach (var l in db.Lines.Where(x => new[] { "L-MD1", "L-MD2", "L-MD3", "L-MD4", "L-MD5" }.Contains(x.Code)).ToList())
            db.Lines.Remove(l);
        db.SaveChanges();
        return factory.CreateClient();
    }

    private async Task<(HttpClient admin, HttpClient leader, HttpClient op)> ClientsAsync()
    {
        factory.EnsureSeedUsers();
        async Task<HttpClient> As(string u, string p)
        {
            var res = await _anon.PostAsJsonAsync("/api/auth/login", new { username = u, password = p });
            var json = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
            var c = factory.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());
            return c;
        }
        return (await As("admin", "Adm1n!pwd"), await As("leader1", "Lead!pwd12"), await As("op1", "Op!pwd123"));
    }

    [Fact]
    public async Task Leader_Can_Create_Line_And_Read_Back()
    {
        var (_, leader, _) = await ClientsAsync();
        var create = await leader.PostAsJsonAsync("/api/lines", new { code = "L-MD1", name = "MD One" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var list = await leader.GetFromJsonAsync<JsonElement>("/api/lines");
        Assert.Contains(list.EnumerateArray(), e => e.GetProperty("code").GetString() == "L-MD1");
    }

    [Fact]
    public async Task Duplicate_Line_Code_Returns_409()
    {
        var (_, leader, _) = await ClientsAsync();
        await leader.PostAsJsonAsync("/api/lines", new { code = "L-MD2", name = "MD Two" });
        var dup = await leader.PostAsJsonAsync("/api/lines", new { code = "L-MD2", name = "again" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task Operator_Cannot_Create_Line_403()
    {
        var (_, _, op) = await ClientsAsync();
        var res = await op.PostAsJsonAsync("/api/lines", new { code = "L-MD3", name = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Cannot_List_Lines_401()
    {
        var res = await _anon.GetAsync("/api/lines");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Delete_Line_Deactivates_Softly()
    {
        var (admin, _, _) = await ClientsAsync();
        var create = await admin.PostAsJsonAsync("/api/lines", new { code = "L-MD4", name = "MD Four" });
        var body = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetInt32();
        var del = await admin.DeleteAsync($"/api/lines/{id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/lines?active=true");
        Assert.DoesNotContain(list.EnumerateArray(), e => e.GetProperty("code").GetString() == "L-MD4");
    }

    [Fact]
    public async Task Update_Line_Code_To_Existing_Code_Returns_409()
    {
        var (_, leader, _) = await ClientsAsync();
        await leader.PostAsJsonAsync("/api/lines", new { code = "L-MD1", name = "MD One" });
        await leader.PostAsJsonAsync("/api/lines", new { code = "L-MD2", name = "MD Two" });
        var list = await leader.GetFromJsonAsync<JsonElement>("/api/lines");
        var source = list.EnumerateArray().First(e => e.GetProperty("code").GetString() == "L-MD1");
        var res = await leader.PutAsJsonAsync($"/api/lines/{source.GetProperty("id").GetInt32()}",
            new { code = "L-MD2", name = "MD One", isActive = true });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        Assert.Equal("code already exists", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Update_Station_Code_To_Existing_Code_Returns_409()
    {
        var (_, leader, _) = await ClientsAsync();
        var lineRes = await leader.PostAsJsonAsync("/api/lines", new { code = "L-MD5", name = "MD Five" });
        var lineId = (await lineRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        await leader.PostAsJsonAsync("/api/stations", new { lineId, code = "ST-MD-QC", name = "QC MD", processType = "QC" });
        var second = await leader.PostAsJsonAsync("/api/stations",
            new { lineId, code = "ST-MD-QC2", name = "QC MD Two", processType = "QC" });
        Assert.True(second.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict); // idempotent for reruns
        var stations = await leader.GetFromJsonAsync<JsonElement>("/api/stations");
        var source = stations.EnumerateArray().First(e => e.GetProperty("code").GetString() == "ST-MD-QC2");
        var res = await leader.PutAsJsonAsync($"/api/stations/{source.GetProperty("id").GetInt32()}",
            new { code = "ST-MD-QC", name = "QC MD Two", processType = "QC", isEnabled = true });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        Assert.Equal("code already exists", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Create_Station_Under_Line_Works_And_404_For_Bad_Line()
    {
        var (_, leader, _) = await ClientsAsync();
        var lineRes = await leader.PostAsJsonAsync("/api/lines", new { code = "L-MD5", name = "MD Five" });
        var lineBody = await lineRes.Content.ReadFromJsonAsync<JsonElement>();
        var lineId = lineBody.GetProperty("id").GetInt32();
        var ok = await leader.PostAsJsonAsync("/api/stations", new { lineId, code = "ST-MD-QC", name = "QC MD", processType = "QC" });
        Assert.True(ok.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict); // idempotent for reruns
        var bad = await leader.PostAsJsonAsync("/api/stations", new { lineId = 999999, code = "ST-X", name = "X" });
        Assert.Equal(HttpStatusCode.NotFound, bad.StatusCode);
    }
}
