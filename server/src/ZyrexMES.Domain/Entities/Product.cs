namespace ZyrexMES.Domain.Entities;

public class Product
{
    public int Id { get; set; }
    public string Sku { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Origin of the row: "Manual" (default) or "Legacy" (migration import).</summary>
    public string Source { get; set; } = "Manual";
    public ICollection<BomItem> BomItems { get; set; } = new List<BomItem>();
    public ICollection<Routing> Routings { get; set; } = new List<Routing>();
}
