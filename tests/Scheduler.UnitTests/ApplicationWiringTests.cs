using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application;
using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Tasks;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.UnitTests;

/// <summary>The use cases are reached through their decorators, so validation and authorisation always run.</summary>
public class ApplicationWiringTests
{
    private static ServiceProvider Build(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISchedulerLogger, NullSchedulerLogger>();
        services.AddSingleton<IUnitOfWorkFactory, NoDatabase>();
        configure?.Invoke(services);
        services.AddSchedulerApplication();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Validators_AreRegisteredAndRun()
    {
        using var provider = Build();
        var preview = provider.GetRequiredService<IQueryHandler<PreviewScheduleQuery, IReadOnlyList<PreviewRun>>>();

        var result = await preview.HandleAsync(new PreviewScheduleQuery(new ScheduleInput { Type = ScheduleType.Cron, CronExpression = "*/0 * * * *" }));

        Assert.True(result.IsFailure);
        Assert.NotNull(result.MessageFor(TaskFields.CronExpression));
    }

    [Fact]
    public async Task Preview_ReturnsFutureRunsForAValidSchedule()
    {
        using var provider = Build();
        var preview = provider.GetRequiredService<IQueryHandler<PreviewScheduleQuery, IReadOnlyList<PreviewRun>>>();

        var result = await preview.HandleAsync(new PreviewScheduleQuery(new ScheduleInput { Type = ScheduleType.Interval, IntervalSeconds = 60 }));

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.Count);
    }

    [Fact]
    public async Task AdministratorCommands_AreRefusedForAViewer()
    {
        using var provider = Build(services => services.AddSingleton<ICurrentUser>(new Viewer()));
        var create = provider.GetRequiredService<ICommandHandler<CreateTaskCommand, Guid>>();

        var result = await create.HandleAsync(new CreateTaskCommand(new TaskInput { Name = "x" }));

        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
    }

    [Fact]
    public async Task DeveloperOnlyCommands_AreRefusedForAdministrators()
    {
        using var provider = Build(services => services.AddSingleton<ICurrentUser>(new Administrator()));
        var decorator = new AuthorizationCommandDecorator<DeveloperOnlyCommand, Guid>(
            new DeveloperOnlyHandler(),
            provider.GetRequiredService<ICurrentUser>());

        var result = await decorator.HandleAsync(new DeveloperOnlyCommand());

        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
    }

    [Fact]
    public async Task DeveloperOnlyCommands_AreAllowedForDevelopers()
    {
        using var provider = Build(services => services.AddSingleton<ICurrentUser>(new Developer()));
        var decorator = new AuthorizationCommandDecorator<DeveloperOnlyCommand, Guid>(
            new DeveloperOnlyHandler(),
            provider.GetRequiredService<ICurrentUser>());

        var result = await decorator.HandleAsync(new DeveloperOnlyCommand());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void TwoHandlersForTheSameTaskType_FailWithAClearMessage()
    {
        using var provider = Build(services =>
        {
            services.AddTransient<ITaskHandler, Duplicate>();
            services.AddTransient<ITaskHandler, Duplicate>();
        });
        var registry = provider.GetRequiredService<ITaskTypeRegistry>();

        var error = Assert.Throws<InvalidOperationException>(() => registry.TaskTypes);
        Assert.Contains("Test.Duplicate", error.Message);
    }

    private sealed class Duplicate : ITaskHandler
    {
        public string TaskType => "Test.Duplicate";
        public Task<TaskResult> ExecuteAsync(ITaskContext context) => Task.FromResult(TaskResult.Success());
    }

    private sealed class Viewer : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? Username => "viewer";
        public UserRole? Role => UserRole.Viewer;
        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class Administrator : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? Username => "admin";
        public UserRole? Role => UserRole.Administrator;
        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class Developer : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? Username => "developer";
        public UserRole? Role => UserRole.Developer;
        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed record DeveloperOnlyCommand : IRequireDeveloper;

    private sealed class DeveloperOnlyHandler : ICommandHandler<DeveloperOnlyCommand, Guid>
    {
        public Task<Result<Guid>> HandleAsync(DeveloperOnlyCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(Guid.NewGuid()));
    }

    // The authorisation check must refuse before any validator or handler reaches the database.
    private sealed class NoDatabase : IUnitOfWorkFactory
    {
        public IUnitOfWork Create() => throw new InvalidOperationException("The database must not be used in this test.");
    }

    private sealed class NullSchedulerLogger : ISchedulerLogger
    {
        public Task LogAsync(SchedulerLogLevel level, string source, string message, Guid? executionId = null,
            IReadOnlyDictionary<string, object?>? properties = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
