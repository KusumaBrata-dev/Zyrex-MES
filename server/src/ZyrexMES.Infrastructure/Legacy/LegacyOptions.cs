namespace ZyrexMES.Infrastructure.Legacy;

public sealed class LegacyOptions
{
    public string Url { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Password { get; set; } = "";
    public string SnType { get; set; } = "SN";
    public int TimeoutSeconds { get; set; } = 20;
}
