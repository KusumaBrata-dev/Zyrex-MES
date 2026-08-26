namespace ZyrexMES.Domain.Entities;

/// <summary>
/// One label to print for one unit. Created automatically when a scan passes a
/// routing step flagged RequireLabel; consumed by the station print agent via
/// claim/ack endpoints.
/// </summary>
public class PrintJob
{
    public long Id { get; set; }
    public int UnitId { get; set; }
    public int StationId { get; set; }
    public string TemplateCode { get; set; } = null!;
    /// <summary>JSON payload for the template: {sn, productSku, stationCode, scannedAtUtc}.</summary>
    public string PayloadJson { get; set; } = null!;
    /// <summary>Pending | Sent | Printed | Failed.</summary>
    public string Status { get; set; } = "Pending";
    /// <summary>How many times the job was claimed by an agent.</summary>
    public int Attempts { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
