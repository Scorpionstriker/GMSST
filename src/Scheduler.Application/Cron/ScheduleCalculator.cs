using Scheduler.Domain.Enums;

namespace Scheduler.Application.Cron;

/// <summary>
/// Calculates next and missed run times of cron, interval and single-run schedules within their start
/// and end window (Algorithm 6.8; FR-10 to FR-15). Pure functions, so they are unit-tested directly.
/// </summary>
public static class ScheduleCalculator
{
    /// <summary>
    /// A run whose scheduled time is older than this when the engine sees it counts as missed, for example
    /// because the computer was off or the engine was paused. Runs that are only a little late, because the
    /// engine was finishing a cycle, are not misfires.
    /// </summary>
    public static readonly TimeSpan MisfireThreshold = TimeSpan.FromSeconds(60);

    /// <summary>The next run strictly after <paramref name="afterUtc"/>, or null when the schedule has no further run.</summary>
    public static DateTime? GetNextRun(ScheduleSpec schedule, DateTime afterUtc)
    {
        var after = DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc);
        var from = Max(after, schedule.StartUtc.AddSeconds(-1));

        DateTime? next = schedule.Type switch
        {
            ScheduleType.Cron => schedule.Cron?.GetNextOccurrence(from, schedule.TimeZone),
            ScheduleType.Interval => NextInterval(schedule, after),
            // A single run that has not happened yet is due at its start time, even if that has passed.
            ScheduleType.Once => schedule.LastRunUtc is null ? schedule.StartUtc : null,
            _ => null
        };

        return WithinWindow(schedule, next);
    }

    /// <summary>
    /// Every run time from <paramref name="fromUtc"/> (a run time, inclusive) up to and including <paramref name="toUtc"/>,
    /// at most <paramref name="max"/> of them, used to detect and replay missed runs.
    /// </summary>
    public static IReadOnlyList<DateTime> GetDueRuns(ScheduleSpec schedule, DateTime fromUtc, DateTime toUtc, int max)
    {
        var runs = new List<DateTime>();
        DateTime? t = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);

        while (t is not null && t <= toUtc && runs.Count < max)
        {
            if (schedule.EndUtc is not null && t > schedule.EndUtc)
            {
                break;
            }

            runs.Add(t.Value);
            t = schedule.Type switch
            {
                ScheduleType.Cron => schedule.Cron?.GetNextOccurrence(t.Value, schedule.TimeZone),
                ScheduleType.Interval when schedule.IntervalSeconds > 0 => t.Value.AddSeconds(schedule.IntervalSeconds.Value),
                _ => null
            };
        }

        return runs;
    }

    /// <summary>
    /// Decides which runs to queue for a schedule that has become due (Algorithm 6.3, FR-15). Runs that are
    /// only a little late are queued once as Scheduled; runs older than <see cref="MisfireThreshold"/> are
    /// treated by the misfire policy: Skip queues none, RunOnce queues one catch-up run now, RunAll queues up to
    /// <paramref name="runAllCap"/> catch-up runs.
    /// </summary>
    public static IReadOnlyList<PlannedRun> PlanDueRuns(ScheduleSpec schedule, MisfirePolicy policy, DateTime nextRunUtc,
        DateTime nowUtc, int runAllCap)
    {
        var cap = Math.Max(1, runAllCap);
        var due = GetDueRuns(schedule, nextRunUtc, nowUtc, cap + 50);
        var lateBefore = nowUtc - MisfireThreshold;
        var missed = due.Where(t => t < lateBefore).ToList();
        var onTime = due.Where(t => t >= lateBefore).ToList();

        var plan = new List<PlannedRun>();
        if (missed.Count > 0)
        {
            switch (policy)
            {
                case MisfirePolicy.RunOnce when onTime.Count == 0:
                    plan.Add(new PlannedRun(nowUtc, TriggerType.CatchUp));
                    break;
                case MisfirePolicy.RunAll:
                    plan.AddRange(missed.Take(cap).Select(t => new PlannedRun(t, TriggerType.CatchUp)));
                    break;
            }
        }

        if (onTime.Count > 0)
        {
            // Several on-time runs only happen with very short intervals; one run covers them.
            plan.Add(new PlannedRun(onTime[^1], TriggerType.Scheduled));
        }

        return plan;
    }

    /// <summary>The next <paramref name="count"/> run times after <paramref name="nowUtc"/>, for the editor preview (FR-16).</summary>
    public static IReadOnlyList<DateTime> Preview(ScheduleSpec schedule, DateTime nowUtc, int count)
    {
        var runs = new List<DateTime>();
        var current = schedule;
        var after = nowUtc;

        for (var i = 0; i < count; i++)
        {
            var next = GetNextRun(current, after);
            if (next is null)
            {
                break;
            }

            runs.Add(next.Value);
            current = current with { LastRunUtc = next.Value };
            after = next.Value;
        }

        return runs;
    }

    private static DateTime? NextInterval(ScheduleSpec schedule, DateTime after)
    {
        if (schedule.IntervalSeconds is not > 0)
        {
            return null;
        }

        var interval = TimeSpan.FromSeconds(schedule.IntervalSeconds.Value);
        var next = schedule.LastRunUtc is null ? schedule.StartUtc : schedule.LastRunUtc.Value + interval;

        if (next <= after)
        {
            // Keep the cadence: jump forward a whole number of intervals.
            var behind = after - next;
            var steps = (long)(behind.Ticks / interval.Ticks) + 1;
            next = next.AddTicks(steps * interval.Ticks);
        }

        return next;
    }

    private static DateTime? WithinWindow(ScheduleSpec schedule, DateTime? next)
    {
        if (next is null)
        {
            return null;
        }

        if (schedule.EndUtc is not null && next > schedule.EndUtc)
        {
            return null;
        }

        return DateTime.SpecifyKind(next.Value, DateTimeKind.Utc);
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}

/// <summary>A run to queue: its scheduled time and why it is queued.</summary>
public sealed record PlannedRun(DateTime ScheduledTimeUtc, TriggerType TriggerType);
