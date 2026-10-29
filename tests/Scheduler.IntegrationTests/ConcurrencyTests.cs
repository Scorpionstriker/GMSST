using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Configuration;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Engine;
using Scheduler.Extensibility.Events;

namespace Scheduler.IntegrationTests;

/// <summary>
/// NFR-03: concurrent workers never claim or run the same entry twice, and only one engine runs per database (WP-1).
/// </summary>
public sealed class ConcurrencyTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    [SkippableFact]
    public async Task ParallelClaims_TakeEveryEntryExactlyOnce()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("claim-test");
        var taskId = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        var workers = new List<Guid>();
        await using (var db = database.CreateContext())
        {
            var task = await db.Tasks.SingleAsync(t => t.Id == taskId);
            var now = DateTime.UtcNow.AddSeconds(-1);
            for (var i = 0; i < 200; i++)
            {
                db.TaskQueue.Add(QueueEntry.Create(task, null, TriggerType.Manual, now, 0, now));
            }

            for (var i = 0; i < 8; i++)
            {
                var worker = new WorkerThread("claim-test", WorkerThread.DefaultPoolName, now);
                db.WorkerThreads.Add(worker);
                workers.Add(worker.Id);
            }

            await db.SaveChangesAsync();
        }

        var factory = services.GetRequiredService<IUnitOfWorkFactory>();
        var claimed = new ConcurrentBag<Guid>();
        await Task.WhenAll(workers.Select(worker => Task.Run(async () =>
        {
            while (true)
            {
                await using var unitOfWork = factory.Create();
                if (await unitOfWork.Queue.ClaimNextAsync(worker, [CountingHandler.Type], DateTime.UtcNow, 60) is not { } id)
                {
                    return;
                }

                claimed.Add(id);
            }
        })));

        Assert.Equal(200, claimed.Count);
        Assert.Equal(200, claimed.Distinct().Count());
    }

    [SkippableFact]
    public async Task SecondEngine_DoesNotStart_AndReportsTheFirstHost()
    {
        RequiresDatabase.Check();
        var first = database.CreateServices("host-a").GetRequiredService<SchedulerEngine>();
        var second = database.CreateServices("host-b").GetRequiredService<SchedulerEngine>();
        await first.StartAsync();
        try
        {
            await second.StartAsync();
            Assert.Equal(EngineState.Running, first.State);
            Assert.Equal(EngineState.Stopped, second.State);
            Assert.Equal("The scheduler engine is already running on host-a. Only one scheduler may run per database.", second.BlockedMessage);

            await using var db = database.CreateContext();
            Assert.False(await db.WorkerThreads.AnyAsync(w => w.HostName == "host-b"));
            Assert.Equal("host-a", (await db.EngineInstances.SingleAsync()).HostName);
        }
        finally
        {
            await first.StopAsync();
        }

        // Once the first engine stops, the lock is free again.
        await second.StartAsync();
        try
        {
            Assert.Equal(EngineState.Running, second.State);
            Assert.Null(second.BlockedMessage);
        }
        finally
        {
            await second.StopAsync();
        }
    }

    [SkippableFact]
    public async Task DueTaskOfAnUnregisteredType_IsNotQueued()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("types");
        var taskId = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        var due = DateTime.UtcNow.AddSeconds(-1);
        await using (var db = database.CreateContext())
        {
            // As if the module that provided the type had been removed.
            await db.Tasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.TaskType, "Removed.Module.Task"));
            await db.Schedules.Where(s => s.TaskId == taskId).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextRunTimeUtc, due));
        }

        var engine = services.GetRequiredService<SchedulerEngine>();
        await engine.StartAsync();
        try
        {
            // The engine sleeps until it is woken, so wake it for each of two cycles.
            var signal = services.GetRequiredService<Scheduler.Application.Common.IEngineSignal>();
            for (var i = 0; i < 2; i++)
            {
                var cycles = engine.CycleCount;
                signal.Wake();
                await Wait.UntilAsync(() => Task.FromResult(engine.CycleCount > cycles), TimeSpan.FromSeconds(30), "a cycle ran");
            }
        }
        finally
        {
            await engine.StopAsync();
        }

        await using var check = database.CreateContext();
        Assert.False(await check.TaskQueue.AnyAsync(q => q.TaskId == taskId));
        Assert.Equal(due, (await check.Schedules.SingleAsync(s => s.TaskId == taskId)).NextRunTimeUtc!.Value, TimeSpan.FromMilliseconds(1));
    }

    [SkippableFact]
    public async Task StoppedWorkerOfThisHost_IsRevived()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("revive");
        var engine = services.GetRequiredService<SchedulerEngine>();
        await engine.StartAsync();
        try
        {
            await using (var db = database.CreateContext())
            {
                // As if another, older engine had marked this host's workers as stale.
                await db.WorkerThreads.Where(w => w.HostName == "revive")
                    .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkerStatus.Stopped));
            }

            services.GetRequiredService<Scheduler.Application.Common.IEngineSignal>().Wake();
            await Wait.UntilAsync(async () =>
            {
                await using var db = database.CreateContext();
                return !await db.WorkerThreads.AnyAsync(w => w.HostName == "revive" && w.Status == WorkerStatus.Stopped);
            }, TimeSpan.FromSeconds(30), "the workers were revived");

            var taskId = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
            await using (var db = database.CreateContext())
            {
                var task = await db.Tasks.SingleAsync(t => t.Id == taskId);
                db.TaskQueue.Add(QueueEntry.Create(task, null, TriggerType.Manual, DateTime.UtcNow, 0, DateTime.UtcNow));
                await db.SaveChangesAsync();
            }

            services.GetRequiredService<Scheduler.Application.Common.IEngineSignal>().Wake();
            await Wait.UntilAsync(async () =>
            {
                await using var db = database.CreateContext();
                return await db.TaskExecutions.AnyAsync(e => e.TaskId == taskId && e.Status == ExecutionStatus.Success);
            }, TimeSpan.FromSeconds(30), "a run on a revived worker succeeded");
        }
        finally
        {
            await engine.StopAsync();
        }
    }
}

/// <summary>D3: the engine must not busy-loop while work is running or waiting for a free worker.</summary>
public sealed class IdleBehaviourTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    [SkippableFact]
    public async Task Engine_DoesNotSpinWhileTheOnlyWorkerIsBusy()
    {
        RequiresDatabase.Check();
        await database.SetSettingAsync(SettingKeys.MaxConcurrency, 1);
        var services = database.CreateServices("idle");
        var slow = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type, payload: """{ "delayMs": 4000 }""");
        var waiting = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        await using (var db = database.CreateContext())
        {
            var now = DateTime.UtcNow;
            var slowTask = await db.Tasks.SingleAsync(t => t.Id == slow);
            var waitingTask = await db.Tasks.SingleAsync(t => t.Id == waiting);
            db.TaskQueue.Add(QueueEntry.Create(slowTask, null, TriggerType.Manual, now, 0, now));
            db.TaskQueue.Add(QueueEntry.Create(waitingTask, null, TriggerType.Manual, now.AddMilliseconds(1), 0, now));
            await db.SaveChangesAsync();
        }

        var engine = services.GetRequiredService<SchedulerEngine>();
        await engine.StartAsync();
        try
        {
            await Wait.UntilAsync(() => Task.FromResult(engine.RunningCount == 1), TimeSpan.FromSeconds(10), "the slow task started");
            var before = engine.CycleCount;
            await Task.Delay(TimeSpan.FromSeconds(3));
            var cycles = engine.CycleCount - before;
            Assert.True(cycles <= 3, $"The engine ran {cycles} cycles in 3 seconds while its only worker was busy.");
        }
        finally
        {
            await engine.StopAsync();
            await database.SetSettingAsync(SettingKeys.MaxConcurrency, 4);
        }
    }
}
