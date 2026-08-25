using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ZyrexMES.Infrastructure.Legacy;

/// <summary>
/// Parsed legacy MES response. <see cref="Code"/> is the numeric value of the
/// 6-digit wire code ("000000" → 0); success/failure decisions are made on the
/// raw string inside the client, never on this int.
/// </summary>
public sealed record LegacyEnvelope(int Code, JsonElement Data, string Raw);

/// <summary>
/// Legacy MES failure: business error (Code != "000000") or exhausted network
/// retries (<see cref="NetworkFailureCode"/>). Never lets a raw transport
/// exception escape to callers.
/// </summary>
public sealed class LegacyException : Exception
{
    public const int NetworkFailureCode = -1;

    public int LegacyCode { get; }
    public string Desc { get; }

    public LegacyException(int legacyCode, string desc, Exception? innerException = null)
        : base($"Legacy MES call failed: Code={legacyCode} Desc={desc}", innerException)
    {
        LegacyCode = legacyCode;
        Desc = desc;
    }
}

/// <summary>
/// READ-ONLY client for the legacy vendor MES (GetToken / CheckFlow /
/// GetMesData only). UpdateInfo and any other write service are intentionally
/// unreachable from this class — enforced by test.
/// </summary>
public sealed class LegacyMesClient(HttpClient http, IOptions<LegacyOptions> options, ILogger<LegacyMesClient>? logger = null)
{
    private const string SuccessCode = "000000";
    private const string ClientData = "MESTools";
    private const string ServiceGetToken = "GetToken";
    private const string ServiceCheckFlow = "CheckFlow";
    private const string ServiceGetMesData = "GetMesData";
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    private static readonly JsonElement NullDataElement = JsonDocument.Parse("null").RootElement;

    private readonly HttpClient _http = http;
    private readonly LegacyOptions _options = options.Value;
    private readonly ILogger<LegacyMesClient>? _logger = logger;
    private string? _token;

    public Task<LegacyEnvelope> GetTokenAsync(CancellationToken ct = default) => FetchTokenEnvelopeAsync(ct);

    public async Task<LegacyEnvelope> CheckFlowAsync(string sn, string station, CancellationToken ct = default)
    {
        ValidateInput(sn, station);
        _token ??= await FetchTokenAsync(ct);
        return await SendAsync(ServiceCheckFlow, new { SN = sn, SNType = _options.SnType, Station = station }, ct);
    }

    public async Task<LegacyEnvelope> GetMesDataAsync(string sn, string station, CancellationToken ct = default)
    {
        ValidateInput(sn, station);
        _token ??= await FetchTokenAsync(ct);
        return await SendAsync(ServiceGetMesData, new { SN = sn, SNType = _options.SnType, Station = station }, ct);
    }

    // --- internals -----------------------------------------------------------

    private async Task<string> FetchTokenAsync(CancellationToken ct)
    {
        await FetchTokenEnvelopeAsync(ct);
        return _token!; // guaranteed non-null by FetchTokenEnvelopeAsync (throws otherwise)
    }

    private async Task<LegacyEnvelope> FetchTokenEnvelopeAsync(CancellationToken ct)
    {
        var envelope = await SendAsync(
            ServiceGetToken,
            new { UserID = _options.UserId, Password = _options.Password },
            ct);

        var token = envelope.Data.ValueKind == JsonValueKind.Object
            && envelope.Data.TryGetProperty("token", out var t)
                ? t.GetString()
                : null;
        if (string.IsNullOrEmpty(token))
        {
            throw new LegacyException(LegacyException.NetworkFailureCode, "GetToken succeeded but APIResData.token is missing");
        }

        _token = token;
        return envelope;
    }

    /// <summary>Sends one request with manual retry (3x, 500ms backoff) for
    /// network failures/timeouts only. Business errors (Code != "000000") are
    /// never retried.</summary>
    private async Task<LegacyEnvelope> SendAsync(string serviceName, object reqData, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(BuildEnvelope(serviceName, reqData));
        Exception? lastError = null;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "");
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                if (_token is not null)
                {
                    request.Headers.TryAddWithoutValidation("Authorization", _token); // raw token, no Bearer
                }

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                var raw = await response.Content.ReadAsStringAsync(ct);
                return ParseAndValidate(raw);
            }
            catch (LegacyException)
            {
                throw; // business/protocol error: never retried
            }
            catch (HttpRequestException ex)
            {
                lastError = ex;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                lastError = new TimeoutException("Legacy MES request timed out");
            }

            if (attempt >= MaxAttempts)
            {
                break;
            }
            _logger?.LogWarning("Legacy {Service} attempt {Attempt}/{MaxAttempts} failed on network, retrying in {DelayMs}ms",
                serviceName, attempt, MaxAttempts, RetryDelay.TotalMilliseconds);
            await Task.Delay(RetryDelay, ct);
        }

        throw new LegacyException(
            LegacyException.NetworkFailureCode,
            $"Network failure after {MaxAttempts} attempts calling {serviceName}: {lastError?.Message}",
            lastError);
    }

    private static object BuildEnvelope(string serviceName, object reqData) => new
    {
        APIReqHeader = new
        {
            RequestId = Guid.NewGuid().ToString("N"),
            ServiceName = serviceName,
            Language = "",
            ClientData,
        },
        APIReqData = reqData,
    };

    private static LegacyEnvelope ParseAndValidate(string raw)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(raw);
        }
        catch (JsonException ex)
        {
            throw new LegacyException(LegacyException.NetworkFailureCode, "Malformed JSON from legacy MES", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("APIResHeader", out var header)
                || !header.TryGetProperty("Code", out var codeEl))
            {
                throw new LegacyException(LegacyException.NetworkFailureCode, "Legacy MES response has no APIResHeader.Code");
            }

            var code = codeEl.GetString() ?? "";
            var desc = header.TryGetProperty("Desc", out var descEl) ? descEl.GetString() ?? "" : "";
            if (!string.Equals(code, SuccessCode, StringComparison.Ordinal))
            {
                throw new LegacyException(ToLegacyCode(code), desc);
            }

            var data = root.TryGetProperty("APIResData", out var dataEl) ? dataEl.Clone() : NullDataElement;
            return new LegacyEnvelope(ToLegacyCode(code), data, raw);
        }
    }

    /// <summary>Numeric view of the wire code for reporting only ("000500" → 500).</summary>
    private static int ToLegacyCode(string code) =>
        int.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : LegacyException.NetworkFailureCode;

    private static void ValidateInput(string sn, string station)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sn);
        ArgumentException.ThrowIfNullOrWhiteSpace(station);
    }
}
