using System.Net;
using System.Text;
using System.Text.Json;
using ZyrexMES.PrintAgent;

namespace ZyrexMES.PrintAgent.Tests;

/// <summary>Records every request so tests can assert the HTTP contract the
/// agent uses against the MES API (claim/ack must be POST — server maps MapPost).</summary>
public sealed class RecordingHandler : HttpMessageHandler
{
    public List<(HttpMethod Method, string PathAndQuery)> Calls { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        lock (Calls) Calls.Add((request.Method, request.RequestUri!.PathAndQuery));
        var path = request.RequestUri!.AbsolutePath;
        static HttpResponseMessage Json<T>(T body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        if (path == "/api/auth/login")
            return Task.FromResult(Json(new { token = "test-token", user = new { username = "u", fullName = "f", role = "Agent" } }));
        if (path == "/api/print/jobs/claim")
            return Task.FromResult(Json(Array.Empty<object>()));
        return Task.FromResult(Json(new { status = "Printed" }));
    }
}

public class ApiClientContractTests
{
    private static (ApiClient Api, RecordingHandler Handler) Create()
    {
        var handler = new RecordingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://mes.test") };
        var api = new ApiClient(http, new AgentOptions { Username = "agent1", Password = "pwd" });
        return (api, handler);
    }

    [Fact]
    public async Task Claim_Uses_Post_Verb()
    {
        var (api, handler) = Create();

        await api.ClaimJobsAsync(7);

        Assert.Equal(2, handler.Calls.Count); // login (first token fetch) + claim
        var claim = handler.Calls[^1];
        Assert.Equal(HttpMethod.Post, claim.Method);
        Assert.Equal("/api/print/jobs/claim?stationId=7", claim.PathAndQuery);
    }

    [Fact]
    public async Task Ack_Uses_Post_Verb_After_Login()
    {
        var (api, handler) = Create();

        await api.AckJobAsync(5, ok: true, error: null);

        Assert.Equal(2, handler.Calls.Count); // login + ack
        Assert.Equal((HttpMethod.Post, "/api/auth/login"), handler.Calls[0]);
        Assert.Equal((HttpMethod.Post, "/api/print/jobs/5/ack"), handler.Calls[1]);
    }
}
