using System.Diagnostics;

namespace ZyrexMES.PrintAgent;

/// <summary>Runs an external label-printer executable and returns its exit code.</summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="exe"/> with <paramref name="args"/>, waiting at most
    /// <paramref name="timeoutSeconds"/>. Throws <see cref="TimeoutException"/>
    /// when the process exceeds the timeout (the implementation kills it first).
    /// </summary>
    Task<int> RunAsync(string exe, string args, int timeoutSeconds, CancellationToken ct);
}

/// <summary>Real process runner: kill-on-timeout, returns the process exit code.</summary>
public sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    public async Task<int> RunAsync(string exe, string args, int timeoutSeconds, CancellationToken ct)
    {
        using var process = Process.Start(new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new PrintException($"failed to start printer executable '{exe}'");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Our timeout fired (not host shutdown): kill the whole process tree.
            logger.LogWarning("printer '{Exe}' exceeded {Timeout}s; killing process tree", exe, timeoutSeconds);
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already exited */ }
            throw new TimeoutException($"printer did not exit within {timeoutSeconds}s");
        }
    }
}
