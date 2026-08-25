namespace ZyrexMES.Domain.Entities;

public class NgCode
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string Description { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}
