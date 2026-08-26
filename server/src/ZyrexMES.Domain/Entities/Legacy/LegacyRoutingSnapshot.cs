namespace ZyrexMES.Domain.Entities;

/// <summary>Staging copy of one legacy MES routing step (flat, FK-free; RawJson keeps the original payload).</summary>
public class LegacyRoutingSnapshot
{
    public int Id { get; set; }
    public string LegacySku { get; set; } = null!;
    public int Sequence { get; set; }
    public string LegacyStationCode { get; set; } = null!;
    public bool RequireLabel { get; set; }
    public string RawJson { get; set; } = null!;
    public DateTime ImportedAtUtc { get; set; }
}
