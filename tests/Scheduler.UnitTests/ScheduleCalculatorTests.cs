using Scheduler.Application.Cron;
using Scheduler.Domain.Enums;

namespace Scheduler.UnitTests;

public class ScheduleCalculatorTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ScheduleSpec Interval(int seconds, DateTime? end = null) =>
        new(ScheduleType.Interval, null, seconds, TimeZoneInfo.Utc, Start, end, null);

    private static ScheduleSpec Cron(string text) =>
        new(ScheduleType.Cron, CronExpression.Parse(text), null, TimeZoneInfo.Utc, Start, null, null);

    [Fact]
    public void Interval_NextRunIsOneIntervalAfterTheLastDueTime()
    {
        Assert.Equal(Start.AddMinutes(10), ScheduleCalculator.GetNextRun(Interval(600), Start.AddMinutes(1)));
    }

    [Fact]
    public void Once_IsDueUntilItHasRun()
    {
        var once = new ScheduleSpec(ScheduleType.Once, null, null, TimeZoneInfo.Utc, Start, null, null);

        // A single run that was missed (for example while the computer was off) is still due.
        Assert.Equal(Start, ScheduleCalculator.GetNextRun(once, Start.AddHours(1)));
        Assert.Null(ScheduleCalculator.GetNextRun(once with { LastRunUtc = Start }, Start.AddHours(1)));
    }

    [Fact]
    public void EndTime_StopsTheSchedule()
    {
        var spec = Interval(3600, end: Start.AddHours(2));

        Assert.Null(ScheduleCalculator.GetNextRun(spec, Start.AddHours(2)));
    }

    [Fact]
    public void Preview_ReturnsTheNextFiveRuns()
    {
        var runs = ScheduleCalculator.Preview(Cron("0 * * * *"), Start.AddMinutes(30), 5);

        Assert.Equal(5, runs.Count);
        Assert.Equal(Start.AddHours(1), runs[0]);
        Assert.Equal(Start.AddHours(5), runs[4]);
    }

    [Fact]
    public void OnTimeRun_IsQueuedOnceAsScheduled()
    {
        var plan = ScheduleCalculator.PlanDueRuns(Cron("*/5 * * * *"), MisfirePolicy.RunOnce, Start, Start.AddSeconds(5), 10);

        var run = Assert.Single(plan);
        Assert.Equal(TriggerType.Scheduled, run.TriggerType);
        Assert.Equal(Start, run.ScheduledTimeUtc);
    }

    [Fact]
    public void Misfire_Skip_QueuesNothing()
    {
        var now = Start.AddHours(3).AddMinutes(30);

        Assert.Empty(ScheduleCalculator.PlanDueRuns(Cron("0 * * * *"), MisfirePolicy.Skip, Start, now, 10));
    }

    [Fact]
    public void Misfire_RunOnce_QueuesOneCatchUpRun()
    {
        var now = Start.AddHours(3).AddMinutes(30);

        var run = Assert.Single(ScheduleCalculator.PlanDueRuns(Cron("0 * * * *"), MisfirePolicy.RunOnce, Start, now, 10));
        Assert.Equal(TriggerType.CatchUp, run.TriggerType);
        Assert.Equal(now, run.ScheduledTimeUtc);
    }

    [Fact]
    public void Misfire_RunAll_QueuesEveryMissedRunUpToTheCap()
    {
        var now = Start.AddHours(3).AddMinutes(30);

        Assert.Equal(4, ScheduleCalculator.PlanDueRuns(Cron("0 * * * *"), MisfirePolicy.RunAll, Start, now, 10).Count);
        Assert.Equal(2, ScheduleCalculator.PlanDueRuns(Cron("0 * * * *"), MisfirePolicy.RunAll, Start, now, 2).Count);
    }
}
