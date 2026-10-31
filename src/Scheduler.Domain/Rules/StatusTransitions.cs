using Scheduler.Domain.Enums;

namespace Scheduler.Domain.Rules;

/// <summary>
/// The permitted status transitions of Phase 4, Table 6.2. Entities call these checks, so no program
/// path can put a record into a state it cannot reach (FR-03, FR-19, FR-23).
/// </summary>
public static class StatusTransitions
{
    public static bool IsAllowed(ScheduledTaskStatus from, ScheduledTaskStatus to) => (from, to) switch
    {
        (ScheduledTaskStatus.Active, ScheduledTaskStatus.Paused) => true,
        (ScheduledTaskStatus.Active, ScheduledTaskStatus.Disabled) => true,
        (ScheduledTaskStatus.Paused, ScheduledTaskStatus.Active) => true,
        (ScheduledTaskStatus.Paused, ScheduledTaskStatus.Disabled) => true,
        (ScheduledTaskStatus.Disabled, ScheduledTaskStatus.Active) => true,
        _ => false
    };

    public static bool IsAllowed(QueueStatus from, QueueStatus to) => (from, to) switch
    {
        (QueueStatus.Waiting, QueueStatus.Claimed) => true,
        (QueueStatus.Waiting, QueueStatus.Cancelled) => true,
        (QueueStatus.Claimed, QueueStatus.Processed) => true,
        (QueueStatus.Claimed, QueueStatus.Waiting) => true,
        // A claimed entry whose task was paused, disabled or deleted before it ran.
        (QueueStatus.Claimed, QueueStatus.Cancelled) => true,
        _ => false
    };

    public static bool IsAllowed(ExecutionStatus from, ExecutionStatus to) =>
        from == ExecutionStatus.Running && to != ExecutionStatus.Running;

    public static bool IsAllowed(WorkerStatus from, WorkerStatus to) => (from, to) switch
    {
        (WorkerStatus.Idle, WorkerStatus.Busy) => true,
        (WorkerStatus.Busy, WorkerStatus.Idle) => true,
        (_, WorkerStatus.Stopped) => true,
        (WorkerStatus.Stopped, WorkerStatus.Idle) => true,
        _ => false
    };

    internal static void Ensure(bool allowed, string record, object from, object to)
    {
        if (!allowed)
        {
            throw new DomainRuleException($"A {record} cannot change from {from} to {to}.");
        }
    }
}
