namespace ZyrexMES.PrintAgent;

/// <summary>
/// Polls the MES API for pending print jobs at the configured interval and
/// processes each: up to 3 print attempts, then ack(true) on success or
/// ack(false, error) after the final failed attempt. Alerts are raised by the
/// SERVER when it receives the final ack(false) — the agent never sends alerts.
/// </summary>
public sealed class PollingService(
    IAgentApiClient api,
    ILabelPrinter printer,
    AgentOptions options,
    ILogger<PollingService> logger) : BackgroundService
{
    /// <summary>Print attempts per job before acking it as failed.</summary>
    private const int MaxPrintAttempts = 3;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.PollIntervalSeconds));
        using var timer = new PeriodicTimer(interval);
        try
        {
            do
            {
                await ProcessPendingJobsAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Clean shutdown.
        }
    }

    internal async Task ProcessPendingJobsAsync(CancellationToken ct)
    {
        IReadOnlyList<PrintJobDto> jobs;
        try
        {
            jobs = await api.ClaimJobsAsync(options.StationId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "claim poll failed; will retry next tick");
            return;
        }

        foreach (var job in jobs)
        {
            Exception? lastError = null;
            for (var attempt = 1; attempt <= MaxPrintAttempts; attempt++)
            {
                try
                {
                    printer.Print(job.PayloadJson, job.TemplateCode);
                    lastError = null;
                    break;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    logger.LogWarning(ex, "print attempt {Attempt}/{MaxAttempts} failed for job {JobId}",
                        attempt, MaxPrintAttempts, job.Id);
                }
            }

            if (lastError is not null)
            {
                logger.LogError(lastError, "printing job {JobId} failed after {MaxPrintAttempts} attempts",
                    job.Id, MaxPrintAttempts);
                await AckSafeAsync(job.Id, ok: false, error: lastError.Message, ct);
                continue;
            }

            // Print succeeded: send ack(true) ONLY. If that ack itself fails we
            // must NOT fall back to ack(false) — the label is already printed,
            // and a false ack would invite re-dispatch/duplicate printing. The
            // job stays Sent on the server for manual follow-up.
            try
            {
                await api.AckJobAsync(job.Id, ok: true, error: null, ct);
                logger.LogInformation("printed job {JobId} ({Template})", job.Id, job.TemplateCode);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "ack(ok=true) for job {JobId} failed; job remains Sent on the server for manual follow-up",
                    job.Id);
            }
        }
    }

    /// <summary>Ack that swallows its own failures (except cancellation): a broken
    /// ack channel must never take down the poll loop.</summary>
    private async Task AckSafeAsync(long id, bool ok, string? error, CancellationToken ct)
    {
        try
        {
            await api.AckJobAsync(id, ok, error, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ack(ok={Ok}) for job {JobId} failed; will not retry from here", ok, id);
        }
    }
}
