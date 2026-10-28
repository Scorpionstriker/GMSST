namespace Scheduler.Engine;

/// <summary>
/// Runs one claimed queue entry: resolves the <c>ITaskHandler</c> for its task type, applies the
/// time-out, records the execution, applies <c>RetryPolicy</c> on failure and publishes events
/// (FR-22, FR-23, FR-31, FR-43). Each execution is isolated so a failing handler cannot stop the engine.
/// </summary>
public sealed class TaskExecutor
{
}
