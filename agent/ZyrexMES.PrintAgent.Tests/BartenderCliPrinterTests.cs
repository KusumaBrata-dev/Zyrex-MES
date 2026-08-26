using System.Text.Json;
using ZyrexMES.PrintAgent;

namespace ZyrexMES.PrintAgent.Tests;

/// <summary>Records runner invocations; configurable exit code / exception.</summary>
public sealed class FakeRunner : IProcessRunner
{
    public int ExitCode { get; set; }
    public Exception? Throw { get; set; }
    public List<(string Exe, string Args, int TimeoutSeconds, CancellationToken Token)> Calls { get; } = [];
    /// <summary>Invoked right before the (simulated) run — e.g. to observe the data file mid-flight.</summary>
    public Action<string>? OnRun { get; set; }

    public Task<int> RunAsync(string exe, string args, int timeoutSeconds, CancellationToken ct)
    {
        lock (Calls) Calls.Add((exe, args, timeoutSeconds, ct));
        OnRun?.Invoke(args);
        if (Throw is not null) throw Throw;
        return Task.FromResult(ExitCode);
    }
}

public class BartenderCliPrinterTests : IDisposable
{
    private static AgentOptions Options(string dataDir) => new()
    {
        PrinterExePath = "C:\\bt\\bartender.exe",
        DataDir = dataDir,
        Templates = { ["SN_LABEL"] = "templates/SN_LABEL.btw" },
    };

    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), $"bartender-tests-{Guid.NewGuid():N}");
    private readonly FakeRunner _runner = new();

    private BartenderCliPrinter CreatePrinter() => new(Options(_dataDir), _runner);

    public void Dispose()
    {
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    [Fact]
    public void Unknown_Template_Throws_PrintException_Without_Running_Process()
    {
        var printer = CreatePrinter();

        var ex = Assert.Throws<PrintException>(() => printer.Print("{}", "MYSTERY"));

        Assert.Contains("unknown template", ex.Message);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public void Missing_Printer_Exe_Throws_PrintException()
    {
        var printer = new BartenderCliPrinter(new AgentOptions { DataDir = _dataDir, Templates = { ["SN_LABEL"] = "t.btw" } }, _runner);

        var ex = Assert.Throws<PrintException>(() => printer.Print("{}", "SN_LABEL"));

        Assert.Contains("not configured", ex.Message);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public void Success_Writes_Data_File_And_Deletes_It_Afterwards()
    {
        _runner.ExitCode = 0;
        string? fileDuringRun = null;
        var existedDuringRun = false;
        string? argsSeen = null;
        _runner.OnRun = a =>
        {
            argsSeen = a;
            // Args template embeds the data file path after /D="
            const string marker = "/D=\"";
            var start = a.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            var end = a.IndexOf('"', start);
            fileDuringRun = a[start..end];
            existedDuringRun = File.Exists(fileDuringRun);
        };
        var printer = CreatePrinter();

        printer.Print("{\"sn\":\"SN-1\"}", "SN_LABEL");

        Assert.NotNull(fileDuringRun);
        Assert.True(existedDuringRun, "data file must exist while the printer runs");
        Assert.StartsWith(_dataDir, fileDuringRun);
        Assert.EndsWith(".json", fileDuringRun);
        Assert.NotNull(argsSeen);
        Assert.Contains("SN_LABEL.btw", argsSeen); // resolved to an absolute path
        Assert.DoesNotContain("{TemplatePath}", argsSeen, StringComparison.Ordinal); // placeholder replaced
        Assert.DoesNotContain("{DataFile}", argsSeen, StringComparison.Ordinal);
        Assert.False(File.Exists(fileDuringRun), "data file must be deleted afterwards");
        Assert.Empty(Directory.GetFiles(_dataDir));
    }

    [Fact]
    public void NonZero_Exit_Throws_PrintException_And_Cleans_Up_Data_File()
    {
        _runner.ExitCode = 3;
        var printer = CreatePrinter();

        var ex = Assert.Throws<PrintException>(() => printer.Print("{}", "SN_LABEL"));

        Assert.Contains("exit 3", ex.Message);
        Assert.Empty(Directory.GetFiles(_dataDir));
    }

    [Fact]
    public void Runner_Timeout_Becomes_PrintException_Timeout_With_Configured_Limit()
    {
        _runner.Throw = new TimeoutException();
        var printer = CreatePrinter();

        var ex = Assert.Throws<PrintException>(() => printer.Print("{}", "SN_LABEL"));

        Assert.Contains("timeout", ex.Message);
        Assert.IsType<TimeoutException>(ex.InnerException);
        var call = Assert.Single(_runner.Calls);
        Assert.Equal(60, call.TimeoutSeconds); // default PrintTimeoutSeconds
        Assert.Equal(CancellationToken.None, call.Token);
        Assert.Empty(Directory.GetFiles(_dataDir));
    }

    [Fact]
    public void Payload_Is_Validated_Before_Any_Process_Starts()
    {
        var printer = CreatePrinter();

        Assert.ThrowsAny<JsonException>(() => printer.Print("not-json", "SN_LABEL"));

        Assert.Empty(_runner.Calls);
        Assert.False(Directory.Exists(_dataDir) && Directory.GetFiles(_dataDir).Length > 0,
            "no data file may be written for an invalid payload");
    }
}
