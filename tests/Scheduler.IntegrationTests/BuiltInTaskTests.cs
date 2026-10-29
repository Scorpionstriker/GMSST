using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Common;
using Scheduler.Application.Tasks;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.IntegrationTests;

/// <summary>WP-8h: built-in tasks are found by a stable key, can be renamed and cannot be deleted.</summary>
public sealed class BuiltInTaskTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    [SkippableFact]
    public async Task KeyedTask_SurvivesARename_RunsByKey_AndCannotBeDeleted()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("builtin");
        var client = services.GetRequiredService<ISchedulerClient>();
        var definition = new TaskDefinition { Name = "Keyed task", SystemKey = "test.keyed", TaskType = CountingHandler.Type, CronExpression = "0 3 * * *" };
        var id = await client.EnsureTaskAsync(definition);

        await using (var db = database.CreateContext())
        {
            await db.Tasks.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Name, "Renamed by the administrator"));
        }

        Assert.Equal(id, await client.EnsureTaskAsync(definition));
        Assert.True(await client.RunNowAsync("test.keyed"));

        var delete = await services.GetRequiredService<ICommandHandler<DeleteTaskCommand, Unit>>().HandleAsync(new DeleteTaskCommand(id));
        Assert.False(delete.IsSuccess);
        Assert.Equal("Task.BuiltIn", delete.Error!.Code);

        await using var check = database.CreateContext();
        var task = await check.Tasks.SingleAsync(t => t.Id == id);
        Assert.False(task.IsDeleted);
        Assert.Single(await check.Tasks.Where(t => t.SystemKey == "test.keyed").ToListAsync());
    }

    [SkippableFact]
    public async Task OlderTaskWithTheSameNameAndType_IsTakenOverAndRestored()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("builtin-adopt");
        var client = services.GetRequiredService<ISchedulerClient>();
        var older = await client.EnsureTaskAsync(new TaskDefinition { Name = "Old built-in", TaskType = CountingHandler.Type, CronExpression = "0 3 * * *" });
        await using (var db = database.CreateContext())
        {
            await db.Tasks.Where(t => t.Id == older).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDeleted, true));
        }

        var adopted = await client.EnsureTaskAsync(new TaskDefinition
        {
            Name = "Old built-in", SystemKey = "test.adopted", TaskType = CountingHandler.Type, CronExpression = "0 3 * * *"
        });

        Assert.Equal(older, adopted);
        await using var check = database.CreateContext();
        var task = await check.Tasks.SingleAsync(t => t.Id == older);
        Assert.Equal("test.adopted", task.SystemKey);
        Assert.False(task.IsDeleted);
        Assert.Equal(ScheduledTaskStatus.Active, task.Status);
    }
}
