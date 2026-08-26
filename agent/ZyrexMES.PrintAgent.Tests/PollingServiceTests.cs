using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZyrexMES.PrintAgent;

namespace ZyrexMES.PrintAgent.Tests;

public sealed class FakeApi : IAgentApiClient
{
    public List<PrintJobDto> Pending { get; set; } = [];
    public Exception? ClaimError { get; set; }
    /// <summary>When set, ack(ok=true) throws (simulates a broken ack channel after printing).</summary>
    public Exception? AckTrueError { get; set; }
    public int ClaimCalls;
    public List<(long Id, bool Ok, string? Error)> Acks { get; } = [];

    public Task<IReadOnlyList<PrintJobDto>> ClaimJobsAsync(int stationId, CancellationToken ct = default)
    {
        Interlocked.Increment(ref ClaimCalls);
        if (ClaimError is not null) throw ClaimError;
        return Task.FromResult<IReadOnlyList<PrintJobDto>>(Pending);
    }

    public Task AckJobAsync(long id, bool ok, string? error, CancellationToken ct = default)
    {
        if (ok && AckTrueError is not null) throw AckTrueError;
        lock (Acks) Acks.Add((id, ok, error));
        return Task.CompletedTask;
    }
}

public sealed class FakePrinter : ILabelPrinter
{
    public Func<string, string, Exception?>? ThrowOn { get; set; }
    /// <summary>Throws a PrintException for the first N print calls (retry simulation).</summary>
    public int FailFirst { get; set; }
    public List<string> Printed { get; } = [];

    public void Print(string payloadJson, string templateCode)
    {
        Exception? failure = null;
        if (FailFirst > 0)
        {
            FailFirst--;
            failure = new PrintException("transient printer fault");
        }
        failure ??= ThrowOn?.Invoke(payloadJson, templateCode);
        if (failure is not null) throw failure;
        lock (Printed) Printed.Add(payloadJson);
    }
}

public class PollingServiceTests : IDisposable
{
    private static AgentOptions Options() => new()
    {
        StationId = 7,
        PollIntervalSeconds = 1,
        Templates = { ["SN_LABEL"] = "templates/SN_LABEL.btw" },
    };

    private readonly FakeApi _api = new();
    private readonly FakePrinter _printer = new();
    private readonly PollingService _service;

    public PollingServiceTests() => _service = new PollingService(_api, _printer, Options(), NullLogger<PollingService>.Instance);

    [Fact]
    public async Task ProcessPendingJobs_Acks_True_For_Each_Printed_Job()
    {
        _api.Pending =
        [
            new PrintJobDto(1, "SN_LABEL", "{\"sn\":\"SN-1\"}", 0),
            new PrintJobDto(2, "SN_LABEL", "{\"sn\":\"SN-2\"}", 0),
        ];

        await _service.ProcessPendingJobsAsync(CancellationToken.None);

        Assert.Equal(2, _printer.Printed.Count);
        Assert.Equal(2, _api.Acks.Count);
        Assert.All(_api.Acks, a => { Assert.True(a.Ok); Assert.Null(a.Error); });
        Assert.Equal([1L, 2L], _api.Acks.Select(a => a.Id));
    }

    [Fact]
    public async Task Printer_Failure_Acks_False_With_Message_And_Continues()
    {
        _api.Pending =
        [
            new PrintJobDto(1, "SN_LABEL", "{\"sn\":\"A\"}", 0),
            new PrintJobDto(2, "SN_LABEL", "{\"sn\":\"B\"}", 0),
        ];
        _printer.ThrowOn = (payload, _) =>
            payload.Contains('B') ? new InvalidOperationException("boom") : null;

        await _service.ProcessPendingJobsAsync(CancellationToken.None);

        var acks = _api.Acks;
        Assert.Equal(2, acks.Count);
        Assert.True(acks[0].Ok, $"acks=[{string.Join("; ", acks)}]");
        Assert.False(acks[1].Ok);
        Assert.Equal("boom", acks[1].Error);
    }

    [Fact]
    public async Task Unknown_Template_Acks_False()
    {
        // Template resolution now lives in the printer; the fake simulates its failure.
        _api.Pending = [new PrintJobDto(9, "MYSTERY", "{}", 0)];
        _printer.ThrowOn = (_, templateCode) => new PrintException($"unknown template '{templateCode}'");

        await _service.ProcessPendingJobsAsync(CancellationToken.None);

        var ack = Assert.Single(_api.Acks);
        Assert.False(ack.Ok);
        Assert.Contains("template", ack.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_printer.Printed);
    }

    [Fact]
    public async Task Claim_Failure_Is_Survived_On_Next_Ticks()
    {
        _api.ClaimError = new HttpRequestException("api down");

        await _service.ProcessPendingJobsAsync(CancellationToken.None); // must not throw
        Assert.Empty(_api.Acks);

        _api.ClaimError = null;
        _api.Pending = [new PrintJobDto(5, "SN_LABEL", "{}", 0)];
        await _service.ProcessPendingJobsAsync(CancellationToken.None);
        Assert.Single(_api.Acks, a => a.Id == 5 && a.Ok);
    }

    [Fact]
    public async Task Two_Print_Failures_Then_Success_Acks_True_Once()
    {
        _api.Pending = [new PrintJobDto(6, "SN_LABEL", "{}", 0)];
        _printer.FailFirst = 2;

        await _service.ProcessPendingJobsAsync(CancellationToken.None);

        var ack = Assert.Single(_api.Acks);
        Assert.Equal(6, ack.Id);
        Assert.True(ack.Ok);
        Assert.Null(ack.Error);
        Assert.Single(_printer.Printed); // third attempt succeeded
    }

    [Fact]
    public async Task Three_Print_Failures_Ack_False_Once_Without_Agent_Alert()
    {
        _api.Pending = [new PrintJobDto(7, "SN_LABEL", "{}", 0)];
        _printer.FailFirst = 99; // never succeeds

        await _service.ProcessPendingJobsAsync(CancellationToken.None);

        var ack = Assert.Single(_api.Acks);
        Assert.False(ack.Ok);
        Assert.Contains("transient printer fault", ack.Error);
        // The agent has no alert channel at all: alerts are raised by the server
        // on the final ack(false). Nothing beyond this single ack is emitted.
        Assert.Empty(_printer.Printed);
    }

    [Fact]
    public async Task Ack_True_Failure_Sends_No_Ack_False_And_Loop_Survives()
    {
        _api.Pending = [new PrintJobDto(3, "SN_LABEL", "{}", 0)];
        _api.AckTrueError = new HttpRequestException("ack channel down");

        await _service.ProcessPendingJobsAsync(CancellationToken.None);

        // Label printed, but ack(true) failed: no ack(false) may be sent.
        Assert.Single(_printer.Printed);
        Assert.Empty(_api.Acks);

        // Loop stays alive: next tick processes normally.
        _api.AckTrueError = null;
        _api.Pending = [new PrintJobDto(4, "SN_LABEL", "{}", 0)];
        await _service.ProcessPendingJobsAsync(CancellationToken.None);
        var ack = Assert.Single(_api.Acks);
        Assert.Equal(4, ack.Id);
        Assert.True(ack.Ok);
    }

    [Fact]
    public async Task Cancellation_Stops_Loop_Cleanly()
    {
        using var cts = new CancellationTokenSource();
        var runTask = _service.StartAsync(cts.Token);

        // Wait until at least one claim happened, then stop.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_api.ClaimCalls == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.True(_api.ClaimCalls > 0, "poll loop never claimed");

        await _service.StopAsync(CancellationToken.None);
        var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(runTask, completed); // ExecuteTask finished within timeout
        Assert.False(runTask.IsFaulted, $"loop faulted: {runTask.Exception}");
    }

    public void Dispose() => _service.Dispose();
}
