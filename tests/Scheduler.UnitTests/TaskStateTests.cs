using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Domain.Rules;

namespace Scheduler.UnitTests;

/// <summary>WP-6: the task state table and which queue entries may run.</summary>
public class TaskStateTests
{
    [Theory]
    [InlineData(ScheduledTaskStatus.Active, ScheduledTaskStatus.Paused, true)]
    [InlineData(ScheduledTaskStatus.Active, ScheduledTaskStatus.Disabled, true)]
    [InlineData(ScheduledTaskStatus.Paused, ScheduledTaskStatus.Active, true)]
    [InlineData(ScheduledTaskStatus.Paused, ScheduledTaskStatus.Disabled, true)]
    [InlineData(ScheduledTaskStatus.Disabled, ScheduledTaskStatus.Active, true)]
    [InlineData(ScheduledTaskStatus.Disabled, ScheduledTaskStatus.Paused, false)]
    public void Transitions_FollowTheStateTable(ScheduledTaskStatus from, ScheduledTaskStatus to, bool allowed) =>
        Assert.Equal(allowed, StatusTransitions.IsAllowed(from, to));

    [Fact]
    public void PausedTask_RunsOnlyForAManualRunAndItsRetries()
    {
        var task = NewTask();
        task.Pause(DateTime.UtcNow, null);
        var manual = QueueEntry.Create(task, null, TriggerType.Manual, DateTime.UtcNow, 0, DateTime.UtcNow);
        var retryOfManual = manual.FollowUp(task, TriggerType.Retry, DateTime.UtcNow, 1, DateTime.UtcNow);
        var scheduled = QueueEntry.Create(task, Guid.NewGuid(), TriggerType.Scheduled, DateTime.UtcNow, 0, DateTime.UtcNow);
        var retryOfScheduled = scheduled.FollowUp(task, TriggerType.Retry, DateTime.UtcNow, 1, DateTime.UtcNow);

        Assert.True(manual.MayRun(task));
        Assert.True(retryOfManual.MayRun(task));
        Assert.Equal(TriggerType.Retry, retryOfManual.TriggerType);
        Assert.False(scheduled.MayRun(task));
        Assert.False(retryOfScheduled.MayRun(task));
        Assert.Equal(scheduled.ScheduleId, retryOfScheduled.ScheduleId);
    }

    [Fact]
    public void DisabledTask_NeverRuns()
    {
        var task = NewTask();
        task.Disable(DateTime.UtcNow, null);

        Assert.False(QueueEntry.Create(task, null, TriggerType.Manual, DateTime.UtcNow, 0, DateTime.UtcNow).MayRun(task));
    }

    private static ScheduledTask NewTask() => new("Task", null, "Test", "{}", 5, null, 60, DateTime.UtcNow, null);
}
