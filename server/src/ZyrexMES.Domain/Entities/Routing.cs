namespace ZyrexMES.Domain.Entities;

public class Routing
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public Product Product { get; set; } = null!;
    public ICollection<RoutingStep> Steps { get; set; } = new List<RoutingStep>();
}
