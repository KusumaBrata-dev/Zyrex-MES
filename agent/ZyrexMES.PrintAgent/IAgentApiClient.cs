namespace ZyrexMES.PrintAgent;

/// <summary>A print job as returned by the MES claim endpoint.</summary>
public sealed record PrintJobDto(long Id, string TemplateCode, string PayloadJson, int Attempts);

/// <summary>
/// MES API access for the agent. Implementations own the JWT lifecycle
/// (login on demand, single re-login retry on 401).
/// </summary>
public interface IAgentApiClient
{
    Task<IReadOnlyList<PrintJobDto>> ClaimJobsAsync(int stationId, CancellationToken ct = default);

    Task AckJobAsync(long id, bool ok, string? error, CancellationToken ct = default);
}
