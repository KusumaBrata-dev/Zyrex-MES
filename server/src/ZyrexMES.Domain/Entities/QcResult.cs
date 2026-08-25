namespace ZyrexMES.Domain.Entities;

public class QcResult
{
    public int Id { get; set; }
    public int UnitId { get; set; }
    public int StationId { get; set; }
    public int UserId { get; set; }
    public QcVerdict Verdict { get; set; }
    public int? NgCodeId { get; set; }
    public string? Notes { get; set; }
    public DateTime CheckedAtUtc { get; set; }
    public NgCode? NgCode { get; set; }
}
