using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Configuration;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;
using Scheduler.Extensibility.Events;

namespace Scheduler.UnitTests.InMemory;

/// <summary>The scheduling engine end to end on the in-memory database: run now, retries, errors, dependencies and claim order.</summary>
public class InMemoryEngineTests
{
    [Fact]
    public async Task RunNow_ExecutesTheTaskOnce_AndRecordsSuccess()
    {
        await using var host = new InMemoryHost();
        var taskId = await host.CreateManualTaskAsync(CountingHandler.Type);
        var succeeded = 0;
        host.Get<ISchedulerEvents>().TaskSucceeded += (_, _) => Interlocked.Increment(ref succeeded);

        await host.RunEngineAsync(async () =>
        {
            Assert.True(await host.Get<ISchedulerClient>().RunNowAsync(host.NameOf(taskId)));
            await InMemoryHost.WaitUntilAsync(() => host.Statuses(taskId) is [ExecutionStatus.Success], "the run succeeded");
        });

        var execution = host.Database.Executions.Single(e => e.TaskId == taskId);
        Assert.Equal(TriggerType.Manual, execution.TriggerType);
        Assert.Equal(QueueStatus.Processed, host.Database.Queue.Single(q => q.TaskId == taskId).Status);
        Assert.Equal(1, succeeded);
    }

    [Fact]
    public async Task FailedRun_IsRetried_AndFinallyMarkedFailed()
    {
        await using var host = new InMemoryHost();
        host.SetSetting(SettingKeys.RetryBaseDelaySeconds, 1);
        var taskId = await host.CreateManualTaskAsync(FailingHandler.Type, maxRetries: 2);

        await host.RunEngineAsync(async () =>
        {
            await host.Get<ISchedulerClient>().RunNowAsync(host.NameOf(taskId));
            await InMemoryHost.WaitUntilAsync(() => host.Statuses(taskId).Count == 3, "three attempts were made");
        });

        Assert.Equal([ExecutionStatus.Retrying, ExecutionStatus.Retrying, ExecutionStatus.Failed], host.Statuses(taskId));
        var attempts = host.Database.Queue.Where(q => q.TaskId == taskId).OrderBy(q => q.Attempt).ToList();
        Assert.Equal([0, 1, 2], attempts.Select(q => q.Attempt));
        Assert.All(attempts.Skip(1), q => Assert.Equal(TriggerType.Retry, q.TriggerType));
        Assert.Equal(3, host.Database.Errors.Count);
    }

    [Fact]
    public async Task ThrowingHandler_IsRecordedWithItsStackTrace()
    {
        await using var host = new InMemoryHost();
        var taskId = await host.CreateManualTaskAsync(ThrowingHandler.Type);

        await host.RunEngineAsync(async () =>
        {
            await host.Get<ISchedulerClient>().RunNowAsync(host.NameOf(taskId));
            await InMemoryHost.WaitUntilAsync(() => host.Statuses(taskId) is [ExecutionStatus.Failed], "the run failed");
        });

        var error = Assert.Single(host.Database.Errors);
        Assert.Equal(nameof(InvalidOperationException), error.ErrorType);
        Assert.Contains("ThrowingHandler", error.StackTrace);
    }

    [Fact]
    public async Task SuccessfulTask_QueuesItsDependant()
    {
        await using var host = new InMemoryHost();
        var prerequisite = await host.CreateManualTaskAsync(CountingHandler.Type);
        var dependant = await host.CreateManualTaskAsync(CountingHandler.Type);
        host.Database.Tasks.Single(t => t.Id == dependant).SetDependencies([prerequisite], DateTime.UtcNow);

        await host.RunEngineAsync(async () =>
        {
            await host.Get<ISchedulerClient>().RunNowAsync(host.NameOf(prerequisite));
            await InMemoryHost.WaitUntilAsync(() => host.Statuses(dependant) is [ExecutionStatus.Success], "the dependant ran");
        });

        Assert.Equal(TriggerType.Dependency, host.Database.Executions.Single(e => e.TaskId == dependant).TriggerType);
    }

    [Fact]
    public async Task Claim_TakesTheHighestPriorityFirst_ThenTheEntryQueuedFirst()
    {
        await using var host = new InMemoryHost();
        var low = await host.CreateManualTaskAsync(CountingHandler.Type);
        var high = await host.CreateManualTaskAsync(CountingHandler.Type);
        var now = DateTime.UtcNow;
        var lowTask = host.Database.Tasks.Single(t => t.Id == low);
        var highTask = host.Database.Tasks.Single(t => t.Id == high);
        InMemoryDatabase.SetProperty(highTask, nameof(ScheduledTask.Priority), 1);
        await using (var uow = host.Database.Create())
        {
            uow.Queue.Add(QueueEntry.Create(lowTask, null, TriggerType.Manual, now.AddSeconds(-2), 0, now.AddSeconds(-2)));
            uow.Queue.Add(QueueEntry.Create(highTask, null, TriggerType.Manual, now.AddSeconds(-1), 0, now.AddSeconds(-1)));
            await uow.SaveChangesAsync();
        }

        await using var claim = host.Database.Create();
        var first = await claim.Queue.ClaimNextAsync(Guid.NewGuid(), [CountingHandler.Type], now, 60);
        var second = await claim.Queue.ClaimNextAsync(Guid.NewGuid(), [CountingHandler.Type], now, 60);
        var third = await claim.Queue.ClaimNextAsync(Guid.NewGuid(), [CountingHandler.Type], now, 60);

        Assert.Equal(high, host.Database.Queue.Single(q => q.Id == first).TaskId);
        Assert.Equal(low, host.Database.Queue.Single(q => q.Id == second).TaskId);
        Assert.Null(third);
    }

    [Fact]
    public async Task ParallelClaims_TakeEveryEntryExactlyOnce()
    {
        await using var host = new InMemoryHost();
        var taskId = await host.CreateManualTaskAsync(CountingHandler.Type);
        var task = host.Database.Tasks.Single(t => t.Id == taskId);
        var now = DateTime.UtcNow;
        await using (var uow = host.Database.Create())
        {
            for (var i = 0; i < 50; i++)
            {
                uow.Queue.Add(QueueEntry.Create(task, null, TriggerType.Manual, now.AddSeconds(-1), 0, now));
            }

            await uow.SaveChangesAsync();
        }

        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            var mine = new List<Guid>();
            await using var uow = host.Database.Create();
            while (await uow.Queue.ClaimNextAsync(Guid.NewGuid(), [CountingHandler.Type], now, 60) is { } id)
            {
                mine.Add(id);
            }

            return mine;
        })));

        var all = claims.SelectMany(c => c).ToList();
        Assert.Equal(50, all.Count);
        Assert.Equal(50, all.Distinct().Count());
    }
}
