namespace AiDetection.Web;

public sealed class RetentionWorker(AppSettings settings, IResultStore store, ILogger<RetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (settings.ResultsRetentionDays <= 0) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cutoff = DateTimeOffset.UtcNow.AddDays(-settings.ResultsRetentionDays);
                var purged = await store.PurgeOlderThanAsync(cutoff, stoppingToken);
                if (purged > 0) logger.LogInformation("Purged {Count} result records older than {Cutoff}", purged, cutoff);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogWarning(exception, "Result-retention purge failed"); }
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
