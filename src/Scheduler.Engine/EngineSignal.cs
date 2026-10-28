namespace Scheduler.Engine;

/// <summary>
/// Wakes the engine immediately when a task is created, changed or run manually (FR-18).
/// </summary>
public sealed class EngineSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Wake()
    {
        if (_signal.CurrentCount == 0)
        {
            try { _signal.Release(); }
            catch (SemaphoreFullException) { /* already signalled */ }
        }
    }

    /// <summary>Waits for a wake-up or the timeout. Returns true when woken.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _signal.WaitAsync(timeout, cancellationToken);
}
