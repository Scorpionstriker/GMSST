using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Scheduler.Application.Common;
using Scheduler.Domain.Enums;

namespace Scheduler.Engine.Execution;

/// <summary>
/// The logger given to a handler: its entries are written through the scheduler's logging providers with the
/// execution's id, so they appear in the execution details (FR-28, FR-58). Debug and Trace entries are ignored.
/// </summary>
internal sealed class ExecutionLogger(ISchedulerLogger log, Guid executionId, string taskName) : ILogger
{
    public const string Source = "TaskHandler";
    private readonly ConcurrentBag<Task> _pending = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var level = logLevel switch
        {
            LogLevel.Information => SchedulerLogLevel.Info,
            LogLevel.Warning => SchedulerLogLevel.Warning,
            _ => SchedulerLogLevel.Error
        };

        var properties = new Dictionary<string, object?> { ["task"] = taskName };
        if (state is IEnumerable<KeyValuePair<string, object?>> values)
        {
            foreach (var (key, value) in values.Where(v => v.Key != "{OriginalFormat}"))
            {
                properties[key] = value;
            }
        }

        if (exception is not null)
        {
            properties["exception"] = exception.GetType().FullName + ": " + exception.Message;
        }

        _pending.Add(log.LogAsync(level, Source, formatter(state, exception), executionId, properties));
    }

    /// <summary>Waits until every entry has been written, before the execution is completed.</summary>
    public Task FlushAsync() => Task.WhenAll(_pending);
}
