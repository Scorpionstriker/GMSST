using Scheduler.Domain.Enums;
using Scheduler.Domain.Rules;

namespace Scheduler.Domain.Entities;

/// <summary>
/// One attempt to run a task: planned and actual timing, outcome, retry number and output (FR-22, FR-23).
/// The schedule is optional because manual, retry and dependency runs have none (BR-04).
/// </summary>
public sealed class TaskExecution
{
    private TaskExecution()
    {
    }

    private TaskExecution(QueueEntry entry, Guid threadId, DateTime startTimeUtc)
    {
        TaskId = entry.TaskId;
        ScheduleId = entry.ScheduleId;
        QueueId = entry.Id;
        ThreadId = threadId;
        TriggerType = entry.TriggerType;
        ScheduledTimeUtc = entry.ScheduledTimeUtc;
        StartTimeUtc = startTimeUtc;
        Status = ExecutionStatus.Running;
        RetryCount = entry.Attempt;
    }

    public static TaskExecution Start(QueueEntry entry, Guid threadId, DateTime startTimeUtc) => new(entry, threadId, startTimeUtc);

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid TaskId { get; private set; }
    public ScheduledTask? Task { get; private set; }
    public Guid? ScheduleId { get; private set; }
    /// <summary>
    /// The queue entry that produced this execution. Queue entries are purged after seven days while executions
    /// are kept for the retention period, so the link is cleared when the entry is removed (Phase 4, Section 5.6).
    /// </summary>
    public Guid? QueueId { get; private set; }
    public Guid ThreadId { get; private set; }
    public TriggerType TriggerType { get; private set; }
    public DateTime ScheduledTimeUtc { get; private set; }
    public DateTime StartTimeUtc { get; private set; }
    public DateTime? EndTimeUtc { get; private set; }
    public int? DurationMs { get; private set; }
    public ExecutionStatus Status { get; private set; }
    public int RetryCount { get; private set; }

    /// <summary>Structured result returned by the handler, as JSON.</summary>
    public string? Output { get; private set; }

    /// <summary>Delay between the planned and the actual start, in milliseconds.</summary>
    public double StartDelayMs => Math.Max(0, (StartTimeUtc - ScheduledTimeUtc).TotalMilliseconds);

    public void Complete(ExecutionStatus status, DateTime endTimeUtc, string? output)
    {
        StatusTransitions.Ensure(StatusTransitions.IsAllowed(Status, status), "task execution", Status, status);
        Status = status;
        EndTimeUtc = endTimeUtc < StartTimeUtc ? StartTimeUtc : endTimeUtc;
        DurationMs = (int)Math.Min(int.MaxValue, (EndTimeUtc.Value - StartTimeUtc).TotalMilliseconds);
        Output = output;
    }
}
