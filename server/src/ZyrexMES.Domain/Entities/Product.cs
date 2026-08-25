namespace ZyrexMES.Domain.Entities;

public class Product
{
    public int Id { get; set; }
    public string Sku { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<BomItem> BomItems { get; set; } = new List<BomItem>();
    public ICollection<Routing> Routings { get; set; } = new List<Routing>();
}
