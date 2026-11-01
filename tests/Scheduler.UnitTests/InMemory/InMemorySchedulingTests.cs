using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Configuration;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Engine;
using Scheduler.Engine.Execution;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.UnitTests.InMemory;

/// <summary>Task states, the "All" dependency rule, misfires, recovery, shutdown and the idle behaviour (FR-03, FR-15, FR-26, FR-33, FR-34).</summary>
public class InMemorySchedulingTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Dependant_RunsOnlyAfterAllPrerequisitesSucceeded_OncePerRound()
    {
        await using var host = new InMemoryHost();
        var (a, b, dependant) = await CreateChainAsync(host);

        await host.RunEngineAsync(async () =>
        {
            await RunAndWaitAsync(host, a, 1);
            await host.IdleCyclesAsync();
            Assert.Equal(0, Successes(host, dependant));

            await RunAndWaitAsync(host, b, 1);
            await InMemoryHost.WaitUntilAsync(() => Successes(host, dependant) == 1, "the dependant ran after A and B");

            await RunAndWaitAsync(host, a, 2);
            await host.IdleCyclesAsync();
            Assert.Equal(1, Successes(host, dependant));
            await RunAndWaitAsync(host, b, 2);
            await InMemoryHost.WaitUntilAsync(() => Successes(host, dependant) == 2, "the dependant ran a second time");
        });

        Assert.All(host.Database.Executions.Where(e => e.TaskId == dependant), e => Assert.Equal(TriggerType.Dependency, e.TriggerType));
    }

    [Fact]
    public async Task FinalFailureOfAPrerequisite_StopsTheRound()
    {
        await using var host = new InMemoryHost();
        var (a, b, dependant) = await CreateChainAsync(host);

        await host.RunEngineAsync(async () =>
        {
            await RunAndWaitAsync(host, a, 1);
            InMemoryDatabase.SetProperty(host.TaskOf(a), nameof(ScheduledTask.TaskType), FailingHandler.Type);
            await RunAndWaitAsync(host, a, 2);
            await RunAndWaitAsync(host, b, 1);
            await host.IdleCyclesAsync();
            Assert.Equal(0, Successes(host, dependant));

            InMemoryDatabase.SetProperty(host.TaskOf(a), nameof(ScheduledTask.TaskType), CountingHandler.Type);
            await RunAndWaitAsync(host, a, 3);
            await InMemoryHost.WaitUntilAsync(() => Successes(host, dependant) == 1, "the dependant ran once A succeeded again");
        });
    }

    [Fact]
    public async Task ManualRunOfAPausedTask_KeepsItsRetries_AndADisabledTaskCannotRun()
    {
        await using var host = new InMemoryHost();
        host.SetSetting(SettingKeys.RetryBaseDelaySeconds, 1);
        host.SetSetting(SettingKeys.RetryMaxDelaySeconds, 1);
        var taskId = await host.CreateManualTaskAsync(FailingHandler.Type, maxRetries: 2);
        host.TaskOf(taskId).Pause(DateTime.UtcNow, null);

        await host.RunEngineAsync(async () =>
        {
            Assert.True(await host.Get<ISchedulerClient>().RunNowAsync(host.NameOf(taskId)));
            await InMemoryHost.WaitUntilAsync(() => host.Statuses(taskId) is [ExecutionStatus.Retrying, ExecutionStatus.Retrying, ExecutionStatus.Failed],
                "the manual run and its two retries ran", Long);
        });

        Assert.True(host.TaskOf(taskId).CanChangeStatusTo(ScheduledTaskStatus.Disabled));
        host.TaskOf(taskId).Disable(DateTime.UtcNow, null);
        Assert.False(await host.Get<ISchedulerClient>().RunNowAsync(host.NameOf(taskId)));
    }

    [Fact]
    public async Task ExecutionLimit_CompletesTaskAfterTheConfiguredNumberOfStartedRuns()
    {
        await using var host = new InMemoryHost();
        host.SetSetting(SettingKeys.MaxConcurrency, 1);
        var client = host.Get<ISchedulerClient>();
        var taskId = await client.EnsureTaskAsync(new TaskDefinition
        {
            Name = "Limited task",
            TaskType = CountingHandler.Type,
            CronExpression = "0 0 1 1 *",
            MaxExecutions = 2
        });

        Assert.True(await client.RunNowAsync("Limited task"));
        Assert.True(await client.RunNowAsync("Limited task"));
        Assert.True(await client.RunNowAsync("Limited task"));
        await host.RunEngineAsync(() => InMemoryHost.WaitUntilAsync(
            () => host.Statuses(taskId).Count == 2, "both allowed executions started"));

        Assert.Equal(2, host.TaskOf(taskId).ExecutionCount);
        Assert.Equal(ScheduledTaskStatus.Completed, host.TaskOf(taskId).Status);
        Assert.False(await client.RunNowAsync("Limited task"));
        Assert.DoesNotContain(host.Database.Queue, entry => entry.TaskId == taskId && entry.Status == QueueStatus.Waiting);
        Assert.Contains(host.Database.Queue, entry => entry.TaskId == taskId && entry.Status == QueueStatus.Cancelled);
    }

    [Fact]
    public async Task ExecutionLimit_CountsRetriesAndPreventsFurtherRetryStarts()
    {
        await using var host = new InMemoryHost();
        host.SetSetting(SettingKeys.RetryBaseDelaySeconds, 1);
        host.SetSetting(SettingKeys.RetryMaxDelaySeconds, 1);
        var taskId = await host.Get<ISchedulerClient>().EnsureTaskAsync(new TaskDefinition
        {
            Name = "Limited failing task",
            TaskType = FailingHandler.Type,
            CronExpression = "0 0 1 1 *",
            MaxRetries = 5,
            MaxExecutions = 2
        });

        await host.RunEngineAsync(async () =>
        {
            Assert.True(await host.Get<ISchedulerClient>().RunNowAsync("Limited failing task"));
            await InMemoryHost.WaitUntilAsync(() => host.Statuses(taskId).Count == 2, "the initial run and one retry started", Long);
        });

        Assert.Equal([ExecutionStatus.Retrying, ExecutionStatus.Failed], host.Statuses(taskId));
        Assert.Equal(2, host.TaskOf(taskId).ExecutionCount);
        Assert.Equal(ScheduledTaskStatus.Completed, host.TaskOf(taskId).Status);
    }

    [Fact]
    public async Task EventTrigger_QueuesBySystemKeyAndPassesSeparateEventDataToHandler()
    {
        await using var host = new InMemoryHost();
        var client = host.Get<ISchedulerClient>();
        var taskId = await client.EnsureTaskAsync(new TaskDefinition
        {
            Name = "Event task",
            SystemKey = "test.event-task",
            TaskType = CountingHandler.Type,
            CronExpression = "0 0 1 1 *",
            Payload = """{"configured":"value"}"""
        });

        await host.RunEngineAsync(async () =>
        {
            Assert.True(await client.TriggerEventAsync("test.event-task", "product.updated", "Inventory Monitor",
                """{"productId":"sku-1"}"""));
            await InMemoryHost.WaitUntilAsync(() => host.Statuses(taskId).Count == 1, "the event-triggered execution completed");
        });

        var execution = Assert.Single(host.Database.Executions, item => item.TaskId == taskId);
        Assert.Equal(TriggerType.Event, execution.TriggerType);
        Assert.Equal(("product.updated", "Inventory Monitor", """{"productId":"sku-1"}""", """{"configured":"value"}"""),
            CountingHandler.Events[execution.Id]);
        Assert.False(await client.TriggerEventAsync("missing.key", "product.updated", "Inventory Monitor", "{}"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.TriggerEventAsync("test.event-task", "product.updated", "Inventory Monitor", "[]"));
        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() =>
            client.TriggerEventAsync("test.event-task", "product.updated", "Inventory Monitor", "not-json"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.TriggerEventAsync("test.event-task", "product.updated", "Inventory Monitor", new string('x', 65 * 1024)));
    }

    [Fact]
    public async Task HealthMonitor_LogsAndNotifiesOnRepeatedTaskFailures()
    {
        await using var host = new InMemoryHost(services =>
        {
            new Scheduler.Modules.Security.SecurityModule().Register(services);
            services.AddSingleton<IResourceHealthReader>(new FixedHealthReader(
                new ResourceHealthSnapshot(0, 0, 0, 1, 0,
                    [new TaskExecutionHealth(Guid.NewGuid(), "Repeated failure", 3, 0)])));
        });
        var users = host.Get<Scheduler.Modules.Security.Authentication.UserManagement>();
        Assert.True((await users.CreateFirstDeveloperAsync("admin", "Correct horse 42", "Correct horse 42")).Succeeded);
        var administratorId = (await users.ListAsync()).Single().Id;
        host.SetSetting(SettingKeys.HealthFailureThreshold, 2);
        host.SetSetting(SettingKeys.HealthTimeoutThreshold, 10);
        host.SetSetting(SettingKeys.HealthQueueBacklogThreshold, 1000);
        host.SetSetting(SettingKeys.HealthCpuPercentThreshold, 100);
        host.SetSetting(SettingKeys.HealthMemoryMegabytesThreshold, 1_000_000);

        await host.RunEngineAsync(() => InMemoryHost.WaitUntilAsync(
            () => host.Logger.Entries.Any(entry => entry.Message.Contains("has 3 failures in the last 10 minutes.", StringComparison.Ordinal)),
            "the repeated-failure health alert was logged"));

        Assert.Contains(host.Notifications.Items, item => item.Item.Source == "Scheduler Health"
            && item.Item.Title == "Scheduler health alert" && item.UserId == administratorId);
    }

    [Fact]
    public async Task PausedTask_IsNotRunByItsSchedule()
    {
        await using var host = new InMemoryHost();
        var taskId = await host.CreateManualTaskAsync(CountingHandler.Type);
        host.TaskOf(taskId).Pause(DateTime.UtcNow, null);
        await host.EnqueueAsync(taskId, DateTime.UtcNow, TriggerType.Scheduled);

        await host.RunEngineAsync(() => InMemoryHost.WaitUntilAsync(
            () => host.Database.Locked(() => host.Database.Queue.Any(q => q.TaskId == taskId && q.Status == QueueStatus.Cancelled)),
            "the queued run was cancelled"));

        Assert.Empty(host.Statuses(taskId));
    }

    [Theory]
    [InlineData(MisfirePolicy.Skip, 0)]
    [InlineData(MisfirePolicy.RunOnce, 1)]
    [InlineData(MisfirePolicy.RunAll, 6)]
    public async Task MissedRuns_FollowTheMisfirePolicy(MisfirePolicy policy, int expectedRuns)
    {
        await using var host = new InMemoryHost();
        var missedSince = DateTime.UtcNow.AddHours(-5).AddMinutes(-30);
        var taskId = await host.Get<ISchedulerClient>().EnsureTaskAsync(new TaskDefinition
        {
            Name = $"Every hour {policy}",
            TaskType = CountingHandler.Type,
            ScheduleType = ScheduleType.Interval,
            IntervalSeconds = 3600,
            MisfirePolicy = policy,
            StartTimeUtc = missedSince
        });

        // The computer was off: the next run is five and a half hours in the past, so six hourly runs were missed.
        var schedule = host.TaskOf(taskId).Schedules.Single(s => !s.IsDeleted);
        InMemoryDatabase.SetProperty(schedule, nameof(Schedule.NextRunTimeUtc), missedSince);
        InMemoryDatabase.SetProperty(schedule, nameof(Schedule.LastRunTimeUtc), null);

        await host.RunEngineAsync(async () =>
        {
            await InMemoryHost.WaitUntilAsync(() => schedule.NextRunTimeUtc > DateTime.UtcNow, "the schedule moved on");
            await InMemoryHost.WaitUntilAsync(() => host.Statuses(taskId).Count(s => s == ExecutionStatus.Success) == expectedRuns,
                $"{expectedRuns} catch-up runs completed");
        });

        var entries = host.Database.Queue.Where(q => q.TaskId == taskId).ToList();
        Assert.Equal(expectedRuns, entries.Count);
        Assert.All(entries, q => Assert.Equal(TriggerType.CatchUp, q.TriggerType));
    }

    [Fact]
    public async Task DueTaskOfAnUnregisteredType_IsNotQueued()
    {
        await using var host = new InMemoryHost();
        var taskId = await host.CreateManualTaskAsync(CountingHandler.Type);
        var due = DateTime.UtcNow.AddSeconds(-1);
        // As if the module that provided the type had been removed.
        InMemoryDatabase.SetProperty(host.TaskOf(taskId), nameof(ScheduledTask.TaskType), "Removed.Module.Task");
        var schedule = host.TaskOf(taskId).Schedules.Single(s => !s.IsDeleted);
        InMemoryDatabase.SetProperty(schedule, nameof(Schedule.NextRunTimeUtc), due);

        await host.RunEngineAsync(host.IdleCyclesAsync);

        Assert.DoesNotContain(host.Database.Queue, q => q.TaskId == taskId);
        Assert.Equal(due, schedule.NextRunTimeUtc);
    }

    [Fact]
    public async Task Recovery_RetriesRunsInterruptedByACrash_AndReleasesUnstartedClaims()
    {
        await using var host = new InMemoryHost();
        host.SetSetting(SettingKeys.RetryBaseDelaySeconds, 1);
        var crashedTask = await host.CreateManualTaskAsync(CountingHandler.Type, maxRetries: 1);
        var claimedTask = await host.CreateManualTaskAsync(CountingHandler.Type);
        var past = DateTime.UtcNow.AddMinutes(-10);

        // A host that crashed: one entry was running, one was claimed but never started.
        var worker = new WorkerThread("crashed-host", WorkerThread.DefaultPoolName, past);
        var crashed = await host.EnqueueAsync(crashedTask, past);
        var claimed = await host.EnqueueAsync(claimedTask, past);
        await using (var unitOfWork = host.Database.Create())
        {
            unitOfWork.Workers.Add(worker);
            foreach (var entry in new[] { crashed, claimed })
            {
                InMemoryDatabase.SetProperty(entry, nameof(QueueEntry.Status), QueueStatus.Claimed);
                InMemoryDatabase.SetProperty(entry, nameof(QueueEntry.ClaimedBy), worker.Id);
                InMemoryDatabase.SetProperty(entry, nameof(QueueEntry.ClaimedAtUtc), past);
                InMemoryDatabase.SetProperty(entry, nameof(QueueEntry.ClaimedUntilUtc), past.AddMinutes(1));
            }

            unitOfWork.Executions.Add(TaskExecution.Start(crashed, worker.Id, past));
            await unitOfWork.SaveChangesAsync();
        }

        await host.RunEngineAsync(async () =>
        {
            await InMemoryHost.WaitUntilAsync(() => host.Statuses(crashedTask) is [ExecutionStatus.Retrying, ExecutionStatus.Success],
                "the interrupted run was retried");
            await InMemoryHost.WaitUntilAsync(() => host.Statuses(claimedTask) is [ExecutionStatus.Success], "the released entry ran");
        });

        Assert.Equal(WorkerStatus.Stopped, worker.Status);
        var crashedExecution = host.Database.Executions.First(e => e.TaskId == crashedTask);
        Assert.Equal("Interrupted", host.Database.Errors.Single(e => e.ExecutionId == crashedExecution.Id).ErrorType);
    }

    [Fact]
    public async Task Stop_CancelsRunningTasks_AndQueuesThemAgain()
    {
        await using var host = new InMemoryHost();
        host.SetSetting(SettingKeys.ShutdownGraceSeconds, 0);
        var taskId = await host.CreateManualTaskAsync(CountingHandler.Type, payload: """{ "delayMs": 20000 }""");
        var engine = host.Get<SchedulerEngine>();
        await engine.StartAsync();
        await host.Get<ISchedulerClient>().RunNowAsync(host.NameOf(taskId));
        await InMemoryHost.WaitUntilAsync(() => host.Statuses(taskId) is [ExecutionStatus.Running], "the run started");

        await engine.StopAsync();

        Assert.Equal([ExecutionStatus.Cancelled], host.Statuses(taskId));
        var waiting = host.Database.Queue.Single(q => q.TaskId == taskId && q.Status == QueueStatus.Waiting);
        Assert.Equal(TriggerType.Manual, waiting.TriggerType);
        Assert.All(host.Database.Workers, w => Assert.Equal(WorkerStatus.Stopped, w.Status));
    }

    [Fact]
    public async Task StoppedWorkerOfThisHost_IsRevived()
    {
        await using var host = new InMemoryHost();
        await host.RunEngineAsync(async () =>
        {
            // As if another, older engine had marked this host's workers as stale.
            host.Database.Locked(() =>
            {
                host.Database.Workers.ForEach(w => InMemoryDatabase.SetProperty(w, nameof(WorkerThread.Status), WorkerStatus.Stopped));
                return 0;
            });
            host.Get<IEngineSignal>().Wake();
            await InMemoryHost.WaitUntilAsync(() => host.Database.Locked(() => host.Database.Workers.All(w => w.Status != WorkerStatus.Stopped)),
                "the workers were revived");

            var taskId = await host.CreateManualTaskAsync(CountingHandler.Type);
            await host.EnqueueAsync(taskId, DateTime.UtcNow);
            host.Get<IEngineSignal>().Wake();
            await InMemoryHost.WaitUntilAsync(() => host.Statuses(taskId) is [ExecutionStatus.Success], "a run on a revived worker succeeded");
        });
    }

    [Fact]
    public async Task Engine_DoesNotSpinWhileTheOnlyWorkerIsBusy()
    {
        await using var host = new InMemoryHost();
        host.SetSetting(SettingKeys.MaxConcurrency, 1);
        var slow = await host.CreateManualTaskAsync(CountingHandler.Type, payload: """{ "delayMs": 4000 }""");
        var waiting = await host.CreateManualTaskAsync(CountingHandler.Type);
        var now = DateTime.UtcNow;
        await host.EnqueueAsync(slow, now);
        await host.EnqueueAsync(waiting, now.AddMilliseconds(1));
        var engine = host.Get<SchedulerEngine>();

        await host.RunEngineAsync(async () =>
        {
            await InMemoryHost.WaitUntilAsync(() => engine.RunningCount == 1, "the slow task started");
            var before = engine.CycleCount;
            await Task.Delay(TimeSpan.FromSeconds(3));
            var cycles = engine.CycleCount - before;
            Assert.True(cycles <= 3, $"The engine ran {cycles} cycles in 3 seconds while its only worker was busy.");
        });
    }

    [Fact]
    public async Task OlderTaskWithTheSameNameAndType_IsTakenOverByItsKey_AndRestored()
    {
        await using var host = new InMemoryHost();
        var older = await host.CreateManualTaskAsync(CountingHandler.Type, name: "Nightly backup");
        host.TaskOf(older).Delete(DateTime.UtcNow, null);

        var id = await host.Get<ISchedulerClient>().EnsureTaskAsync(new TaskDefinition
        {
            Name = "Nightly backup",
            SystemKey = "test.nightly",
            TaskType = CountingHandler.Type,
            CronExpression = "0 1 * * *"
        });

        Assert.Equal(older, id);
        Assert.Equal("test.nightly", host.TaskOf(id).SystemKey);
        Assert.False(host.TaskOf(id).IsDeleted);
    }

    private static async Task<(Guid A, Guid B, Guid Dependant)> CreateChainAsync(InMemoryHost host)
    {
        var a = await host.CreateManualTaskAsync(CountingHandler.Type);
        var b = await host.CreateManualTaskAsync(CountingHandler.Type);
        var dependant = await host.CreateManualTaskAsync(CountingHandler.Type);
        host.TaskOf(dependant).SetDependencies([a, b], DateTime.UtcNow);
        return (a, b, dependant);
    }

    private static async Task RunAndWaitAsync(InMemoryHost host, Guid taskId, int finishedRuns)
    {
        Assert.True(await host.Get<ISchedulerClient>().RunNowAsync(host.NameOf(taskId)));
        await InMemoryHost.WaitUntilAsync(() => host.Statuses(taskId).Count(s => s != ExecutionStatus.Running) >= finishedRuns, "the prerequisite finished");
    }

    private static int Successes(InMemoryHost host, Guid taskId) => host.Statuses(taskId).Count(s => s == ExecutionStatus.Success);
}

/// <summary>
/// At-least-once delivery (NFR-01, WP-2) and time-outs. These tests change static hooks of the executor, so they run
/// one after another in their own class.
/// </summary>
public class InMemoryDeliveryTests
{
    [Fact]
    public async Task DueTask_StartsWithinFiveSecondsOfItsDueTime()
    {
        await using var host = new InMemoryHost();
        await host.RunEngineAsync(async () =>
        {
            var due = DateTime.UtcNow.AddSeconds(3);
            var taskId = await host.Get<ISchedulerClient>().EnsureTaskAsync(new TaskDefinition
            {
                Name = "Lateness",
                TaskType = CountingHandler.Type,
                ScheduleType = ScheduleType.Once,
                StartTimeUtc = due
            });

            await InMemoryHost.WaitUntilAsync(() => host.Statuses(taskId).Count > 0, "the run started", TimeSpan.FromSeconds(20));
            var started = host.Database.Executions.First(e => e.TaskId == taskId).StartTimeUtc;
            Assert.True(started - due <= TimeSpan.FromSeconds(5), $"The run started {(started - due).TotalSeconds:0.0} s after its due time.");
        });
    }

    [Fact]
    public async Task CrashBeforeTheClaim_TheWaitingEntryRunsAfterRestart()
    {
        await using var host = new InMemoryHost();
        var taskId = await host.CreateManualTaskAsync(CountingHandler.Type);
        await host.EnqueueAsync(taskId, DateTime.UtcNow.AddMinutes(-5));

        await host.RunEngineAsync(() => InMemoryHost.WaitUntilAsync(() => host.Statuses(taskId) is [ExecutionStatus.Success], "the waiting entry ran"));
    }

    [Fact]
    public async Task ResultThatCannotBeSaved_IsRetried_AndOtherwiseTheTaskRunsAgain()
    {
        await using var host = new InMemoryHost();
        host.SetSetting(SettingKeys.LeaseGraceSeconds, 10);
        host.SetSetting(SettingKeys.RetryBaseDelaySeconds, 1);
        var flaky = await host.CreateManualTaskAsync(CountingHandler.Type, maxRetries: 0, timeoutSeconds: 5);
        var lost = await host.CreateManualTaskAsync(CountingHandler.Type, maxRetries: 1, timeoutSeconds: 5);
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

        try
        {
            await host.RunEngineAsync(async () =>
            {
                var client = host.Get<ISchedulerClient>();
                await client.RunNowAsync(host.NameOf(flaky));
                await client.RunNowAsync(host.NameOf(lost));

                // Two failed saves: the third attempt saves the result, and the handler ran once.
                await InMemoryHost.WaitUntilAsync(() => host.Statuses(flaky) is [ExecutionStatus.Success], "the flaky save succeeded");
                Assert.Single(CountingHandler.Runs, r => r.Value == flaky);

                // Four failed saves: the result is lost, the lease expires and recovery runs the task again (at-least-once).
                await InMemoryHost.WaitUntilAsync(() =>
                {
                    host.Get<IEngineSignal>().Wake();
                    return host.Statuses(lost) is [ExecutionStatus.Retrying, ExecutionStatus.Success];
                }, "the task whose result was lost ran again", TimeSpan.FromSeconds(60));
                Assert.Equal(2, CountingHandler.Runs.Count(r => r.Value == lost));
            });
        }
        finally
        {
            TaskExecutor.BeforeResultSaved = null;
            TaskExecutor.SaveRetryDelays = delays;
        }
    }

    [Fact]
    public async Task HandlerThatIgnoresItsTimeOut_IsAbandonedAndRecordedAsTimedOut()
    {
        TaskExecutor.AbandonGrace = TimeSpan.FromMilliseconds(500);
        await using var host = new InMemoryHost();
        var taskId = await host.CreateManualTaskAsync(HangingHandler.Type, timeoutSeconds: 1);

        await host.RunEngineAsync(async () =>
        {
            await host.Get<ISchedulerClient>().RunNowAsync(host.NameOf(taskId));
            await InMemoryHost.WaitUntilAsync(() => host.Statuses(taskId) is [ExecutionStatus.TimedOut], "the run timed out");
        });
    }

}

internal sealed class FixedHealthReader(ResourceHealthSnapshot snapshot) : IResourceHealthReader
{
    public Task<ResourceHealthSnapshot> GetSnapshotAsync(DateTime executionsSinceUtc, DateTime staleBeforeUtc,
        CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
}
