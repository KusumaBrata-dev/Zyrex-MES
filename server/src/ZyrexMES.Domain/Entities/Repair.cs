namespace ZyrexMES.Domain.Entities;

public class Repair
{
    public int Id { get; set; }
    public int UnitId { get; set; }
    public string ProblemDescription { get; set; } = null!;
    public string? RootCause { get; set; }
    public RepairStatus Status { get; set; }
    public int ReportedByUserId { get; set; }
    public DateTime ReportedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}
