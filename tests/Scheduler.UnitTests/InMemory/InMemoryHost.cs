using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application;
using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Configuration;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Engine;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.UnitTests.InMemory;

/// <summary>
/// The scheduler composed as the Admin application composes it, but on the in-memory database: use cases with their
/// decorators, the engine and the test task types. Nothing here needs PostgreSQL.
/// </summary>
public sealed class InMemoryHost : IAsyncDisposable
{
    public InMemoryHost(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Database);
        services.AddSingleton<IUnitOfWorkFactory>(Database);
        services.AddSingleton(Logger);
        services.AddSingleton<ISchedulerLogger>(Logger);
        services.AddSingleton(Users);
        services.AddSingleton<IUserAccountStore>(Users);
        services.AddSingleton(Notifications);
        services.AddSingleton<ISchedulerNotifications>(Notifications);
        services.AddSingleton<InMemorySettingsProvider>();
        services.AddSingleton<ISettingsProvider>(sp => sp.GetRequiredService<InMemorySettingsProvider>());
        services.AddSingleton<ISettingsReader>(sp => sp.GetRequiredService<InMemorySettingsProvider>());
        services.AddSchedulerApplication();
        services.AddSchedulerEngine(new EngineOptions { HostName = "in-memory", CreateBuiltInTasks = false, RegisterBuiltInHandlers = false });
        services.AddTransient<ITaskHandler, CountingHandler>();
        services.AddTransient<ITaskHandler, FailingHandler>();
        services.AddTransient<ITaskHandler, ThrowingHandler>();
        services.AddTransient<ITaskHandler, HangingHandler>();
        configure?.Invoke(services);
        Services = services.BuildServiceProvider();
    }

    public InMemoryDatabase Database { get; } = new();
    public InMemorySchedulerLogger Logger { get; } = new();
    public InMemoryUserAccountStore Users { get; } = new();
    public InMemoryNotifications Notifications { get; } = new();
    public ServiceProvider Services { get; }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>Stores a global setting, as an administrator would on the Configuration page.</summary>
    public void SetSetting(string key, int value) => Database.Locked(() =>
    {
        Database.Settings.RemoveAll(s => s.TaskId == null && s.Key == key);
        Database.Settings.Add(ConfigurationSetting.Global(key, value.ToString(), DateTime.UtcNow));
        return 0;
    });

    /// <summary>A task whose schedule is far in the future, so it only runs when it is run manually.</summary>
    public Task<Guid> CreateManualTaskAsync(string taskType, string? name = null, int? maxRetries = 0, string payload = "{}",
        int timeoutSeconds = 30) =>
        Get<ISchedulerClient>().EnsureTaskAsync(new TaskDefinition
        {
            Name = name ?? $"{taskType} {Guid.NewGuid():N}",
            TaskType = taskType,
            Payload = payload,
            MaxRetries = maxRetries,
            TimeoutSeconds = timeoutSeconds,
            ScheduleType = ScheduleType.Cron,
            CronExpression = "0 0 1 1 *"
        });

    public ScheduledTask TaskOf(Guid taskId) => Database.Locked(() => Database.Tasks.Single(t => t.Id == taskId));

    /// <summary>Adds a queue entry directly, as if it had been queued before the engine started.</summary>
    public async Task<QueueEntry> EnqueueAsync(Guid taskId, DateTime scheduledUtc, TriggerType trigger = TriggerType.Manual, DateTime? enqueuedUtc = null)
    {
        var entry = QueueEntry.Create(TaskOf(taskId), null, trigger, scheduledUtc, 0, enqueuedUtc ?? scheduledUtc);
        await using var unitOfWork = Database.Create();
        unitOfWork.Queue.Add(entry);
        await unitOfWork.SaveChangesAsync();
        return entry;
    }

    /// <summary>Wakes the engine for each of two cycles and waits for them, so a test can check that nothing happened.</summary>
    public async Task IdleCyclesAsync()
    {
        var engine = Get<SchedulerEngine>();
        for (var i = 0; i < 2; i++)
        {
            var cycles = engine.CycleCount;
            Get<IEngineSignal>().Wake();
            await WaitUntilAsync(() => engine.CycleCount > cycles, "a cycle ran");
        }

        await WaitUntilAsync(() => engine.RunningCount == 0, "no run is in progress");
    }

    public string NameOf(Guid taskId) => Database.Locked(() => Database.Tasks.Single(t => t.Id == taskId).Name);

    public List<ExecutionStatus> Statuses(Guid taskId) => Database.Locked(() =>
        Database.Executions.Where(e => e.TaskId == taskId).OrderBy(e => e.StartTimeUtc).Select(e => e.Status).ToList());

    /// <summary>Runs <paramref name="body"/> while the engine runs, then stops the engine.</summary>
    public async Task RunEngineAsync(Func<Task> body)
    {
        var engine = Get<SchedulerEngine>();
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

    /// <summary>Polls <paramref name="condition"/> until it is true or ten seconds pass.</summary>
    public static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        await WaitUntilAsync(condition, because, TimeSpan.FromSeconds(10));
    }

    public static async Task WaitUntilAsync(Func<bool> condition, string because, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"Timed out waiting until {because}.");
    }

    public ValueTask DisposeAsync() => Services.DisposeAsync();
}

public sealed class InMemoryNotifications : ISchedulerNotifications
{
    private readonly object _gate = new();
    private readonly List<(Guid UserId, NotificationItem Item, DateTime? CancelledAtUtc)> _items = [];

    public IReadOnlyList<(Guid UserId, NotificationItem Item)> Items
    {
        get
        {
            lock (_gate)
            {
                return _items.Where(item => item.CancelledAtUtc is null).Select(item => (item.UserId, item.Item)).ToList();
            }
        }
    }

    public Task<Guid> ScheduleAsync(string source, Guid recipientUserId, string title, string message, DateTime? deliverAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var deliver = deliverAtUtc ?? now;
        var item = new NotificationItem(Guid.NewGuid(), source, title, message, now, deliver, null);
        lock (_gate)
        {
            _items.Add((recipientUserId, item, null));
        }

        return Task.FromResult(item.Id);
    }

    public Task<IReadOnlyList<NotificationItem>> GetScheduledAsync(string source, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<NotificationItem>>(_items
                .Where(item => item.Item.Source == source && item.CancelledAtUtc is null && item.Item.DeliverAtUtc > DateTime.UtcNow)
                .Select(item => item.Item).ToList());
        }
    }

    public Task<bool> CancelScheduledAsync(string source, Guid notificationId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var index = _items.FindIndex(item => item.Item.Source == source && item.Item.Id == notificationId
                && item.CancelledAtUtc is null && item.Item.DeliverAtUtc > DateTime.UtcNow);
            if (index < 0)
            {
                return Task.FromResult(false);
            }

            var existing = _items[index];
            _items[index] = (existing.UserId, existing.Item, DateTime.UtcNow);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<NotificationItem>> GetInboxAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<NotificationItem>>([]);

    public Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

/// <summary>Records every run by execution id.</summary>
public sealed class CountingHandler : ITaskHandler
{
    public const string Type = "Test.Count";
    public static readonly ConcurrentDictionary<Guid, Guid> Runs = new();
    public static readonly ConcurrentDictionary<Guid, (string? Name, string? Source, string? Data, string Payload)> Events = new();

    public string TaskType => Type;

    public async Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        Runs[context.ExecutionId] = context.TaskId;
        if (context.EventName is not null)
        {
            Events[context.ExecutionId] = (context.EventName, context.EventSource, context.EventData?.GetRawText(), context.Payload.GetRawText());
        }

        if (context.GetInt32("delayMs") is { } delay)
        {
            await Task.Delay(delay, context.CancellationToken);
        }

        return TaskResult.Success("counted", new { context.RetryNumber });
    }
}

/// <summary>Ignores its cancellation token, as a badly written handler would.</summary>
public sealed class HangingHandler : ITaskHandler
{
    public const string Type = "Test.Hang";
    public string TaskType => Type;

    public async Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None);
        return TaskResult.Success();
    }
}

public sealed class FailingHandler : ITaskHandler
{
    public const string Type = "Test.Fail";
    public string TaskType => Type;
    public Task<TaskResult> ExecuteAsync(ITaskContext context) => Task.FromResult(TaskResult.Failure("Expected failure."));
}

public sealed class ThrowingHandler : ITaskHandler
{
    public const string Type = "Test.Throw";
    public string TaskType => Type;
    public Task<TaskResult> ExecuteAsync(ITaskContext context) => throw new InvalidOperationException("Boom from the handler.");
}

/// <summary>A signed-in Viewer, for the role checks (FR-49).</summary>
public sealed class ViewerUser : ICurrentUser
{
    public bool IsAuthenticated => true;
    public Guid? UserId => null;
    public string? Username => "viewer";
    public UserRole? Role => UserRole.Viewer;
    public event EventHandler? Changed { add { } remove { } }
}
