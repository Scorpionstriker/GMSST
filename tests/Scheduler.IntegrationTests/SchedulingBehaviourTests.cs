using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Configuration;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Engine;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.IntegrationTests;

/// <summary>WP-6: the "All" dependency rule, the task state table and the claim order.</summary>
public sealed class SchedulingBehaviourTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [SkippableFact]
    public async Task Dependant_RunsOnlyAfterAllPrerequisitesSucceeded_OncePerRound()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("all-rule");
        var (a, b, dependant) = await CreateChainAsync(services);

        await RunEngineAsync(services, async () =>
        {
            // Only A succeeded: no run.
            await RunAndWaitAsync(services, a, 1);
            await IdleCyclesAsync(services);
            Assert.Equal(0, await RunsAsync(dependant));

            // Then B: one run.
            await RunAndWaitAsync(services, b, 1);
            await Wait.UntilAsync(async () => await RunsAsync(dependant) == 1, Timeout, "the dependant ran after A and B");

            // A, B again: a second run, and only one.
            await RunAndWaitAsync(services, a, 2);
            await IdleCyclesAsync(services);
            Assert.Equal(1, await RunsAsync(dependant));
            await RunAndWaitAsync(services, b, 2);
            await Wait.UntilAsync(async () => await RunsAsync(dependant) == 2, Timeout, "the dependant ran a second time");
        });

        await IdleCheckAsync(dependant, 2);
    }

    [SkippableFact]
    public async Task FinalFailureOfAPrerequisite_StopsTheRound()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("all-rule-failure");
        var (a, b, dependant) = await CreateChainAsync(services);

        await RunEngineAsync(services, async () =>
        {
            // A succeeds, then A fails with no retries left, then B succeeds: the round is broken, no run.
            await RunAndWaitAsync(services, a, 1);
            await SetTypeAsync(a, FailingHandler.Type);
            await RunAndWaitAsync(services, a, 2);
            await RunAndWaitAsync(services, b, 1);
            await IdleCyclesAsync(services);
            Assert.Equal(0, await RunsAsync(dependant));

            // A succeeds again: now both have succeeded since the last dependency run.
            await SetTypeAsync(a, CountingHandler.Type);
            await RunAndWaitAsync(services, a, 3);
            await Wait.UntilAsync(async () => await RunsAsync(dependant) == 1, Timeout, "the dependant ran once A succeeded again");
        });
    }

    [SkippableFact]
    public async Task ManualRunOfAPausedTask_KeepsItsRetries_AndADisabledTaskCannotRun()
    {
        RequiresDatabase.Check();
        await database.SetSettingAsync(SettingKeys.RetryBaseDelaySeconds, 1);
        await database.SetSettingAsync(SettingKeys.RetryMaxDelaySeconds, 1);
        var services = database.CreateServices("paused");
        var taskId = await TaskSetup.CreateManualTaskAsync(services, FailingHandler.Type, maxRetries: 2);
        await SetStatusAsync(taskId, ScheduledTaskStatus.Paused);

        await RunEngineAsync(services, async () =>
        {
            await RunNowAsync(services, taskId);
            await Wait.UntilAsync(async () => await StatusesAsync(taskId) is [ExecutionStatus.Retrying, ExecutionStatus.Retrying, ExecutionStatus.Failed],
                Timeout, "the manual run and its two retries ran");
        });

        // Paused -> Disabled is allowed (WP-6 state table), and Run now is then refused.
        await using (var db = database.CreateContext())
        {
            var task = await db.Tasks.SingleAsync(t => t.Id == taskId);
            Assert.True(task.CanChangeStatusTo(ScheduledTaskStatus.Disabled));
            task.Disable(DateTime.UtcNow, null);
            await db.SaveChangesAsync();
        }

        var name = await NameAsync(taskId);
        Assert.False(await services.GetRequiredService<ISchedulerClient>().RunNowAsync(name));
    }

    [SkippableFact]
    public async Task Claim_TakesHighestPriorityFirst_ThenTheEntryQueuedFirst()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("priority");
        var low = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        var high = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        Guid worker;
        var due = DateTime.UtcNow.AddSeconds(-5);
        QueueEntry lowEntry, highEntry, older, newer;
        await using (var db = database.CreateContext())
        {
            await db.Tasks.Where(t => t.Id == low).ExecuteUpdateAsync(s => s.SetProperty(t => t.Priority, 10));
            await db.Tasks.Where(t => t.Id == high).ExecuteUpdateAsync(s => s.SetProperty(t => t.Priority, 1));
            var lowTask = await db.Tasks.SingleAsync(t => t.Id == low);
            var highTask = await db.Tasks.SingleAsync(t => t.Id == high);
            lowEntry = QueueEntry.Create(lowTask, null, TriggerType.Manual, due, 0, due);
            highEntry = QueueEntry.Create(highTask, null, TriggerType.Manual, due, 0, due.AddSeconds(1));
            newer = QueueEntry.Create(lowTask, null, TriggerType.Manual, due.AddSeconds(1), 0, due.AddSeconds(3));
            older = QueueEntry.Create(lowTask, null, TriggerType.Manual, due.AddSeconds(1), 0, due.AddSeconds(2));
            db.TaskQueue.AddRange(lowEntry, highEntry, newer, older);
            var thread = new WorkerThread("priority", WorkerThread.DefaultPoolName, DateTime.UtcNow);
            db.WorkerThreads.Add(thread);
            worker = thread.Id;
            await db.SaveChangesAsync();
        }

        var claimed = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            await using var unitOfWork = services.GetRequiredService<IUnitOfWorkFactory>().Create();
            claimed.Add((await unitOfWork.Queue.ClaimNextAsync(worker, [CountingHandler.Type], DateTime.UtcNow, 60))!.Value);
        }

        // Priority 1 first; of the rest (priority 10) the earliest due time, then the entry queued first.
        Assert.Equal([highEntry.Id, lowEntry.Id, older.Id, newer.Id], claimed);
    }

    private async Task<(Guid A, Guid B, Guid Dependant)> CreateChainAsync(IServiceProvider services)
    {
        var a = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        var b = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        var dependant = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        await using var db = database.CreateContext();
        var task = await db.Tasks.Include(t => t.Dependencies).SingleAsync(t => t.Id == dependant);
        task.SetDependencies([a, b], DateTime.UtcNow);
        await db.SaveChangesAsync();
        return (a, b, dependant);
    }

    private async Task RunAndWaitAsync(IServiceProvider services, Guid taskId, int finishedRuns)
    {
        await RunNowAsync(services, taskId);
        await Wait.UntilAsync(async () =>
        {
            await using var db = database.CreateContext();
            return await db.TaskExecutions.CountAsync(e => e.TaskId == taskId && e.Status != ExecutionStatus.Running) >= finishedRuns;
        }, Timeout, "the prerequisite finished");
    }

    private static async Task IdleCyclesAsync(IServiceProvider services)
    {
        // The engine sleeps until it is woken, so wake it for each of two cycles.
        var engine = services.GetRequiredService<SchedulerEngine>();
        var signal = services.GetRequiredService<Scheduler.Application.Common.IEngineSignal>();
        for (var i = 0; i < 2; i++)
        {
            var cycles = engine.CycleCount;
            signal.Wake();
            await Wait.UntilAsync(() => Task.FromResult(engine.CycleCount > cycles), Timeout, "a cycle ran");
        }

        await Wait.UntilAsync(() => Task.FromResult(engine.RunningCount == 0), Timeout, "no run is in progress");
    }

    private async Task IdleCheckAsync(Guid dependant, int expected)
    {
        await using var db = database.CreateContext();
        Assert.Equal(expected, await db.TaskExecutions.CountAsync(e => e.TaskId == dependant));
        Assert.True(await db.TaskExecutions.Where(e => e.TaskId == dependant).AllAsync(e => e.TriggerType == TriggerType.Dependency));
    }

    private async Task<int> RunsAsync(Guid taskId)
    {
        await using var db = database.CreateContext();
        return await db.TaskExecutions.CountAsync(e => e.TaskId == taskId && e.Status == ExecutionStatus.Success);
    }

    private async Task SetTypeAsync(Guid taskId, string type)
    {
        await using var db = database.CreateContext();
        await db.Tasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.TaskType, type));
    }

    private async Task SetStatusAsync(Guid taskId, ScheduledTaskStatus status)
    {
        await using var db = database.CreateContext();
        await db.Tasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, status));
    }

    private async Task<string> NameAsync(Guid taskId)
    {
        await using var db = database.CreateContext();
        return await db.Tasks.Where(t => t.Id == taskId).Select(t => t.Name).SingleAsync();
    }

    private async Task RunNowAsync(IServiceProvider services, Guid taskId) =>
        Assert.True(await services.GetRequiredService<ISchedulerClient>().RunNowAsync(await NameAsync(taskId)));

    private async Task<List<ExecutionStatus>> StatusesAsync(Guid taskId)
    {
        await using var db = database.CreateContext();
        return await db.TaskExecutions.Where(e => e.TaskId == taskId).OrderBy(e => e.StartTimeUtc).Select(e => e.Status).ToListAsync();
    }

    private static async Task RunEngineAsync(IServiceProvider services, Func<Task> body)
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
}
