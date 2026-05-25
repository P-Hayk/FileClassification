using FileClassification.Application.DTOs;
using FileClassification.Application.Enums;
using FileClassification.Application.Interfaces;
using FileClassification.Application.Repositories;
using FileClassification.Entities;
using FileClassification.Worker.Settings;
using Microsoft.Extensions.Options;

namespace FileClassification.Worker.Workers;

public class FileProcessingWorker(
    ILogger<FileProcessingWorker> logger,
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerSettings> options) : BackgroundService
{
    private readonly WorkerSettings _settings = options.Value;
    private readonly string _workerId = Guid.NewGuid().ToString("N");
    private readonly SemaphoreSlim _slots = new(options.Value.ConcurrencyLimit, options.Value.ConcurrencyLimit);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker {WorkerId} up (concurrency {Limit}, poll {Poll}s)",
            _workerId, _settings.ConcurrencyLimit, _settings.PollIntervalSeconds);

        var pollInterval = TimeSpan.FromSeconds(_settings.PollIntervalSeconds);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await PollAsync(stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Poll cycle failed");
                }

                await Task.Delay(pollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var availableSlots = _slots.CurrentCount;
        if (availableSlots == 0) return;

        IReadOnlyList<FileRecord> claimed;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IFileRepository>();
            claimed = await repository.ClaimPendingAsync(_workerId, availableSlots, ct);
        }

        foreach (var file in claimed)
        {
            await _slots.WaitAsync(ct);
            _ = Task.Run(() => RunOne(file, ct), ct);
        }
    }

    private async Task RunOne(FileRecord file, CancellationToken hostToken)
    {
        try
        {
            var result = await ClassifyAsync(file, hostToken);
            if (result is not null)
                await FinalizeAsync(file.Id, result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unhandled error for file {FileId}", file.Id);
        }
        finally
        {
            _slots.Release();
        }
    }

    private async Task<ProcessingResult?> ClassifyAsync(FileRecord file, CancellationToken hostToken)
    {
        logger.LogInformation("Processing {FileId} ({FileName})", file.Id, file.FileName);

        double latestProgress = 0;
        var progress = new Progress<double>(value => latestProgress = value);

        using var processingCts = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
        var heartbeat = RunHeartbeatAsync(file.Id, () => latestProgress, processingCts, heartbeatCts.Token);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var classifier = scope.ServiceProvider.GetRequiredService<IFileClassifier>();
            var repository = scope.ServiceProvider.GetRequiredService<IFileRepository>();

            await using var dataStream = await repository.OpenReadStreamAsync(file.DataOid, processingCts.Token);
            var classification = await classifier.ClassifyAsync(dataStream, file.SizeBytes, progress, processingCts.Token);
            return new ProcessingResult(FileState.Completed, classification.Language, classification.Score);
        }
        catch (OperationCanceledException) when (!hostToken.IsCancellationRequested)
        {
            logger.LogInformation("File {FileId} cancelled mid-processing", file.Id);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Classification failed for {FileId} ({FileName})", file.Id, file.FileName);
            return new ProcessingResult(FileState.Failed, Language.Unknown, null);
        }
        finally
        {
            await heartbeatCts.CancelAsync();
            try { await heartbeat; } catch (OperationCanceledException) { }
        }
    }

    private async Task FinalizeAsync(int fileId, ProcessingResult result)
    {
        // host token may be cancelled on shutdown; finalize on a fresh, time-boxed one
        using var finalizeCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IFileRepository>();
        await repository.FinalizeAsync(fileId, _workerId, result.FinalState, result.Language, result.Score, finalizeCts.Token);

        logger.LogInformation("File {FileId} → {State} ({Language} {Score:F1}%)",
            fileId, result.FinalState, result.Language, result.Score ?? 0);
    }

    private async Task RunHeartbeatAsync(int fileId, Func<double> getProgress, CancellationTokenSource processingCts, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(_settings.ProgressUpdateIntervalSeconds);
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(interval, ct); }
            catch (OperationCanceledException) { return; }

            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IFileRepository>();
            var stillActive = await repository.UpdateProgressAsync(fileId, _workerId, getProgress(), ct);

            if (!stillActive)
            {
                logger.LogInformation("File {FileId} no longer active, cancelling", fileId);
                await processingCts.CancelAsync();
                return;
            }
        }
    }

    public override void Dispose()
    {
        _slots.Dispose();
        base.Dispose();
    }
}
