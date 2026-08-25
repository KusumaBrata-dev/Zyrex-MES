namespace ZyrexMES.Domain.Entities;

public class Station
{
    public int Id { get; set; }
    public int LineId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? ProcessType { get; set; }
    public bool IsEnabled { get; set; } = true;
    /// <summary>Origin of the row: "Manual" (default) or "Legacy" (migration import).</summary>
    public string Source { get; set; } = "Manual";
    public Line Line { get; set; } = null!;
}
