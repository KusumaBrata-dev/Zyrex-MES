namespace ZyrexMES.Domain.Entities;

public class RoutingStep
{
    public int Id { get; set; }
    public int RoutingId { get; set; }
    public int Sequence { get; set; }
    public int StationId { get; set; }
    public bool RequireLabel { get; set; }
    public Routing Routing { get; set; } = null!;
    public Station Station { get; set; } = null!;
}
