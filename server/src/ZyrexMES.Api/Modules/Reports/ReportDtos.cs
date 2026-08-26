namespace ZyrexMES.Api.Modules.Reports;

public static class ReportDtos
{
    /// <summary>Factory floor timezone: WIB = UTC+7.</summary>
    public const int WibOffsetHours = 7;

    /// <summary>
    /// UTC [start, end) window covering the given WIB calendar day.
    /// Local day D (00:00..24:00 WIB) == UTC [D-7h, D+17h).
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc) WibRange(DateOnly date)
    {
        var startUtc = DateTime.SpecifyKind(
            date.ToDateTime(TimeOnly.MinValue).AddHours(-WibOffsetHours), DateTimeKind.Utc);
        return (startUtc, startUtc.AddDays(1));
    }
}

public sealed record StationSummaryDto(int StationId, string StationCode, int Output, int Ng, double? YieldPercent);

public sealed record NgListItemDto(string Sn, string StationCode, string? NgCode, string? Notes, DateTime CheckedAtUtc);

public sealed record NgListDto(int Total, int Page, IReadOnlyList<NgListItemDto> Items);

public sealed record LineGridStationDto(
    int StationId, string StationCode, string Name,
    int OutputToday, int NgToday, DateTime? LastEventAtUtc, string Status);

public sealed record LineGridLineDto(string LineCode, IReadOnlyList<LineGridStationDto> Stations);

public sealed record LineGridDto(IReadOnlyList<LineGridLineDto> Lines);
