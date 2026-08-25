namespace ZyrexMES.Domain.Entities;

/// <summary>Staging copy of one legacy MES station (flat, FK-free; RawJson keeps the original payload).</summary>
public class LegacyStationSnapshot
{
    public int Id { get; set; }
    public string LegacyLineCode { get; set; } = null!;
    public string LegacyCode { get; set; } = null!;
    public string LegacyName { get; set; } = null!;
    public string? ProcessType { get; set; }
    public string RawJson { get; set; } = null!;
    public DateTime ImportedAtUtc { get; set; }
}
