namespace Scheduler.Application.Common.Persistence;

public interface IResourceHealthReader
{
    Task<ResourceHealthSnapshot> GetSnapshotAsync(DateTime executionsSinceUtc, DateTime staleBeforeUtc,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceHealthSnapshot(
    int WaitingQueueEntries,
    int ClaimedQueueEntries,
    int BusyWorkers,
    int TotalWorkers,
    int StaleWorkers,
    IReadOnlyList<TaskExecutionHealth> RecentTaskExecutions);

public sealed record TaskExecutionHealth(Guid TaskId, string TaskName, int FailureCount, int TimeoutCount);
