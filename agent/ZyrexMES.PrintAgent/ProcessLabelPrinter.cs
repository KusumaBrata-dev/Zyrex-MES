using System.Diagnostics;
using System.Text.Json;

namespace ZyrexMES.PrintAgent;

/// <summary>
/// Stub label printer: writes the payload to a data file and (when configured)
/// launches the label executable. Real BarTender integration lands in a later task.
/// </summary>
public sealed class ProcessLabelPrinter(ILogger<ProcessLabelPrinter> logger, AgentOptions options) : ILabelPrinter
{
    public void Print(string payloadJson, string templatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        _ = JsonDocument.Parse(payloadJson); // validate before writing anything

        var dir = Path.GetFullPath(options.DataDir);
        Directory.CreateDirectory(dir);
        var dataFile = Path.Combine(dir, $"label-{DateTime.UtcNow:yyyyMMddHHmmss-fff}.txt");
        File.WriteAllText(dataFile, payloadJson);

        var exe = options.PrinterExePath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            logger.LogWarning("PrinterExePath not configured; label data written to {DataFile} only", dataFile);
            return;
        }

        var args = options.PrinterArgsTemplate
            .Replace("{TemplatePath}", templatePath)
            .Replace("{DataFile}", dataFile);
        // ponytail: blocking WaitForExit with no timeout — a hung printer exe stalls
        // the poll loop; upgrade path is WaitForExitAsync + per-job timeout.
        using var process = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false });
        process?.WaitForExit();
    }
}
