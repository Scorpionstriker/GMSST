using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Scheduler.Application.Common;
using Scheduler.Extensibility.Events;

namespace Scheduler.Engine.Events;

/// <summary>
/// Delivers scheduler events to subscribers on one background reader, through a bounded channel, so a slow or
/// failing subscriber never holds up the engine (FR-43). When the channel is full the oldest event is dropped.
/// </summary>
public sealed class SchedulerEventHub : ISchedulerEvents, ISchedulerEventPublisher, IDisposable
{
    private readonly Channel<SchedulerEvent> _channel =
        Channel.CreateBounded<SchedulerEvent>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly ILogger<SchedulerEventHub> _logger;
    private readonly Task _reader;

    public SchedulerEventHub(ILogger<SchedulerEventHub> logger)
    {
        _logger = logger;
        _reader = Task.Run(ReadAsync);
    }

    public event EventHandler<TaskLifecycleEventArgs>? TaskQueued;
    public event EventHandler<TaskLifecycleEventArgs>? TaskStarted;
    public event EventHandler<TaskLifecycleEventArgs>? TaskSucceeded;
    public event EventHandler<TaskLifecycleEventArgs>? TaskFailed;
    public event EventHandler<EngineStateEventArgs>? EngineStateChanged;

    public void Publish(SchedulerEvent schedulerEvent) => _channel.Writer.TryWrite(schedulerEvent);

    public void Dispose() => _channel.Writer.TryComplete();

    private async Task ReadAsync()
    {
        await foreach (var item in _channel.Reader.ReadAllAsync())
        {
            try
            {
                Raise(item);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A subscriber of the {Event} event failed.", item.Kind);
            }
        }
    }

    private void Raise(SchedulerEvent item)
    {
        switch (item.Kind, item.Args)
        {
            case (SchedulerEventKind.TaskQueued, TaskLifecycleEventArgs args):
                Invoke(TaskQueued, args);
                break;
            case (SchedulerEventKind.TaskStarted, TaskLifecycleEventArgs args):
                Invoke(TaskStarted, args);
                break;
            case (SchedulerEventKind.TaskSucceeded, TaskLifecycleEventArgs args):
                Invoke(TaskSucceeded, args);
                break;
            case (SchedulerEventKind.TaskFailed, TaskLifecycleEventArgs args):
                Invoke(TaskFailed, args);
                break;
            case (SchedulerEventKind.EngineStateChanged, EngineStateEventArgs args):
                Invoke(EngineStateChanged, args);
                break;
        }
    }

    // Each subscriber is called separately, so one failing subscriber does not stop the others.
    private void Invoke<T>(EventHandler<T>? handlers, T args)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<T>>())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Event subscriber {Subscriber} failed.", handler.Method.DeclaringType?.FullName);
            }
        }
    }
}
