using Scheduler.Application.Cron;
using Scheduler.Domain.Entities;

namespace Scheduler.Application.Tasks;

/// <summary>Turns validated editor input into entities, shared by the create and update use cases and the scheduler client.</summary>
internal static class TaskWriter
{
    public static ScheduledTask CreateTask(TaskInput input, DateTime nowUtc, Guid? userId)
    {
        var task = new ScheduledTask(input.Name, input.Description, input.TaskType, input.Payload, input.Priority, input.MaxRetries,
            input.TimeoutSeconds, nowUtc, userId);
        task.SetSchedule(BuildSchedule(task.Id, input.Schedule, nowUtc, userId), nowUtc, userId);
        task.SetDependencies(input.DependsOn, nowUtc);
        return task;
    }

    /// <summary>Builds a schedule and calculates its first run (FR-13).</summary>
    public static Schedule BuildSchedule(Guid taskId, ScheduleInput input, DateTime nowUtc, Guid? userId)
    {
        var zone = TimeZones.FindOrUtc(input.TimeZoneId);
        var startUtc = TimeZones.ToUtc(input.StartTime ?? TimeZones.FromUtc(nowUtc, zone), zone);
        DateTime? endUtc = input.EndTime is null ? null : TimeZones.ToUtc(input.EndTime.Value, zone);

        var schedule = Schedule.Create(taskId, input.Type, input.CronExpression, input.IntervalSeconds, input.TimeZoneId,
            input.MisfirePolicy, startUtc, endUtc, nowUtc, userId);
        schedule.SetNextRun(ScheduleCalculator.GetNextRun(ScheduleSpec.From(schedule), nowUtc));
        return schedule;
    }

    /// <summary>The editor's view of a stored schedule, with times converted back to its time zone.</summary>
    public static ScheduleInput ToInput(Schedule? schedule)
    {
        if (schedule is null)
        {
            return new ScheduleInput();
        }

        var zone = TimeZones.FindOrUtc(schedule.TimeZoneId);
        return new ScheduleInput
        {
            Type = schedule.Type,
            CronExpression = schedule.CronExpression,
            IntervalSeconds = schedule.IntervalSeconds,
            TimeZoneId = schedule.TimeZoneId,
            MisfirePolicy = schedule.MisfirePolicy,
            StartTime = TimeZones.FromUtc(schedule.StartTimeUtc, zone),
            EndTime = schedule.EndTimeUtc is null ? null : TimeZones.FromUtc(schedule.EndTimeUtc.Value, zone)
        };
    }
}
