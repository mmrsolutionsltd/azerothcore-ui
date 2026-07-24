namespace AzerothCore_UI.Api.Services;

public sealed class DatabaseBackupWorker(
    DatabaseBackupScheduler scheduler,
    ILogger<DatabaseBackupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try { await scheduler.CheckScheduleAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Database backup scheduler check failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
