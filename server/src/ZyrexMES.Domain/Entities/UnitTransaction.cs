namespace ZyrexMES.Domain.Entities;

public class UnitTransaction
{
    public int Id { get; set; }
    public int UnitId { get; set; }
    public int StationId { get; set; }
    public int UserId { get; set; }
    public DateTime ScannedAtUtc { get; set; }
    public QcVerdict Result { get; set; }
    public string? Notes { get; set; }
    public Unit Unit { get; set; } = null!;
}
