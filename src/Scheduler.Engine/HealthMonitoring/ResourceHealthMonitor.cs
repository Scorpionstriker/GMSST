using System.Diagnostics;
using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Configuration;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Engine.HealthMonitoring;

internal sealed class ResourceHealthMonitor(
    IResourceHealthReader reader,
    ISettingsProvider settings,
    IUserAccountStore users,
    ISchedulerNotifications notifications,
    ISchedulerLogger log,
    TimeProvider clock)
{
    private static readonly TimeSpan AlertCooldown = TimeSpan.FromMinutes(10);
    private readonly Dictionary<string, DateTime> _lastAlertUtc = new(StringComparer.Ordinal);
    private DateTime _nextCheckUtc;
    private TimeSpan? _lastProcessCpu;
    private DateTime? _lastProcessSampleUtc;

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        try
        {
            var intervalSeconds = await settings.GetIntAsync(SettingKeys.HealthCheckIntervalSeconds, 60, cancellationToken);
            if (now < _nextCheckUtc)
            {
                return;
            }

            _nextCheckUtc = now.AddSeconds(intervalSeconds);
            var staleSeconds = await settings.GetIntAsync(SettingKeys.WorkerStaleSeconds, 120, cancellationToken);
            var snapshot = await reader.GetSnapshotAsync(now.AddMinutes(-10), now.AddSeconds(-staleSeconds), cancellationToken);
            var (cpuPercent, workingSetBytes) = SampleProcess(now);
            var alerts = new List<(string Key, string Message)>();

            var failureThreshold = await settings.GetIntAsync(SettingKeys.HealthFailureThreshold, 3, cancellationToken);
            var timeoutThreshold = await settings.GetIntAsync(SettingKeys.HealthTimeoutThreshold, 2, cancellationToken);
            foreach (var task in snapshot.RecentTaskExecutions.Where(item => item.FailureCount >= failureThreshold))
            {
                alerts.Add(($"failures:{task.TaskId}", $"Task '{task.TaskName}' has {task.FailureCount} failures in the last 10 minutes."));
            }

            foreach (var task in snapshot.RecentTaskExecutions.Where(item => item.TimeoutCount >= timeoutThreshold))
            {
                alerts.Add(($"timeouts:{task.TaskId}", $"Task '{task.TaskName}' has timed out {task.TimeoutCount} times in the last 10 minutes."));
            }

            if (snapshot.StaleWorkers > 0)
            {
                alerts.Add(("workers:stale", $"{snapshot.StaleWorkers} scheduler worker(s) have stale heartbeats."));
            }

            var queueThreshold = await settings.GetIntAsync(SettingKeys.HealthQueueBacklogThreshold, 100, cancellationToken);
            if (snapshot.WaitingQueueEntries >= queueThreshold)
            {
                alerts.Add(("queue:backlog", $"The scheduler queue has {snapshot.WaitingQueueEntries} waiting entries."));
            }

            if (snapshot.TotalWorkers > 0 && snapshot.BusyWorkers >= snapshot.TotalWorkers && snapshot.WaitingQueueEntries > 0)
            {
                alerts.Add(("workers:saturated",
                    $"All {snapshot.TotalWorkers} scheduler workers are busy with {snapshot.WaitingQueueEntries} entries waiting."));
            }

            var cpuThreshold = await settings.GetIntAsync(SettingKeys.HealthCpuPercentThreshold, 80, cancellationToken);
            if (cpuPercent is { } cpu && cpu >= cpuThreshold)
            {
                alerts.Add(("process:cpu", $"Scheduler process CPU use is {cpu:F0}% (warning threshold {cpuThreshold}%)."));
            }

            var memoryThreshold = await settings.GetIntAsync(SettingKeys.HealthMemoryMegabytesThreshold, 1024, cancellationToken);
            var memoryMb = workingSetBytes / (1024d * 1024d);
            if (memoryMb >= memoryThreshold)
            {
                alerts.Add(("process:memory", $"Scheduler process memory use is {memoryMb:F0} MB (warning threshold {memoryThreshold} MB)."));
            }

            var activeKeys = alerts.Select(alert => alert.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var resolved in _lastAlertUtc.Keys.Except(activeKeys, StringComparer.Ordinal).ToList())
            {
                _lastAlertUtc.Remove(resolved);
            }

            foreach (var (key, message) in alerts)
            {
                await AlertAsync(key, message, now, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await log.LogAsync(SchedulerLogLevel.Error, LogSources.Engine,
                $"Scheduler health monitoring failed; scheduling will continue: {ex.Message}", cancellationToken: CancellationToken.None);
        }
    }

    public async Task ReportStaleWorkersAsync(int count, CancellationToken cancellationToken)
    {
        try
        {
            await AlertAsync("workers:stale", $"{count} scheduler worker(s) were stopped because their heartbeats were stale.",
                clock.GetUtcNow().UtcDateTime, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await log.LogAsync(SchedulerLogLevel.Error, LogSources.Engine,
                $"Stale worker alerting failed; scheduling will continue: {ex.Message}", cancellationToken: CancellationToken.None);
        }
    }

    private async Task AlertAsync(string key, string message, DateTime now, CancellationToken cancellationToken)
    {
        if (_lastAlertUtc.TryGetValue(key, out var last) && now - last < AlertCooldown)
        {
            return;
        }

        _lastAlertUtc[key] = now;
        await log.LogAsync(SchedulerLogLevel.Warning, LogSources.Engine, message, cancellationToken: cancellationToken);
        var recipients = (await users.ListAsync(cancellationToken))
            .Where(user => user.IsActive && user.Role is UserRole.Administrator or UserRole.Developer)
            .Select(user => user.Id)
            .Distinct()
            .ToList();
        foreach (var recipient in recipients)
        {
            try
            {
                await notifications.ScheduleAsync("Scheduler Health", recipient, "Scheduler health alert", message,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await log.LogAsync(SchedulerLogLevel.Error, LogSources.Engine,
                    $"Could not notify user {recipient} about a scheduler health alert: {ex.Message}",
                    cancellationToken: CancellationToken.None);
            }
        }
    }

    private (double? CpuPercent, long WorkingSetBytes) SampleProcess(DateTime now)
    {
        using var process = Process.GetCurrentProcess();
        var cpuTime = process.TotalProcessorTime;
        var workingSet = process.WorkingSet64;
        double? cpuPercent = null;
        if (_lastProcessCpu is { } previousCpu && _lastProcessSampleUtc is { } previousAt && now > previousAt)
        {
            var elapsed = (now - previousAt).TotalMilliseconds * Environment.ProcessorCount;
            cpuPercent = Math.Clamp((cpuTime - previousCpu).TotalMilliseconds / elapsed * 100, 0, 100);
        }

        _lastProcessCpu = cpuTime;
        _lastProcessSampleUtc = now;
        return (cpuPercent, workingSet);
    }
}
