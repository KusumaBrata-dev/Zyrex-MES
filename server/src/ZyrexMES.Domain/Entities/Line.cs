namespace ZyrexMES.Domain.Entities;

public class Line
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public ICollection<Station> Stations { get; set; } = new List<Station>();
}
