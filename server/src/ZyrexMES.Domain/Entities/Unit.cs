namespace ZyrexMES.Domain.Entities;

public class Unit
{
    public int Id { get; set; }
    public string SerialNumber { get; set; } = null!;
    public int ProductId { get; set; }
    public UnitStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Product Product { get; set; } = null!;
}
