namespace ZyrexMES.PrintAgent;

/// <summary>Configuration for the station print agent (bound from the "Agent" section).</summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Base URL of the MES API, e.g. http://mes-host:5000.</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:5000";

    /// <summary>The station this agent serves (claim filters on it).</summary>
    public int StationId { get; set; }

    /// <summary>Seconds between claim polls.</summary>
    public int PollIntervalSeconds { get; set; } = 2;

    /// <summary>Label printer executable; empty means "write data file only" (stub mode).</summary>
    public string PrinterExePath { get; set; } = "";

    /// <summary>Argument template with {TemplatePath} and {DataFile} placeholders.</summary>
    public string PrinterArgsTemplate { get; set; } = "/F=\"{TemplatePath}\" /P /D=\"{DataFile}\"";

    /// <summary>Directory where label data files are written.</summary>
    public string DataDir { get; set; } = "data";

    /// <summary>Agent account username (role Agent user created by an admin at deployment).</summary>
    public string Username { get; set; } = "print-agent";

    /// <summary>Agent account password. Override via environment (Agent__Password) in production.</summary>
    public string Password { get; set; } = "";

    /// <summary>Template code -> template file path (e.g. SN_LABEL -> templates/SN_LABEL.btw).</summary>
    public Dictionary<string, string> Templates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
