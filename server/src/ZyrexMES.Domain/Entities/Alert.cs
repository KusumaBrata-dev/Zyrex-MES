namespace ZyrexMES.Domain.Entities;

public class Alert
{
    public long Id { get; set; }
    public string Type { get; set; } = null!;
    public string Severity { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string? LineCode { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
}
