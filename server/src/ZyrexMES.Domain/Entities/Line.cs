namespace ZyrexMES.Domain.Entities;

public class Line
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    /// <summary>Origin of the row: "Manual" (default) or "Legacy" (migration import).</summary>
    public string Source { get; set; } = "Manual";
    public ICollection<Station> Stations { get; set; } = new List<Station>();
}
