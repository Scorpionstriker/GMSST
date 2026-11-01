using Scheduler.Domain.Enums;
using Scheduler.Domain.Rules;

namespace Scheduler.Domain.Entities;

/// <summary>
/// Due work waiting to run (FR-19). A worker claims it with a time-limited lease (FR-20); the lease lets
/// work abandoned by a crash be recognised and recovered (FR-33).
/// </summary>
public sealed class QueueEntry
{
    private QueueEntry()
    {
    }

    private QueueEntry(Guid taskId, Guid? scheduleId, TriggerType triggerType, int priority, DateTime scheduledTimeUtc,
        int attempt, DateTime nowUtc, string? eventName, string? eventSource, string? eventDataJson)
    {
        TaskId = taskId;
        ScheduleId = scheduleId;
        TriggerType = triggerType;
        Priority = priority;
        ScheduledTimeUtc = DateTime.SpecifyKind(scheduledTimeUtc, DateTimeKind.Utc);
        EnqueuedAtUtc = nowUtc;
        Status = QueueStatus.Waiting;
        Attempt = attempt;
        IsManualRun = triggerType == TriggerType.Manual;
        EventName = eventName;
        EventSource = eventSource;
        EventDataJson = eventDataJson;
    }

    /// <summary>Creates a Waiting entry. The task's priority is copied so the claim query needs no join.</summary>
    public static QueueEntry Create(ScheduledTask task, Guid? scheduleId, TriggerType triggerType, DateTime scheduledTimeUtc,
        int attempt, DateTime nowUtc, string? eventName = null, string? eventSource = null, string? eventDataJson = null) =>
        new(task.Id, scheduleId, triggerType, task.Priority, scheduledTimeUtc, attempt, nowUtc, eventName, eventSource, eventDataJson);

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid TaskId { get; private set; }
    public ScheduledTask? Task { get; private set; }
    public Guid? ScheduleId { get; private set; }
    public TriggerType TriggerType { get; private set; }
    public int Priority { get; private set; }

    /// <summary>Time from which the entry may be claimed.</summary>
    public DateTime ScheduledTimeUtc { get; private set; }

    public DateTime EnqueuedAtUtc { get; private set; }
    public QueueStatus Status { get; private set; }
    public Guid? ClaimedBy { get; private set; }
    public DateTime? ClaimedAtUtc { get; private set; }
    public DateTime? ClaimedUntilUtc { get; private set; }

    /// <summary>Retry attempt this entry represents; 0 for the first run (BR-08).</summary>
    public int Attempt { get; private set; }

    /// <summary>True for a Run now entry and for the retries and re-queues that follow from it (WP-6).</summary>
    public bool IsManualRun { get; private set; }

    public string? EventName { get; private set; }
    public string? EventSource { get; private set; }
    public string? EventDataJson { get; private set; }

    /// <summary>
    /// A retry or re-queue of this entry. It keeps the schedule and whether the run was started manually, so the retries
    /// of a manual run of a paused task still run (WP-6).
    /// </summary>
    public QueueEntry FollowUp(ScheduledTask task, TriggerType triggerType, DateTime scheduledTimeUtc, int attempt, DateTime nowUtc)
    {
        var next = Create(task, ScheduleId, triggerType, scheduledTimeUtc, attempt, nowUtc);
        next.IsManualRun = IsManualRun;
        next.EventName = EventName;
        next.EventSource = EventSource;
        next.EventDataJson = EventDataJson;
        return next;
    }

    /// <summary>
    /// Only Active tasks run. A paused task runs only for a manual run and its retries; a disabled or deleted task never
    /// runs (FR-02, FR-03, WP-6 state table).
    /// </summary>
    public bool MayRun(ScheduledTask task) =>
        !task.IsDeleted && (task.Status == ScheduledTaskStatus.Active || (task.Status == ScheduledTaskStatus.Paused && IsManualRun));

    public void Cancel()
    {
        StatusTransitions.Ensure(StatusTransitions.IsAllowed(Status, QueueStatus.Cancelled), "queue entry", Status, QueueStatus.Cancelled);
        Status = QueueStatus.Cancelled;
    }

    public void MarkProcessed()
    {
        StatusTransitions.Ensure(StatusTransitions.IsAllowed(Status, QueueStatus.Processed), "queue entry", Status, QueueStatus.Processed);
        Status = QueueStatus.Processed;
    }

    /// <summary>Returns an entry whose lease expired before it produced an execution (Algorithm 6.11).</summary>
    public void ReleaseClaim()
    {
        StatusTransitions.Ensure(StatusTransitions.IsAllowed(Status, QueueStatus.Waiting), "queue entry", Status, QueueStatus.Waiting);
        Status = QueueStatus.Waiting;
        ClaimedBy = null;
        ClaimedAtUtc = null;
        ClaimedUntilUtc = null;
    }
}
