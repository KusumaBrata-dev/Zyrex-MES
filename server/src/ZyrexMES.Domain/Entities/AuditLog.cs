namespace ZyrexMES.Domain.Entities;

public class AuditLog
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string Action { get; set; } = null!;     // HTTP verb or business operation name
    public string Method { get; set; } = null!;
    public string Path { get; set; } = null!;
    public int StatusCode { get; set; }
    public string? UserName { get; set; }
    public DateTime AtUtc { get; set; }
}
