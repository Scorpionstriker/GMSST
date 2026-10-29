using Scheduler.Application.Common;

namespace Scheduler.Engine;

/// <summary>
/// Wakes the engine immediately when a task is created, changed or run manually, or when a worker becomes free
/// (FR-18). Several wake-ups before the engine looks collapse into one.
/// </summary>
public sealed class EngineSignal : IEngineSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Wake()
    {
        try
        {
            if (_signal.CurrentCount == 0)
            {
                _signal.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // Already signalled.
        }
    }

    /// <summary>Waits for a wake-up or the timeout. Returns true when woken.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _signal.WaitAsync(timeout, cancellationToken);
}
