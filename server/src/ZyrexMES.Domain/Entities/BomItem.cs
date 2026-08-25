namespace ZyrexMES.Domain.Entities;

public class BomItem
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ComponentPart { get; set; } = null!;
    public decimal QtyPerUnit { get; set; }
    public Product Product { get; set; } = null!;
}
