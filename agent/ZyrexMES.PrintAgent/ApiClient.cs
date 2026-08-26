using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ZyrexMES.PrintAgent;

/// <summary>Typed HTTP client for the MES API; keeps the JWT in memory only.</summary>
public sealed class ApiClient(HttpClient http, AgentOptions options) : IAgentApiClient
{
    private const int MaxUnauthorizedRetries = 1;
    private string? _token;

    public async Task<IReadOnlyList<PrintJobDto>> ClaimJobsAsync(int stationId, CancellationToken ct = default)
    {
        // Server contract: POST /api/print/jobs/claim (PrintingEndpoints.MapPost).
        return await SendWithAuthAsync<List<PrintJobDto>>(
            () => new HttpRequestMessage(HttpMethod.Post, $"/api/print/jobs/claim?stationId={stationId}"),
            ct) ?? [];
    }

    public async Task AckJobAsync(long id, bool ok, string? error, CancellationToken ct = default)
    {
        await SendWithAuthAsync<EmptyResponse>(
            () => new HttpRequestMessage(HttpMethod.Post, $"/api/print/jobs/{id}/ack")
            {
                Content = JsonContent.Create(new { ok, error }),
            },
            ct);
    }

    private sealed record EmptyResponse;

    /// <summary>Sends the request with the cached token; on 401 re-logins once and retries.</summary>
    private async Task<T?> SendWithAuthAsync<T>(Func<HttpRequestMessage> newRequest, CancellationToken ct)
        where T : class
    {
        for (var attempt = 0; ; attempt++)
        {
            if (_token is null)
                await LoginAsync(ct);
            var request = newRequest();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt < MaxUnauthorizedRetries)
            {
                _token = null; // force fresh login, then retry
                continue;
            }
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        }
    }

    internal async Task LoginAsync(CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Password);
        using var response = await http.PostAsJsonAsync("/api/auth/login",
            new { username = options.Username, password = options.Password }, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        _token = body.GetProperty("token").GetString();
        if (string.IsNullOrEmpty(_token))
            throw new InvalidOperationException("login response did not contain a token");
    }
}
