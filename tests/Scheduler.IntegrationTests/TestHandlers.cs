using System.Collections.Concurrent;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.IntegrationTests;

/// <summary>Records every run by execution id; optional payload "delayMs" makes it run longer.</summary>
public sealed class CountingHandler : ITaskHandler
{
    public const string Type = "Test.Count";
    public static readonly ConcurrentDictionary<Guid, Guid> Runs = new();

    public string TaskType => Type;

    public async Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        Runs[context.ExecutionId] = context.TaskId;
        if (context.GetInt32("delayMs") is { } delay)
        {
            await Task.Delay(delay, context.CancellationToken);
        }

        return TaskResult.Success("counted", new { context.RetryNumber });
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
