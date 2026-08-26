namespace ZyrexMES.PrintAgent;

/// <summary>
/// Polls the MES API for pending print jobs at the configured interval and
/// processes each: print -> ack(true), or ack(false, error) on any failure.
/// </summary>
public sealed class PollingService(
    IAgentApiClient api,
    ILabelPrinter printer,
    AgentOptions options,
    ILogger<PollingService> logger) : BackgroundService
{
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
            try
            {
                var templatePath = ResolveTemplatePath(job.TemplateCode);
                printer.Print(job.PayloadJson, templatePath);
                await api.AckJobAsync(job.Id, ok: true, error: null, ct);
                logger.LogInformation("printed job {JobId} ({Template})", job.Id, job.TemplateCode);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "printing job {JobId} failed", job.Id);
                await api.AckJobAsync(job.Id, ok: false, error: ex.Message, ct);
            }
        }
    }

    /// <exception cref="InvalidOperationException">Template not configured for this agent.</exception>
    private string ResolveTemplatePath(string templateCode)
    {
        if (!options.Templates.TryGetValue(templateCode, out var path))
            throw new InvalidOperationException($"template '{templateCode}' is not configured on this agent");
        return path;
    }
}
