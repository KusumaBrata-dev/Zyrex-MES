using System.Diagnostics;
using System.Text.Json;

namespace ZyrexMES.PrintAgent;

/// <summary>
/// Prints labels through the BarTender command-line interface: writes the job
/// payload to a temp data file, launches the configured BarTender executable
/// with the template/data-file arguments, and enforces a hard timeout.
/// </summary>
public sealed class BartenderCliPrinter(AgentOptions options, IProcessRunner runner) : ILabelPrinter
{
    public void Print(string payloadJson, string templateCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateCode);
        _ = JsonDocument.Parse(payloadJson); // validate before writing anything

        if (string.IsNullOrWhiteSpace(options.PrinterExePath))
            throw new PrintException("printer executable is not configured (Agent:PrinterExePath)");
        if (!options.Templates.TryGetValue(templateCode, out var templatePath))
            throw new PrintException($"unknown template '{templateCode}'");

        var dir = Path.GetFullPath(options.DataDir);
        Directory.CreateDirectory(dir);
        var dataFile = Path.Combine(dir, $"job-{Guid.NewGuid():N}.json");
        File.WriteAllText(dataFile, payloadJson);
        try
        {
            var args = options.PrinterArgsTemplate
                .Replace("{TemplatePath}", Path.GetFullPath(templatePath))
                .Replace("{DataFile}", dataFile);

            int exitCode;
            try
            {
                exitCode = runner.RunAsync(
                    options.PrinterExePath, args, options.PrintTimeoutSeconds, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch (TimeoutException tex)
            {
                throw new PrintException("bartender timeout", tex);
            }
            if (exitCode != 0)
                throw new PrintException($"bartender exit {exitCode}");
        }
        finally
        {
            TryDelete(dataFile);
        }
    }

    private void TryDelete(string dataFile)
    {
        try { File.Delete(dataFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort only; leftover files are harmless in DataDir.
        }
    }
}
