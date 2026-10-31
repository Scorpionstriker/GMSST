using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Configuration;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Engine;
using Scheduler.Engine.Execution;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.IntegrationTests;

/// <summary>
/// WP-2: at-least-once delivery (NFR-01) and the lateness target. The crash after a claim and the crash during the
/// handler are covered by <see cref="EngineTests"/>; these cover the crash before the claim, the failed save after
/// the handler finished, and the start delay.
/// </summary>
public sealed class DeliveryTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [SkippableFact]
    public async Task DueTask_StartsWithinFiveSecondsOfItsDueTime()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("lateness");
        var engine = services.GetRequiredService<SchedulerEngine>();
        await engine.StartAsync();
        try
        {
            var due = DateTime.UtcNow.AddSeconds(3);
            var taskId = await services.GetRequiredService<ISchedulerClient>().EnsureTaskAsync(new TaskDefinition
            {
                Name = $"Lateness {Guid.NewGuid():N}",
                TaskType = CountingHandler.Type,
                ScheduleType = ScheduleType.Once,
                StartTimeUtc = due
            });

            var started = await WaitForStartAsync(taskId, TimeSpan.FromSeconds(20));
            Assert.True(started - due <= TimeSpan.FromSeconds(5), $"The run started {(started - due).TotalSeconds:0.0} s after its due time.");
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [SkippableFact]
    public async Task CrashBeforeTheClaim_TheWaitingEntryRunsAfterRestart()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("before-claim");
        var taskId = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        await using (var db = database.CreateContext())
        {
            // The entry was queued, then the computer stopped before any worker claimed it.
            var task = await db.Tasks.SingleAsync(t => t.Id == taskId);
            var past = DateTime.UtcNow.AddMinutes(-5);
            db.TaskQueue.Add(QueueEntry.Create(task, null, TriggerType.Manual, past, 0, past));
            await db.SaveChangesAsync();
        }

        var engine = services.GetRequiredService<SchedulerEngine>();
        await engine.StartAsync();
        try
        {
            await Wait.UntilAsync(async () => await StatusesAsync(taskId) is [ExecutionStatus.Success], Timeout, "the waiting entry ran");
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [SkippableFact]
    public async Task ResultThatCannotBeSaved_IsRetried_AndOtherwiseTheTaskRunsAgain()
    {
        RequiresDatabase.Check();
        await database.SetSettingAsync(SettingKeys.LeaseGraceSeconds, 10);
        await database.SetSettingAsync(SettingKeys.RetryBaseDelaySeconds, 1);
        var services = database.CreateServices("save-fails");
        var flaky = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type, maxRetries: 0, timeoutSeconds: 5);
        var lost = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type, maxRetries: 1, timeoutSeconds: 5);
        var failures = new Dictionary<Guid, int> { [flaky] = 2, [lost] = 4 };
        var delays = TaskExecutor.SaveRetryDelays;
        TaskExecutor.SaveRetryDelays = [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)];
        TaskExecutor.BeforeResultSaved = taskId =>
        {
            lock (failures)
            {
                if (failures.TryGetValue(taskId, out var left) && left > 0)
                {
                    failures[taskId] = left - 1;
                    throw new InvalidOperationException("Simulated database outage.");
                }
            }

            return Task.CompletedTask;
        };

        var engine = services.GetRequiredService<SchedulerEngine>();
        var signal = services.GetRequiredService<Scheduler.Application.Common.IEngineSignal>();
        await engine.StartAsync();
        try
        {
            var client = services.GetRequiredService<ISchedulerClient>();
            await client.RunNowAsync(await NameAsync(flaky));
            await client.RunNowAsync(await NameAsync(lost));

            // Two failed saves: the third attempt saves the result, the handler ran once.
            await Wait.UntilAsync(async () => await StatusesAsync(flaky) is [ExecutionStatus.Success], Timeout, "the flaky save succeeded");
            Assert.Single(CountingHandler.Runs, r => r.Value == flaky);

            // Four failed saves: the result is lost, the lease expires and recovery runs the task again (at-least-once).
            await Wait.UntilAsync(async () =>
            {
                signal.Wake();
                return await StatusesAsync(lost) is [ExecutionStatus.Retrying, ExecutionStatus.Success];
            }, Timeout, "the task whose result was lost ran again");
            Assert.Equal(2, CountingHandler.Runs.Count(r => r.Value == lost));
        }
        finally
        {
            TaskExecutor.BeforeResultSaved = null;
            TaskExecutor.SaveRetryDelays = delays;
            await engine.StopAsync();
        }
    }

    private async Task<DateTime> WaitForStartAsync(Guid taskId, TimeSpan timeout)
    {
        DateTime? started = null;
        await Wait.UntilAsync(async () =>
        {
            await using var db = database.CreateContext();
            started = await db.TaskExecutions.Where(e => e.TaskId == taskId).Select(e => (DateTime?)e.StartTimeUtc).FirstOrDefaultAsync();
            return started is not null;
        }, timeout, "the run started");
        return started!.Value;
    }

    private async Task<List<ExecutionStatus>> StatusesAsync(Guid taskId)
    {
        await using var db = database.CreateContext();
        return await db.TaskExecutions.Where(e => e.TaskId == taskId).OrderBy(e => e.StartTimeUtc).Select(e => e.Status).ToListAsync();
    }

    private async Task<string> NameAsync(Guid taskId)
    {
        await using var db = database.CreateContext();
        return await db.Tasks.Where(t => t.Id == taskId).Select(t => t.Name).SingleAsync();
    }
}
