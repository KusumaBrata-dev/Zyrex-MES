using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZyrexMES.Infrastructure.Legacy;

namespace ZyrexMES.Api.Tests;

public class LegacyMesClientTests
{
    private const string TokenPlaceholder = "<88-char-opaque-token>";

    // --- fake transport -----------------------------------------------------

    private sealed record CapturedRequest(Uri Uri, string? Authorization, string Body);

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var auth = request.Headers.TryGetValues("Authorization", out var values) ? string.Join(",", values) : null;
            Requests.Add(new CapturedRequest(request.RequestUri!, auth, body));
            return responder(request);
        }
    }

    private static FakeHandler JsonHandler(params string[] responsesInOrder)
    {
        var calls = 0;
        return new FakeHandler(_ =>
        {
            var json = responsesInOrder[Math.Min(calls, responsesInOrder.Length - 1)];
            calls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };
        });
    }

    private static LegacyMesClient CreateClient(FakeHandler handler, int timeoutSeconds = 20)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://legacy.test/API/TE/PostData"),
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };
        return new LegacyMesClient(
            http,
            Options.Create(new LegacyOptions { Url = "http://legacy.test/API/TE/PostData", UserId = "user1", Password = "hash32" }),
            NullLogger<LegacyMesClient>.Instance);
    }

    private static Task<string> FixtureAsync(string name) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static JsonElement BodyJson(CapturedRequest request) =>
        JsonDocument.Parse(request.Body).RootElement;

    // --- (a) GetToken -------------------------------------------------------

    [Fact]
    public async Task GetTokenAsync_WhenCodeIsSuccess_ExtractsTokenAndReturnsEnvelope()
    {
        var handler = JsonHandler(await FixtureAsync("gettoken-success.json"));
        var client = CreateClient(handler);

        var envelope = await client.GetTokenAsync();

        Assert.Equal(0, envelope.Code);
        Assert.Equal(TokenPlaceholder, envelope.Data.GetProperty("token").GetString());
        Assert.Contains("\"000000\"", envelope.Raw);

        var req = handler.Requests.Single();
        var json = BodyJson(req);
        Assert.Equal("GetToken", json.GetProperty("APIReqHeader").GetProperty("ServiceName").GetString());
        Assert.Equal("MESTools", json.GetProperty("APIReqHeader").GetProperty("ClientData").GetString());
        Assert.Equal("", json.GetProperty("APIReqHeader").GetProperty("Language").GetString());
        Assert.Matches("^[0-9a-f]{32}$", json.GetProperty("APIReqHeader").GetProperty("RequestId").GetString());
        Assert.Equal("user1", json.GetProperty("APIReqData").GetProperty("UserID").GetString());
        Assert.Equal("hash32", json.GetProperty("APIReqData").GetProperty("Password").GetString());
    }

    [Fact]
    public async Task GetTokenAsync_WhenCodeIsNotSuccess_ThrowsLegacyExceptionWithoutRetry()
    {
        var errorJson = """
            { "APIResHeader": { "RequestId": "", "ServiceName": "GetToken", "Code": "000500", "Desc": "报文APIReqData内容不能为空" }, "APIResData": null }
            """;
        var handler = JsonHandler(errorJson);
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<LegacyException>(() => client.GetTokenAsync());

        Assert.Equal(500, ex.LegacyCode);
        Assert.Contains("报文", ex.Desc);
        Assert.Single(handler.Requests); // business errors are never retried
    }

    // --- (b) CheckFlow / GetMesData ------------------------------------------

    [Fact]
    public async Task CheckFlowAsync_SendsServicePayloadAndRawAuthorizationToken()
    {
        var handler = JsonHandler(await FixtureAsync("gettoken-success.json"), await FixtureAsync("checkflow-snnotfound.json"), await FixtureAsync("gettoken-success.json"), await FixtureAsync("checkflow-snnotfound.json"));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<LegacyException>(() => client.CheckFlowAsync("SN1234", "PT"));

        Assert.Equal(500, ex.LegacyCode);
        // login → failed call → forced re-login (auth-expired recovery) → replayed call.
        Assert.Equal(4, handler.Requests.Count);

        var dataRequest = handler.Requests[1];
        Assert.Equal(TokenPlaceholder, dataRequest.Authorization); // raw token, no Bearer prefix
        var json = BodyJson(dataRequest);
        Assert.Equal("CheckFlow", json.GetProperty("APIReqHeader").GetProperty("ServiceName").GetString());
        Assert.Equal("SN1234", json.GetProperty("APIReqData").GetProperty("SN").GetString());
        Assert.Equal("SN", json.GetProperty("APIReqData").GetProperty("SNType").GetString());
        Assert.Equal("PT", json.GetProperty("APIReqData").GetProperty("Station").GetString());

        // The replayed request carries the refreshed token as well.
        Assert.Equal(TokenPlaceholder, handler.Requests[3].Authorization);
    }

    [Fact]
    public async Task Data_Call_Auth_Expired_Relogins_Once_Then_Succeeds()
    {
        var successData = """
            { "APIResHeader": { "RequestId": "<guid>", "ServiceName": "CheckFlow", "Code": "000000", "Desc": "OK" }, "APIResData": {} }
            """;
        var authExpired = """
            { "APIResHeader": { "RequestId": "", "ServiceName": "CheckFlow", "Code": "000401", "Desc": "token expired" }, "APIResData": null }
            """;
        var handler = JsonHandler(
            await FixtureAsync("gettoken-success.json"), // initial login
            authExpired,                                 // first CheckFlow: token expired
            await FixtureAsync("gettoken-success.json"), // forced re-login
            successData);                                // replay succeeds
        var client = CreateClient(handler);

        var envelope = await client.CheckFlowAsync("SN1234", "PT");

        Assert.Equal(0, envelope.Code);
        Assert.Equal(4, handler.Requests.Count); // no further retries after success
    }

    [Fact]
    public async Task Data_Call_Failing_Twice_Does_Not_Loop_Refresh()
    {
        var handler = JsonHandler(
            await FixtureAsync("gettoken-success.json"),
            await FixtureAsync("checkflow-snnotfound.json"),
            await FixtureAsync("gettoken-success.json"),
            await FixtureAsync("checkflow-snnotfound.json"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<LegacyException>(() => client.CheckFlowAsync("SN1234", "PT"));
        // Refresh/replay happens once; the second business failure propagates.
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task GetMesDataAsync_SendsCorrectServiceNameAndPayload()
    {
        var successData = """
            { "APIResHeader": { "RequestId": "<guid>", "ServiceName": "GetMesData", "Code": "000000", "Desc": "OK" }, "APIResData": {} }
            """;
        var handler = JsonHandler(await FixtureAsync("gettoken-success.json"), successData);
        var client = CreateClient(handler);

        var envelope = await client.GetMesDataAsync("SN1234", "PT");

        Assert.Equal(0, envelope.Code);
        var json = BodyJson(handler.Requests[1]);
        Assert.Equal("GetMesData", json.GetProperty("APIReqHeader").GetProperty("ServiceName").GetString());
        Assert.Equal("SN1234", json.GetProperty("APIReqData").GetProperty("SN").GetString());
        Assert.Equal("PT", json.GetProperty("APIReqData").GetProperty("Station").GetString());
    }

    // --- (c) network failure / timeout ---------------------------------------

    [Fact]
    public async Task NetworkFailure_RetriesThreeTimesThenThrowsLegacyException()
    {
        var handler = new FakeHandler(_ => throw new TaskCanceledException("simulated timeout"));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<LegacyException>(() => client.CheckFlowAsync("SN1234", "PT"));

        Assert.Equal(-1, ex.LegacyCode);
        Assert.Equal(3, handler.Requests.Count);
        Assert.IsNotType<HttpRequestException>(ex);
        Assert.IsNotType<TaskCanceledException>(ex);
    }

    // --- (d) read-only anti-regression ---------------------------------------

    [Fact]
    public void LegacyMesClient_ExposesOnlyTheThreeReadOnlyMethods()
    {
        string[] allowed = ["GetTokenAsync", "CheckFlowAsync", "GetMesDataAsync", "Dispose"];
        var declaredPublic = typeof(LegacyMesClient)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToList();

        Assert.All(declaredPublic, name => Assert.Contains(name, allowed));
        Assert.Contains("GetTokenAsync", declaredPublic);
        Assert.Contains("CheckFlowAsync", declaredPublic);
        Assert.Contains("GetMesDataAsync", declaredPublic);
    }
}
