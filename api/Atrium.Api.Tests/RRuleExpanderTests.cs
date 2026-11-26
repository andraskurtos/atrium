using Atrium.Api.Calendar;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class RRuleExpanderTests
{
    [Fact]
    public void ExpandOccurrences_DailyWithIntervalAndCount_ReturnsExactDates()
    {
        var start = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
        var end = start.AddMinutes(30);

        var occurrences = RRuleExpander.ExpandOccurrences(
            start,
            end,
            isAllDay: false,
            timezone: "UTC",
            rrule: "FREQ=DAILY;INTERVAL=2;COUNT=4",
            rangeFrom: new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            rangeTo: new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(4, occurrences.Count);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero), occurrences[0].StartAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero), occurrences[1].StartAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero), occurrences[2].StartAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero), occurrences[3].StartAt);
        Assert.All(occurrences, o => Assert.Equal(TimeSpan.FromMinutes(30), o.EndAt - o.StartAt));
    }

    [Fact]
    public void ExpandOccurrences_WeeklyWithByDayAndUntil_ExpandsMatchingWeekdays()
    {
        // 2026-11-02 is Monday
        var start = new DateTimeOffset(2026, 11, 2, 14, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(1);

        var occurrences = RRuleExpander.ExpandOccurrences(
            start,
            end,
            isAllDay: false,
            timezone: "UTC",
            rrule: "FREQ=WEEKLY;INTERVAL=1;BYDAY=MO,WE,FR;UNTIL=20261113T235959Z",
            rangeFrom: new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            rangeTo: new DateTimeOffset(2026, 11, 30, 0, 0, 0, TimeSpan.Zero));

        // Week 1: Nov 2 (Mo), Nov 4 (We), Nov 6 (Fr)
        // Week 2: Nov 9 (Mo), Nov 11 (We), Nov 13 (Fr)
        Assert.Equal(6, occurrences.Count);
        Assert.Equal(
            [2, 4, 6, 9, 11, 13],
            occurrences.Select(o => o.StartAt.Day).ToArray());
    }

    [Fact]
    public void ExpandOccurrences_MonthlyOn31st_SkipsMonthsWithout31Days()
    {
        var start = new DateTimeOffset(2026, 1, 31, 10, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(1);

        var occurrences = RRuleExpander.ExpandOccurrences(
            start,
            end,
            isAllDay: false,
            timezone: "UTC",
            rrule: "FREQ=MONTHLY;COUNT=4",
            rangeFrom: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            rangeTo: new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));

        // Jan 31, Mar 31, May 31, Jul 31
        Assert.Equal(4, occurrences.Count);
        Assert.Equal(
            [1, 3, 5, 7],
            occurrences.Select(o => o.StartAt.Month).ToArray());
    }

    [Fact]
    public void ExpandOccurrences_YearlyAllDay_ExpandsAcrossYears()
    {
        var start = new DateTimeOffset(2026, 12, 25, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 12, 26, 0, 0, 0, TimeSpan.Zero);

        var occurrences = RRuleExpander.ExpandOccurrences(
            start,
            end,
            isAllDay: true,
            timezone: "UTC",
            rrule: "FREQ=YEARLY;COUNT=3",
            rangeFrom: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            rangeTo: new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(3, occurrences.Count);
        Assert.Equal([2026, 2027, 2028], occurrences.Select(o => o.StartAt.Year).ToArray());
    }

    [Fact]
    public void ExpandOccurrences_PreservesWallClockTimeAcrossDstTransition()
    {
        // 2026-03-08 is US Spring Forward in America/New_York (EST UTC-5 -> EDT UTC-4)
        // Start: Monday 2026-03-02 09:00 EST = 14:00 UTC
        var start = new DateTimeOffset(2026, 3, 2, 14, 0, 0, TimeSpan.Zero);
        var end = start.AddMinutes(30);

        var occurrences = RRuleExpander.ExpandOccurrences(
            start,
            end,
            isAllDay: false,
            timezone: "America/New_York",
            rrule: "FREQ=WEEKLY;COUNT=2",
            rangeFrom: new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            rangeTo: new DateTimeOffset(2026, 3, 20, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, occurrences.Count);
        // Before DST: 09:00 EST = 14:00Z
        Assert.Equal(new DateTimeOffset(2026, 3, 2, 14, 0, 0, TimeSpan.Zero), occurrences[0].StartAt);
        // After DST: 09:00 EDT = 13:00Z
        Assert.Equal(new DateTimeOffset(2026, 3, 9, 13, 0, 0, TimeSpan.Zero), occurrences[1].StartAt);
    }

    [Fact]
    public void ExpandOccurrences_WindowFilter_ReturnsOnlyOccurrencesOverlappingRange()
    {
        var start = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(1);

        var occurrences = RRuleExpander.ExpandOccurrences(
            start,
            end,
            isAllDay: false,
            timezone: "UTC",
            rrule: "FREQ=DAILY",
            rangeFrom: new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero),
            rangeTo: new DateTimeOffset(2026, 6, 13, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(3, occurrences.Count);
        Assert.Equal([10, 11, 12], occurrences.Select(o => o.StartAt.Day).ToArray());
    }

    [Fact]
    public void TryParse_RejectsInvalidRules()
    {
        Assert.False(RRuleExpander.TryParse("FREQ=HOURLY", out _, out _));
        Assert.False(RRuleExpander.TryParse("INTERVAL=2", out _, out _));
        Assert.False(RRuleExpander.TryParse("FREQ=DAILY;INTERVAL=0", out _, out _));
        Assert.False(RRuleExpander.TryParse("FREQ=DAILY;COUNT=-1", out _, out _));
        Assert.False(RRuleExpander.TryParse("FREQ=WEEKLY;BYDAY=XX", out _, out _));
        Assert.True(RRuleExpander.TryParse("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,TH;COUNT=8", out var parsed, out var error));
        Assert.Null(error);
        Assert.NotNull(parsed);
        Assert.Equal("WEEKLY", parsed.Frequency);
        Assert.Equal(2, parsed.Interval);
        Assert.Equal(8, parsed.Count);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Thursday], parsed.ByDays);
    }
}
