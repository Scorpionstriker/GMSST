using Scheduler.Application.Cron;

namespace Scheduler.UnitTests;

public class CronExpressionTests
{
    private static readonly TimeZoneInfo London = TimeZones.FindOrUtc("Europe/London");
    private static readonly TimeZoneInfo Johannesburg = TimeZones.FindOrUtc("Africa/Johannesburg");

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    [Fact]
    public void NextOccurrence_EveryFiveMinutes()
    {
        var cron = CronExpression.Parse("*/5 * * * *");

        Assert.Equal(Utc(2026, 1, 1, 10, 5), cron.GetNextOccurrence(Utc(2026, 1, 1, 10, 0), TimeZoneInfo.Utc));
    }

    [Fact]
    public void NextOccurrence_InANonUtcZone_IsConvertedToUtc()
    {
        // Daily at 02:00 in Johannesburg (UTC+2) is 00:00 UTC. A non-UTC zone must not fail.
        var cron = CronExpression.Parse("0 2 * * *");

        Assert.Equal(Utc(2026, 3, 2, 0, 0), cron.GetNextOccurrence(Utc(2026, 3, 1, 1, 0), Johannesburg));
    }

    [Fact]
    public void NextOccurrence_FollowsSummerTime()
    {
        var cron = CronExpression.Parse("0 9 * * *");

        // 09:00 in London is 09:00 UTC in winter and 08:00 UTC in summer.
        Assert.Equal(Utc(2026, 1, 15, 9, 0), cron.GetNextOccurrence(Utc(2026, 1, 15, 0, 0), London));
        Assert.Equal(Utc(2026, 7, 15, 8, 0), cron.GetNextOccurrence(Utc(2026, 7, 15, 0, 0), London));
    }

    [Fact]
    public void NextOccurrence_RunsARepeatedTimeOnlyOnce()
    {
        // On 25 October 2026 London clocks go back from 02:00 to 01:00, so 01:30 happens twice.
        var cron = CronExpression.Parse("30 1 * * *");
        var first = cron.GetNextOccurrence(Utc(2026, 10, 24, 12, 0), London)!.Value;
        var second = cron.GetNextOccurrence(first, London)!.Value;

        Assert.Equal(new DateTime(2026, 10, 25), first.Date);
        Assert.Equal(new DateTime(2026, 10, 26), second.Date);
    }

    [Theory]
    [InlineData("0 9 * * MON-FRI")]
    [InlineData("0 9 * JAN-DEC 1-5")]
    public void Names_AreAccepted(string text)
    {
        Assert.True(CronExpression.TryParse(text, out _, out var error), error);
    }

    [Fact]
    public void Seven_MeansSunday()
    {
        var sunday = CronExpression.Parse("0 12 * * 7");

        // 4 January 2026 is a Sunday.
        Assert.Equal(Utc(2026, 1, 4, 12, 0), sunday.GetNextOccurrence(Utc(2026, 1, 1, 0, 0), TimeZoneInfo.Utc));
    }

    [Fact]
    public void BothDayFields_MatchEitherDay()
    {
        // Standard cron: the 15th of the month OR any Monday.
        var cron = CronExpression.Parse("0 0 15 * MON");

        // 5 January 2026 is a Monday, before the 15th.
        Assert.Equal(Utc(2026, 1, 5, 0, 0), cron.GetNextOccurrence(Utc(2026, 1, 1, 0, 0), TimeZoneInfo.Utc));
    }

    [Fact]
    public void DayFieldStartingWithStar_DoesNotTurnOnTheEitherDayRule()
    {
        // WP-8g: "*/2" in day-of-month counts as unrestricted for the either-day rule, so both fields must match:
        // odd days of the month that are also Mondays. 5 January 2026 is a Monday on an odd day.
        var cron = CronExpression.Parse("0 0 */2 * MON");
        Assert.Equal(Utc(2026, 1, 5, 0, 0), cron.GetNextOccurrence(Utc(2026, 1, 1, 0, 0), TimeZoneInfo.Utc));

        // The next odd-day Monday is 19 January (12 January is even).
        Assert.Equal(Utc(2026, 1, 19, 0, 0), cron.GetNextOccurrence(Utc(2026, 1, 5, 0, 0), TimeZoneInfo.Utc));

        // And a step in day-of-week alone still restricts the days: Sunday, Tuesday, Thursday, Saturday.
        var everyOtherDay = CronExpression.Parse("0 0 * * */2");
        Assert.Equal(Utc(2026, 1, 3, 0, 0), everyOtherDay.GetNextOccurrence(Utc(2026, 1, 1, 0, 0), TimeZoneInfo.Utc)); // Saturday
    }

    [Fact]
    public void LocalTimeSkippedByDaylightSaving_RunsAtTheFirstValidMinuteAfterTheGap()
    {
        // On 29 March 2026 London's clocks go from 01:00 to 02:00, so 01:30 does not exist.
        var cron = CronExpression.Parse("30 1 * * *");
        var next = cron.GetNextOccurrence(Utc(2026, 3, 28, 12, 0), London);

        // 02:00 BST is 01:00 UTC on that day.
        Assert.Equal(Utc(2026, 3, 29, 1, 0), next);

        // The following run is 01:30 GMT+1 the next day, with no extra run in between.
        Assert.Equal(Utc(2026, 3, 30, 0, 30), cron.GetNextOccurrence(next!.Value, London));
    }

    [Theory]
    [InlineData("*/0 * * * *", "step")]
    [InlineData("60 * * * *", "minute")]
    [InlineData("0 24 * * *", "hour")]
    [InlineData("0 0 L * *", "day")]
    [InlineData("0 0 * *", "five fields")]
    [InlineData("5-1 * * * *", "range")]
    public void Invalid_ExpressionsAreRejectedWithAClearMessage(string text, string expectedInMessage)
    {
        Assert.False(CronExpression.TryParse(text, out _, out var error));
        Assert.Contains(expectedInMessage, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ZeroStep_DoesNotHang()
    {
        var task = Task.Run(() => CronExpression.TryParse("*/0 * * * *", out _, out _));

        var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.True(finished == task, "Parsing a zero step must return at once.");
    }

    [Fact]
    public void ImpossibleDate_HasNoNextOccurrence()
    {
        var cron = CronExpression.Parse("0 0 31 2 *"); // 31 February

        Assert.Null(cron.GetNextOccurrence(Utc(2026, 1, 1), TimeZoneInfo.Utc));
    }
}
