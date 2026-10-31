using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Common;
using Scheduler.Application.Configuration;
using Scheduler.Application.Tasks;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.UnitTests.InMemory;

/// <summary>Task and configuration use cases on the in-memory database, through their real validators and decorators.</summary>
public class InMemoryUseCaseTests
{
    [Fact]
    public async Task CreateTask_ReportsEveryInvalidField()
    {
        await using var host = new InMemoryHost();

        var result = await host.Get<ICommandHandler<CreateTaskCommand, Guid>>().HandleAsync(new CreateTaskCommand(new TaskInput
        {
            Name = "",
            TaskType = "Unknown",
            Payload = "{ not json",
            Priority = 11,
            Schedule = new ScheduleInput { Type = ScheduleType.Cron, CronExpression = "*/0 * * * *" }
        }));

        var fields = result.Errors.Select(e => e.Field).ToHashSet();
        Assert.Contains(TaskFields.Name, fields);
        Assert.Contains(TaskFields.TaskType, fields);
        Assert.Contains(TaskFields.Payload, fields);
        Assert.Contains(TaskFields.Priority, fields);
        Assert.Contains(TaskFields.CronExpression, fields);
    }

    [Fact]
    public async Task CreateTask_InALocalTimeZone_StoresUtcAndTheNextRun()
    {
        await using var host = new InMemoryHost();

        var result = await host.Get<ICommandHandler<CreateTaskCommand, Guid>>().HandleAsync(new CreateTaskCommand(new TaskInput
        {
            Name = "Morning job",
            TaskType = CountingHandler.Type,
            Schedule = new ScheduleInput { Type = ScheduleType.Cron, CronExpression = "0 6 * * *", TimeZoneId = "Africa/Johannesburg" }
        }));

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.Message)));
        var schedule = host.Database.Tasks.Single(t => t.Id == result.Value).Schedules.Single(s => !s.IsDeleted);
        Assert.Equal(4, schedule.NextRunTimeUtc!.Value.Hour); // 06:00 SAST is 04:00 UTC.
    }

    [Fact]
    public async Task TaskNames_AreUniqueIgnoringCase()
    {
        await using var host = new InMemoryHost();
        await host.CreateManualTaskAsync(CountingHandler.Type, name: "Nightly report");

        var result = await host.Get<ICommandHandler<CreateTaskCommand, Guid>>().HandleAsync(new CreateTaskCommand(new TaskInput
        {
            Name = "NIGHTLY REPORT",
            TaskType = CountingHandler.Type,
            Schedule = new ScheduleInput { Type = ScheduleType.Interval, IntervalSeconds = 60 }
        }));

        Assert.Contains(result.Errors, e => e.Field == TaskFields.Name);
    }

    [Fact]
    public async Task UpdateTask_WithAnOldVersion_IsAConflict()
    {
        await using var host = new InMemoryHost();
        var id = await host.CreateManualTaskAsync(CountingHandler.Type);
        var details = (await host.Get<IQueryHandler<GetTaskQuery, TaskDetails>>().HandleAsync(new GetTaskQuery(id))).Value!;
        var update = host.Get<ICommandHandler<UpdateTaskCommand, Unit>>();

        var first = await update.HandleAsync(new UpdateTaskCommand(id, details.Version, details.Input with { Description = "first" }));
        var second = await update.HandleAsync(new UpdateTaskCommand(id, details.Version, details.Input with { Description = "second" }));

        Assert.True(first.IsSuccess, string.Join("; ", first.Errors.Select(e => e.Message)));
        Assert.Equal(ErrorKind.Conflict, second.Error!.Kind);
    }

    [Fact]
    public async Task Dependencies_ThatWouldFormACycle_AreRejected()
    {
        await using var host = new InMemoryHost();
        var a = await host.CreateManualTaskAsync(CountingHandler.Type);
        var b = await host.CreateManualTaskAsync(CountingHandler.Type);
        var get = host.Get<IQueryHandler<GetTaskQuery, TaskDetails>>();
        var update = host.Get<ICommandHandler<UpdateTaskCommand, Unit>>();

        var taskB = (await get.HandleAsync(new GetTaskQuery(b))).Value!;
        Assert.True((await update.HandleAsync(new UpdateTaskCommand(b, taskB.Version, taskB.Input with { DependsOn = [a] }))).IsSuccess);

        var taskA = (await get.HandleAsync(new GetTaskQuery(a))).Value!;
        var result = await update.HandleAsync(new UpdateTaskCommand(a, taskA.Version, taskA.Input with { DependsOn = [b] }));
        Assert.Contains(result.Errors, e => e.Field == TaskFields.DependsOn);
    }

    [Fact]
    public async Task Settings_AreValidatedBeforeTheyAreStored()
    {
        await using var host = new InMemoryHost();
        var settings = host.Get<ISettingsProvider>();
        var update = host.Get<ICommandHandler<UpdateSettingCommand, Unit>>();
        Assert.Equal(90, await settings.GetAsync(SettingKeys.LogRetentionDays));

        Assert.True((await update.HandleAsync(new UpdateSettingCommand(SettingKeys.LogRetentionDays, "30"))).IsSuccess);
        Assert.Equal(30, await settings.GetAsync(SettingKeys.LogRetentionDays));
        Assert.True((await update.HandleAsync(new UpdateSettingCommand(SettingKeys.LogRetentionDays, "0"))).IsFailure);
        Assert.Equal(30, await settings.GetAsync(SettingKeys.LogRetentionDays));
    }

    [Fact]
    public async Task ViewerRole_CannotChangeData()
    {
        await using var host = new InMemoryHost(s => s.AddSingleton<ICurrentUser>(new ViewerUser()));
        var id = await host.CreateManualTaskAsync(CountingHandler.Type);

        var result = await host.Get<ICommandHandler<RunTaskNowCommand, Guid>>().HandleAsync(new RunTaskNowCommand(id));

        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
    }

    [Fact]
    public async Task BuiltInTask_SurvivesARename_RunsByKey_AndCannotBeDeleted()
    {
        await using var host = new InMemoryHost();
        var client = host.Get<ISchedulerClient>();
        var definition = new TaskDefinition
        {
            Name = "Keyed task",
            SystemKey = "test.keyed",
            TaskType = CountingHandler.Type,
            ScheduleType = ScheduleType.Interval,
            IntervalSeconds = 3600
        };
        var id = await client.EnsureTaskAsync(definition);
        var details = (await host.Get<IQueryHandler<GetTaskQuery, TaskDetails>>().HandleAsync(new GetTaskQuery(id))).Value!;
        Assert.True((await host.Get<ICommandHandler<UpdateTaskCommand, Unit>>()
            .HandleAsync(new UpdateTaskCommand(id, details.Version, details.Input with { Name = "Renamed task" }))).IsSuccess);

        Assert.Equal(id, await client.EnsureTaskAsync(definition));
        Assert.Equal("Renamed task", host.NameOf(id));
        Assert.True(await client.RunNowAsync("test.keyed"));
        var delete = await host.Get<ICommandHandler<DeleteTaskCommand, Unit>>().HandleAsync(new DeleteTaskCommand(id));
        Assert.True(delete.IsFailure);
    }
}
