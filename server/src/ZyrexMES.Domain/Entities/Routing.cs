namespace ZyrexMES.Domain.Entities;

public class Routing
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    /// <summary>Origin of the row: "Manual" (default) or "Legacy" (migration import).</summary>
    public string Source { get; set; } = "Manual";
    public Product Product { get; set; } = null!;
    public ICollection<RoutingStep> Steps { get; set; } = new List<RoutingStep>();
}
