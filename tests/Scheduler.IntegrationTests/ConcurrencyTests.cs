using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Configuration;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Engine;

namespace Scheduler.IntegrationTests;

/// <summary>NFR-03: concurrent workers, also in different processes, never claim or run the same entry twice.</summary>
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
    public async Task TwoEngines_RunEachQueuedEntryOnce()
    {
        RequiresDatabase.Check();
        var first = database.CreateServices("host-a");
        var second = database.CreateServices("host-b");
        var taskId = await TaskSetup.CreateManualTaskAsync(first, CountingHandler.Type, payload: """{ "delayMs": 50 }""");
        await using (var db = database.CreateContext())
        {
            var task = await db.Tasks.SingleAsync(t => t.Id == taskId);
            var now = DateTime.UtcNow;
            for (var i = 0; i < 40; i++)
            {
                db.TaskQueue.Add(QueueEntry.Create(task, null, TriggerType.Manual, now, 0, now));
            }

            await db.SaveChangesAsync();
        }

        var a = first.GetRequiredService<SchedulerEngine>();
        var b = second.GetRequiredService<SchedulerEngine>();
        await Task.WhenAll(a.StartAsync(), b.StartAsync());
        try
        {
            await Wait.UntilAsync(async () =>
            {
                await using var db = database.CreateContext();
                return await db.TaskExecutions.CountAsync(e => e.TaskId == taskId && e.Status == ExecutionStatus.Success) == 40;
            }, TimeSpan.FromSeconds(60), "all 40 entries ran");
        }
        finally
        {
            await Task.WhenAll(a.StopAsync(), b.StopAsync());
        }

        await using var check = database.CreateContext();
        var executions = await check.TaskExecutions.Where(e => e.TaskId == taskId).ToListAsync();
        Assert.Equal(40, executions.Count);
        Assert.Equal(40, executions.Select(e => e.QueueId).Distinct().Count());
        var hosts = await check.WorkerThreads.Where(w => executions.Select(e => e.ThreadId).Contains(w.Id)).Select(w => w.HostName).Distinct().ToListAsync();
        Assert.Equal(2, hosts.Count);
    }

    [SkippableFact]
    public async Task TwoEngines_QueueEachScheduledRunOnce()
    {
        RequiresDatabase.Check();
        var first = database.CreateServices("sched-a");
        var second = database.CreateServices("sched-b");
        var taskId = await TaskSetup.CreateManualTaskAsync(first, CountingHandler.Type);
        await using (var db = database.CreateContext())
        {
            await db.Schedules.Where(s => s.TaskId == taskId).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextRunTimeUtc, DateTime.UtcNow));
        }

        var a = first.GetRequiredService<SchedulerEngine>();
        var b = second.GetRequiredService<SchedulerEngine>();
        await Task.WhenAll(a.StartAsync(), b.StartAsync());
        try
        {
            await Wait.UntilAsync(async () =>
            {
                await using var db = database.CreateContext();
                return await db.TaskExecutions.AnyAsync(e => e.TaskId == taskId && e.Status == ExecutionStatus.Success);
            }, TimeSpan.FromSeconds(30), "the scheduled run completed");
            await Task.Delay(1500);
        }
        finally
        {
            await Task.WhenAll(a.StopAsync(), b.StopAsync());
        }

        await using var check = database.CreateContext();
        Assert.Equal(1, await check.TaskQueue.CountAsync(q => q.TaskId == taskId));
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
