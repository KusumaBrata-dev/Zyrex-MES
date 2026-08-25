namespace ZyrexMES.Domain.Entities;

/// <summary>Staging copy of one legacy MES scan transaction (flat, FK-free; RawJson keeps the original payload).</summary>
public class LegacyTransactionSnapshot
{
    public int Id { get; set; }
    public string SN { get; set; } = null!;
    public string StationCode { get; set; } = null!;
    /// <summary>Legacy pass/fail flag: "P" or "F".</summary>
    public string ResultChar { get; set; } = null!;
    public DateTime ScannedAtUtc { get; set; }
    public string? OperatorCode { get; set; }
    public string RawJson { get; set; } = null!;
    public DateTime ImportedAtUtc { get; set; }
}
