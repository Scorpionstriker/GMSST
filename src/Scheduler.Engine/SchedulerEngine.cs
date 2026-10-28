using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Scheduler.Engine;

/// <summary>
/// The scheduling loop (FR-18, FR-32). Each cycle: recover expired leases, enqueue due tasks, claim
/// waiting entries up to MaxConcurrency, hand them to <see cref="TaskExecutor"/>, then sleep until the
/// next task is due (at most MaxPollIntervalSeconds) or until <see cref="EngineSignal"/> wakes it.
/// </summary>
public sealed class SchedulerEngine(EngineSignal signal, ILogger<SchedulerEngine> logger) : BackgroundService
{
    private static readonly TimeSpan DefaultMaxPollInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Scheduler engine started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Run one scheduling cycle here once the queue and executor are implemented.
                await signal.WaitAsync(DefaultMaxPollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The loop logs its own errors and keeps running (FR-32).
                logger.LogError(ex, "Scheduler cycle failed");
            }
        }

        logger.LogInformation("Scheduler engine stopped");
    }
}
