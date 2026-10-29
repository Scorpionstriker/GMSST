using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Configuration;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Engine;
using Scheduler.Engine.Execution;
using Scheduler.Extensibility.Contracts;
using Scheduler.Extensibility.Events;

namespace Scheduler.IntegrationTests;

public sealed class EngineTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [SkippableFact]
    public async Task RunNow_ExecutesTheTaskOnceAndRecordsSuccess()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("e2e");
        var taskId = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        var events = new List<string>();
        services.GetRequiredService<ISchedulerEvents>().TaskSucceeded += (_, e) => { lock (events) events.Add(e.TaskName); };

        await RunEngineAsync(services, async () =>
        {
            await RunNowAsync(services, taskId);
            await Wait.UntilAsync(async () => await StatusesAsync(taskId) is [ExecutionStatus.Success], Timeout, "the run succeeded");
        });

        await using var db = database.CreateContext();
        var execution = await db.TaskExecutions.SingleAsync(e => e.TaskId == taskId);
        Assert.Equal(TriggerType.Manual, execution.TriggerType);
        Assert.NotNull(execution.DurationMs);
        Assert.Contains("RetryNumber", execution.Output);
        Assert.Equal(QueueStatus.Processed, (await db.TaskQueue.SingleAsync(q => q.TaskId == taskId)).Status);
        await Wait.UntilAsync(() => Task.FromResult(events.Count > 0), Timeout, "the TaskSucceeded event was raised");
    }

    [SkippableFact]
    public async Task FailedRun_IsRetriedWithBackOffAndFinallyMarkedFailed()
    {
        RequiresDatabase.Check();
        await database.SetSettingAsync(SettingKeys.RetryBaseDelaySeconds, 1);
        var services = database.CreateServices("retry");
        var taskId = await TaskSetup.CreateManualTaskAsync(services, FailingHandler.Type, maxRetries: 2);

        await RunEngineAsync(services, async () =>
        {
            await RunNowAsync(services, taskId);
            await Wait.UntilAsync(async () => (await StatusesAsync(taskId)).Count == 3, Timeout, "three attempts were made");
        });

        Assert.Equal([ExecutionStatus.Retrying, ExecutionStatus.Retrying, ExecutionStatus.Failed], await StatusesAsync(taskId));
        await using var db = database.CreateContext();
        var retries = await db.TaskQueue.Where(q => q.TaskId == taskId).OrderBy(q => q.Attempt).ToListAsync();
        Assert.Equal([0, 1, 2], retries.Select(r => r.Attempt));
        Assert.All(retries.Skip(1), r => Assert.Equal(TriggerType.Retry, r.TriggerType));
        // The second retry waits twice as long as the first (1 s, then 2 s).
        Assert.True(retries[2].ScheduledTimeUtc - retries[2].EnqueuedAtUtc >= TimeSpan.FromSeconds(1.9));
        Assert.Equal(3, await db.ErrorLogs.CountAsync(e => e.Execution!.TaskId == taskId));
    }

    [SkippableFact]
    public async Task ThrowingHandler_IsRecordedWithItsStackTrace()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("throw");
        var taskId = await TaskSetup.CreateManualTaskAsync(services, ThrowingHandler.Type);

        await RunEngineAsync(services, async () =>
        {
            await RunNowAsync(services, taskId);
            await Wait.UntilAsync(async () => await StatusesAsync(taskId) is [ExecutionStatus.Failed], Timeout, "the run failed");
        });

        await using var db = database.CreateContext();
        var error = await db.ErrorLogs.SingleAsync(e => e.Execution!.TaskId == taskId);
        Assert.Equal(nameof(InvalidOperationException), error.ErrorType);
        Assert.Contains("Boom", error.ErrorMessage);
        Assert.Contains("ThrowingHandler", error.StackTrace);
    }

    [SkippableFact]
    public async Task HandlerThatIgnoresItsTimeOut_IsAbandonedAndRecordedAsTimedOut()
    {
        RequiresDatabase.Check();
        TaskExecutor.AbandonGrace = TimeSpan.FromMilliseconds(500);
        var services = database.CreateServices("timeout");
        var taskId = await TaskSetup.CreateManualTaskAsync(services, HangingHandler.Type, timeoutSeconds: 1);

        await RunEngineAsync(services, async () =>
        {
            await RunNowAsync(services, taskId);
            await Wait.UntilAsync(async () => await StatusesAsync(taskId) is [ExecutionStatus.TimedOut], TimeSpan.FromSeconds(10), "the run timed out");
        });
    }

    [SkippableFact]
    public async Task Stop_CancelsRunningTasksAndQueuesThemAgain()
    {
        RequiresDatabase.Check();
        await database.SetSettingAsync(SettingKeys.ShutdownGraceSeconds, 0);
        var services = database.CreateServices("shutdown");
        var taskId = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type, payload: """{ "delayMs": 20000 }""");
        var engine = services.GetRequiredService<SchedulerEngine>();
        await engine.StartAsync();
        await RunNowAsync(services, taskId);
        await Wait.UntilAsync(async () => await StatusesAsync(taskId) is [ExecutionStatus.Running], Timeout, "the run started");

        await engine.StopAsync();
        await database.SetSettingAsync(SettingKeys.ShutdownGraceSeconds, 30);

        Assert.Equal([ExecutionStatus.Cancelled], await StatusesAsync(taskId));
        await using var db = database.CreateContext();
        var waiting = await db.TaskQueue.SingleAsync(q => q.TaskId == taskId && q.Status == QueueStatus.Waiting);
        Assert.Equal(TriggerType.Manual, waiting.TriggerType);
        Assert.All(await db.WorkerThreads.Where(w => w.HostName == "shutdown").ToListAsync(), w => Assert.Equal(WorkerStatus.Stopped, w.Status));
    }

    [SkippableFact]
    public async Task Recovery_RetriesRunsInterruptedByACrash_AndReleasesUnstartedClaims()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("recovery");
        var crashedTask = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type, maxRetries: 1);
        var claimedTask = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        var past = DateTime.UtcNow.AddMinutes(-10);

        // Simulate a host that crashed: one entry was running, one was claimed but never started.
        await using (var db = database.CreateContext())
        {
            var worker = new WorkerThread("crashed-host", WorkerThread.DefaultPoolName, past);
            db.WorkerThreads.Add(worker);
            await db.SaveChangesAsync();
            foreach (var id in new[] { crashedTask, claimedTask })
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO scheduler.task_queue (queue_id, task_id, trigger_type, priority, scheduled_time, enqueued_at, queue_status,
                        claimed_by, claimed_at, claimed_until, attempt)
                    VALUES ({Guid.CreateVersion7()}, {id}, 'Manual', 5, {past}, {past}, 'Claimed', {worker.Id}, {past}, {past.AddMinutes(1)}, 0)
                    """);
            }

            var crashedEntry = await db.TaskQueue.SingleAsync(q => q.TaskId == crashedTask);
            db.TaskExecutions.Add(TaskExecution.Start(crashedEntry, worker.Id, past));
            await db.SaveChangesAsync();
        }

        await RunEngineAsync(services, async () =>
        {
            await Wait.UntilAsync(async () => await StatusesAsync(crashedTask) is [ExecutionStatus.Retrying, ExecutionStatus.Success], Timeout,
                "the interrupted run was retried");
            await Wait.UntilAsync(async () => await StatusesAsync(claimedTask) is [ExecutionStatus.Success], Timeout, "the released entry ran");
        });

        await using var check = database.CreateContext();
        Assert.Equal(WorkerStatus.Stopped, (await check.WorkerThreads.SingleAsync(w => w.HostName == "crashed-host")).Status);
        Assert.Equal("Interrupted", (await check.ErrorLogs.SingleAsync(e => e.Execution!.TaskId == crashedTask)).ErrorType);
    }

    [SkippableFact]
    public async Task SuccessfulTask_QueuesItsDependants()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("dependency");
        var prerequisite = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        var dependant = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        await using (var db = database.CreateContext())
        {
            var task = await db.Tasks.Include(t => t.Dependencies).SingleAsync(t => t.Id == dependant);
            task.SetDependencies([prerequisite], DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        await RunEngineAsync(services, async () =>
        {
            await RunNowAsync(services, prerequisite);
            await Wait.UntilAsync(async () => await StatusesAsync(dependant) is [ExecutionStatus.Success], Timeout, "the dependant ran");
        });

        await using var check = database.CreateContext();
        Assert.Equal(TriggerType.Dependency, (await check.TaskExecutions.SingleAsync(e => e.TaskId == dependant)).TriggerType);
    }

    [SkippableTheory]
    [InlineData(MisfirePolicy.Skip, 0)]
    [InlineData(MisfirePolicy.RunOnce, 1)]
    [InlineData(MisfirePolicy.RunAll, 6)]
    public async Task MissedRuns_FollowTheMisfirePolicy(MisfirePolicy policy, int expectedRuns)
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("misfire-" + policy);
        var name = $"Every minute {policy} {Guid.NewGuid():N}";
        var taskId = await services.GetRequiredService<ISchedulerClient>().EnsureTaskAsync(new TaskDefinition
        {
            Name = name,
            TaskType = CountingHandler.Type,
            ScheduleType = ScheduleType.Interval,
            IntervalSeconds = 3600,
            MisfirePolicy = policy,
            StartTimeUtc = DateTime.UtcNow.AddHours(-5).AddMinutes(-30)
        });

        // The computer was off: the next run is five and a half hours in the past, so six hourly runs were missed.
        await using (var db = database.CreateContext())
        {
            await db.Schedules.Where(s => s.TaskId == taskId).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.NextRunTimeUtc, DateTime.UtcNow.AddHours(-5).AddMinutes(-30))
                .SetProperty(x => x.LastRunTimeUtc, (DateTime?)null));
        }

        await RunEngineAsync(services, async () =>
        {
            await Wait.UntilAsync(async () =>
            {
                await using var db = database.CreateContext();
                return await db.Schedules.AnyAsync(s => s.TaskId == taskId && s.NextRunTimeUtc > DateTime.UtcNow);
            }, Timeout, "the schedule moved on");
            await Wait.UntilAsync(async () => (await StatusesAsync(taskId)).Count(s => s == ExecutionStatus.Success) == expectedRuns, Timeout,
                $"{expectedRuns} catch-up runs completed");
        });

        await using var check = database.CreateContext();
        Assert.All(await check.TaskQueue.Where(q => q.TaskId == taskId).ToListAsync(), q => Assert.Equal(TriggerType.CatchUp, q.TriggerType));
        Assert.Equal(expectedRuns, await check.TaskQueue.CountAsync(q => q.TaskId == taskId));
    }

    [SkippableFact]
    public async Task PausedTask_IsNotRunByItsSchedule()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("paused");
        var taskId = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        await using (var db = database.CreateContext())
        {
            var task = await db.Tasks.SingleAsync(t => t.Id == taskId);
            task.Pause(DateTime.UtcNow, null);
            db.TaskQueue.Add(QueueEntry.Create(task, null, TriggerType.Scheduled, DateTime.UtcNow, 0, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        await RunEngineAsync(services, async () =>
            await Wait.UntilAsync(async () =>
            {
                await using var db = database.CreateContext();
                return await db.TaskQueue.AnyAsync(q => q.TaskId == taskId && q.Status == QueueStatus.Cancelled);
            }, Timeout, "the queued run was cancelled"));

        Assert.Empty(await StatusesAsync(taskId));
    }

    private async Task RunEngineAsync(IServiceProvider services, Func<Task> body)
    {
        var engine = services.GetRequiredService<SchedulerEngine>();
        await engine.StartAsync();
        try
        {
            await body();
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    private static async Task RunNowAsync(IServiceProvider services, Guid taskId)
    {
        await using var db = services.GetRequiredService<IDbContextFactory<Infrastructure.Persistence.SchedulerDbContext>>().CreateDbContext();
        var name = await db.Tasks.Where(t => t.Id == taskId).Select(t => t.Name).SingleAsync();
        Assert.True(await services.GetRequiredService<ISchedulerClient>().RunNowAsync(name));
    }

    private async Task<List<ExecutionStatus>> StatusesAsync(Guid taskId)
    {
        await using var db = database.CreateContext();
        return await db.TaskExecutions.Where(e => e.TaskId == taskId).OrderBy(e => e.StartTimeUtc).Select(e => e.Status).ToListAsync();
    }
}
