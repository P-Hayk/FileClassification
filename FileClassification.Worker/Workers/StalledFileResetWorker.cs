using FileClassification.Application.Repositories;
using FileClassification.Worker.Settings;
using Microsoft.Extensions.Options;

namespace FileClassification.Worker.Workers;

public class StalledFileResetWorker(
    ILogger<StalledFileResetWorker> logger,
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerSettings> options) : BackgroundService
{
    private readonly WorkerSettings _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_settings.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { return; }

            try
            {
                var cutoff = DateTime.UtcNow.AddSeconds(-_settings.HeartbeatTimeoutSeconds);
                await using var scope = scopeFactory.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<IFileRepository>();
                var resetCount = await repository.ResetStalledAsync(cutoff, stoppingToken);
                if (resetCount > 0)
                    logger.LogWarning("Reset {Count} stalled file(s) (no heartbeat for {Timeout}s)",
                        resetCount, _settings.HeartbeatTimeoutSeconds);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Stalled-file reset failed");
            }
        }
    }
}
