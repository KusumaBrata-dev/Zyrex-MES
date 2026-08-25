namespace ZyrexMES.Domain.Entities;

/// <summary>Staging copy of one legacy MES line (flat, FK-free; RawJson keeps the original payload).</summary>
public class LegacyLineSnapshot
{
    public int Id { get; set; }
    public string LegacyCode { get; set; } = null!;
    public string LegacyName { get; set; } = null!;
    public string RawJson { get; set; } = null!;
    public DateTime ImportedAtUtc { get; set; }
}
