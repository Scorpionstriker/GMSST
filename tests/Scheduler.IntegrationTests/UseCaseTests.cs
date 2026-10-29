using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Common;
using Scheduler.Application.Configuration;
using Scheduler.Application.Tasks;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.IntegrationTests;

/// <summary>Use cases resolved from the container exactly as the Admin application resolves them, so validators and decorators are really wired (D6).</summary>
public sealed class UseCaseTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    [SkippableFact]
    public async Task CreateTask_ReportsEveryInvalidFieldThroughTheRegisteredValidator()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("use-cases");
        var handler = services.GetRequiredService<ICommandHandler<CreateTaskCommand, Guid>>();

        var result = await handler.HandleAsync(new CreateTaskCommand(new TaskInput
        {
            Name = "",
            TaskType = "Unknown",
            Payload = "{ not json",
            Priority = 11,
            Schedule = new ScheduleInput { Type = ScheduleType.Cron, CronExpression = "*/0 * * * *" }
        }));

        Assert.True(result.IsFailure);
        var fields = result.Errors.Select(e => e.Field).ToHashSet();
        Assert.Contains(TaskFields.Name, fields);
        Assert.Contains(TaskFields.TaskType, fields);
        Assert.Contains(TaskFields.Payload, fields);
        Assert.Contains(TaskFields.Priority, fields);
        Assert.Contains(TaskFields.CronExpression, fields);
    }

    [SkippableFact]
    public async Task CreateTask_InALocalTimeZone_StoresUtcAndCalculatesTheNextRun()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("use-cases-tz");
        var handler = services.GetRequiredService<ICommandHandler<CreateTaskCommand, Guid>>();

        var result = await handler.HandleAsync(new CreateTaskCommand(new TaskInput
        {
            Name = "Morning job " + Guid.NewGuid().ToString("N"),
            TaskType = CountingHandler.Type,
            Schedule = new ScheduleInput { Type = ScheduleType.Cron, CronExpression = "0 6 * * *", TimeZoneId = "Africa/Johannesburg" }
        }));

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.Message)));
        await using var db = database.CreateContext();
        var schedule = await db.Schedules.SingleAsync(s => s.TaskId == result.Value);
        Assert.Equal(4, schedule.NextRunTimeUtc!.Value.Hour); // 06:00 SAST is 04:00 UTC (D1).
    }

    [SkippableFact]
    public async Task UpdateTask_WithAnOldVersion_IsAConflict()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("use-cases-version");
        var id = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        var details = (await services.GetRequiredService<IQueryHandler<GetTaskQuery, TaskDetails>>().HandleAsync(new GetTaskQuery(id))).Value!;
        var update = services.GetRequiredService<ICommandHandler<UpdateTaskCommand, Unit>>();

        var first = await update.HandleAsync(new UpdateTaskCommand(id, details.Version, details.Input with { Description = "first" }));
        var second = await update.HandleAsync(new UpdateTaskCommand(id, details.Version, details.Input with { Description = "second" }));

        Assert.True(first.IsSuccess, string.Join("; ", first.Errors.Select(e => e.Message)));
        Assert.Equal(ErrorKind.Conflict, second.Error!.Kind);
    }

    [SkippableFact]
    public async Task Dependencies_ThatWouldFormACycle_AreRejected()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("use-cases-cycle");
        var a = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        var b = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        var get = services.GetRequiredService<IQueryHandler<GetTaskQuery, TaskDetails>>();
        var update = services.GetRequiredService<ICommandHandler<UpdateTaskCommand, Unit>>();

        var taskB = (await get.HandleAsync(new GetTaskQuery(b))).Value!;
        Assert.True((await update.HandleAsync(new UpdateTaskCommand(b, taskB.Version, taskB.Input with { DependsOn = [a] }))).IsSuccess);

        var taskA = (await get.HandleAsync(new GetTaskQuery(a))).Value!;
        var result = await update.HandleAsync(new UpdateTaskCommand(a, taskA.Version, taskA.Input with { DependsOn = [b] }));
        Assert.Contains(result.Errors, e => e.Field == TaskFields.DependsOn);
    }

    [SkippableFact]
    public async Task Settings_AreValidatedAndTheCacheIsRefreshed()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("use-cases-settings");
        var settings = services.GetRequiredService<ISettingsProvider>();
        var update = services.GetRequiredService<ICommandHandler<UpdateSettingCommand, Unit>>();
        Assert.Equal(90, await settings.GetAsync(SettingKeys.LogRetentionDays));

        Assert.True((await update.HandleAsync(new UpdateSettingCommand(SettingKeys.LogRetentionDays, "30", null))).IsSuccess);
        Assert.Equal(30, await settings.GetAsync(SettingKeys.LogRetentionDays));

        var invalid = await update.HandleAsync(new UpdateSettingCommand(SettingKeys.LogRetentionDays, "0", null));
        Assert.True(invalid.IsFailure);
        Assert.True((await update.HandleAsync(new UpdateSettingCommand(SettingKeys.LogRetentionDays, "90", null))).IsSuccess);
    }

    [SkippableFact]
    public async Task ViewerRole_CannotChangeData()
    {
        RequiresDatabase.Check();
        var services = database.CreateServices("use-cases-viewer", configure: s => s.AddSingleton<ICurrentUser>(new ViewerUser()));
        var id = await TaskSetup.CreateManualTaskAsync(services, CountingHandler.Type);
        var result = await services.GetRequiredService<ICommandHandler<RunTaskNowCommand, Guid>>().HandleAsync(new RunTaskNowCommand(id));
        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
    }

    private sealed class ViewerUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => null;
        public string? Username => "viewer";
        public UserRole? Role => UserRole.Viewer;
        public event EventHandler? Changed { add { } remove { } }
    }
}
