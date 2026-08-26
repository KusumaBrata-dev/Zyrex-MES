namespace ZyrexMES.Domain.Entities;

/// <summary>Staging copy of one legacy MES product (flat, FK-free; RawJson keeps the original payload).</summary>
public class LegacyProductSnapshot
{
    public int Id { get; set; }
    public string LegacySku { get; set; } = null!;
    public string LegacyName { get; set; } = null!;
    public string RawJson { get; set; } = null!;
    public DateTime ImportedAtUtc { get; set; }
}
