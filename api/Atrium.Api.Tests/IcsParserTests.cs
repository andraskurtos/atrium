using Atrium.Api.Calendar;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class IcsParserTests
{
    [Fact]
    public void Parse_ExtractsBasicUtcEvent_WithLocationDescriptionAndUid()
    {
        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:-//Atrium//Test//EN
            BEGIN:VEVENT
            UID:evt-basic-001@atrium.local
            DTSTART:20261126T093000Z
            DTEND:20261126T101500Z
            SUMMARY:Deep work session
            LOCATION:Home Studio
            DESCRIPTION:Draft calendar schema and migrations
            STATUS:CONFIRMED
            RRULE:FREQ=WEEKLY;BYDAY=TH
            END:VEVENT
            END:VCALENDAR
            """;

        var parsed = IcsParser.Parse(ics);

        Assert.Empty(parsed.Warnings);
        var evt = Assert.Single(parsed.Events);
        Assert.Equal("evt-basic-001@atrium.local", evt.Uid);
        Assert.Equal("Deep work session", evt.Title);
        Assert.Equal("Home Studio", evt.Location);
        Assert.Equal("Draft calendar schema and migrations", evt.Description);
        Assert.Equal(new DateTimeOffset(2026, 11, 26, 9, 30, 0, TimeSpan.Zero), evt.StartAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 26, 10, 15, 0, TimeSpan.Zero), evt.EndAt);
        Assert.False(evt.IsAllDay);
        Assert.Equal("confirmed", evt.Status);
        Assert.Equal("FREQ=WEEKLY;BYDAY=TH", evt.RecurrenceRule);
    }

    [Fact]
    public void Parse_UnfoldsFoldedLines_AndUnescapesTextValues()
    {
        const string ics =
            "BEGIN:VCALENDAR\r\n" +
            "VERSION:2.0\r\n" +
            "BEGIN:VEVENT\r\n" +
            "UID:folded-001\r\n" +
            "DTSTART:20261201T140000Z\r\n" +
            "DTEND:20261201T150000Z\r\n" +
            "SUMMARY:Quarterly review\\, Part\r\n" +
            "  2\\; Budget & Roadmap\r\n" +
            "LOCATION:Room 4B\\, West Wing\\\\Floor 2\r\n" +
            "DESCRIPTION:Line one\\nLine two\r\n" +
            "\t continued\\NLine three\r\n" +
            "END:VEVENT\r\n" +
            "END:VCALENDAR\r\n";

        var parsed = IcsParser.Parse(ics);

        var evt = Assert.Single(parsed.Events);
        Assert.Equal("Quarterly review, Part 2; Budget & Roadmap", evt.Title);
        Assert.Equal("Room 4B, West Wing\\Floor 2", evt.Location);
        Assert.Equal("Line one\nLine two continued\nLine three", evt.Description);
    }

    [Fact]
    public void Parse_HandlesAllDayDateEvents_WithAndWithoutDtEnd()
    {
        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VEVENT
            UID:allday-single
            DTSTART;VALUE=DATE:20261225
            SUMMARY:Christmas Day
            END:VEVENT
            BEGIN:VEVENT
            UID:allday-multi
            DTSTART;VALUE=DATE:20261228
            DTEND;VALUE=DATE:20261231
            SUMMARY:Winter Retreat
            END:VEVENT
            END:VCALENDAR
            """;

        var parsed = IcsParser.Parse(ics);

        Assert.Equal(2, parsed.Events.Count);
        var single = parsed.Events[0];
        Assert.True(single.IsAllDay);
        Assert.Equal(new DateTimeOffset(2026, 12, 25, 0, 0, 0, TimeSpan.Zero), single.StartAt);
        Assert.Equal(new DateTimeOffset(2026, 12, 26, 0, 0, 0, TimeSpan.Zero), single.EndAt);

        var multi = parsed.Events[1];
        Assert.True(multi.IsAllDay);
        Assert.Equal(new DateTimeOffset(2026, 12, 28, 0, 0, 0, TimeSpan.Zero), multi.StartAt);
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero), multi.EndAt);
    }

    [Fact]
    public void Parse_HandlesTzidAndFloatingLocalTimes()
    {
        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            X-WR-TIMEZONE:America/New_York
            BEGIN:VEVENT
            UID:tzid-evt
            DTSTART;TZID=America/New_York:20260715T090000
            DTEND;TZID=America/New_York:20260715T103000
            SUMMARY:Summer Morning Standup
            END:VEVENT
            BEGIN:VEVENT
            UID:floating-evt
            DTSTART:20260715T140000
            DTEND:20260715T150000
            SUMMARY:Afternoon Workshop
            END:VEVENT
            END:VCALENDAR
            """;

        var parsed = IcsParser.Parse(ics);

        Assert.Equal("America/New_York", parsed.Timezone);
        Assert.Equal(2, parsed.Events.Count);

        // July in America/New_York is EDT (UTC-4)
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 13, 0, 0, TimeSpan.Zero), parsed.Events[0].StartAt);
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 14, 30, 0, TimeSpan.Zero), parsed.Events[0].EndAt);
        Assert.Equal("America/New_York", parsed.Events[0].Timezone);

        Assert.Equal(new DateTimeOffset(2026, 7, 15, 18, 0, 0, TimeSpan.Zero), parsed.Events[1].StartAt);
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 19, 0, 0, TimeSpan.Zero), parsed.Events[1].EndAt);
    }

    [Fact]
    public void Parse_ComputesEndFromDuration_WhenDtEndIsMissing()
    {
        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VEVENT
            UID:dur-1
            DTSTART:20261126T100000Z
            DURATION:PT1H30M
            SUMMARY:Design Critique
            END:VEVENT
            BEGIN:VEVENT
            UID:dur-2
            DTSTART:20261127T080000Z
            DURATION:P1DT2H
            SUMMARY:Hackathon
            END:VEVENT
            END:VCALENDAR
            """;

        var parsed = IcsParser.Parse(ics);

        Assert.Equal(2, parsed.Events.Count);
        Assert.Equal(new DateTimeOffset(2026, 11, 26, 11, 30, 0, TimeSpan.Zero), parsed.Events[0].EndAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero), parsed.Events[1].EndAt);
    }

    [Fact]
    public void Parse_IgnoresNestedValarmProperties_AndNonEventComponents()
    {
        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VTIMEZONE
            TZID:Europe/Budapest
            BEGIN:STANDARD
            DTSTART:19701025T030000
            TZOFFSETFROM:+0200
            TZOFFSETTO:+0100
            END:STANDARD
            END:VTIMEZONE
            BEGIN:VTODO
            UID:todo-1
            SUMMARY:Ignored todo
            END:VTODO
            BEGIN:VEVENT
            UID:alarm-parent
            DTSTART:20261126T160000Z
            DTEND:20261126T170000Z
            SUMMARY:Parent Event Title
            DESCRIPTION:Parent Event Description
            BEGIN:VALARM
            ACTION:DISPLAY
            TRIGGER:-PT15M
            DURATION:PT5M
            SUMMARY:Alarm Summary Should Not Overwrite
            DESCRIPTION:Alarm Description Should Not Overwrite
            END:VALARM
            END:VEVENT
            END:VCALENDAR
            """;

        var parsed = IcsParser.Parse(ics);

        var evt = Assert.Single(parsed.Events);
        Assert.Equal("Parent Event Title", evt.Title);
        Assert.Equal("Parent Event Description", evt.Description);
        Assert.Equal(new DateTimeOffset(2026, 11, 26, 16, 0, 0, TimeSpan.Zero), evt.StartAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 26, 17, 0, 0, TimeSpan.Zero), evt.EndAt);
    }

    [Fact]
    public void Parse_ExtractsCalendarMetadata_AndMultipleOverlappingEvents()
    {
        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            X-WR-CALNAME:Team Engineering
            X-WR-CALDESC:Shared sprint & release schedule
            X-WR-TIMEZONE:UTC
            BEGIN:VEVENT
            UID:overlap-1
            DTSTART:20261126T100000Z
            DTEND:20261126T110000Z
            SUMMARY:Architecture Review
            END:VEVENT
            BEGIN:VEVENT
            UID:overlap-2
            DTSTART:20261126T103000Z
            DTEND:20261126T113000Z
            SUMMARY:1:1 Sync
            STATUS:TENTATIVE
            END:VEVENT
            END:VCALENDAR
            """;

        var parsed = IcsParser.Parse(ics);

        Assert.Equal("Team Engineering", parsed.CalendarName);
        Assert.Equal("Shared sprint & release schedule", parsed.CalendarDescription);
        Assert.Equal(2, parsed.Events.Count);
        Assert.Equal("confirmed", parsed.Events[0].Status);
        Assert.Equal("tentative", parsed.Events[1].Status);
    }

    [Fact]
    public void Parse_ThrowsFormatExceptionForInvalidIcs_AndSkipsMalformedVEventWithWarning()
    {
        Assert.Throws<FormatException>(() => IcsParser.Parse("not an ics file"));

        const string partialIcs = """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VEVENT
            UID:missing-start
            SUMMARY:Broken event without start
            END:VEVENT
            BEGIN:VEVENT
            UID:valid-evt
            DTSTART:20261126T120000Z
            DTEND:20261126T123000Z
            SUMMARY:Valid Lunch
            END:VEVENT
            END:VCALENDAR
            """;

        var parsed = IcsParser.Parse(partialIcs);
        var evt = Assert.Single(parsed.Events);
        Assert.Equal("Valid Lunch", evt.Title);
        Assert.Single(parsed.Warnings);
    }

    [Fact]
    public void Serialize_AndParse_RoundTripsCalendarAndEvents()
    {
        var calId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var sourceEvents = new[]
        {
            new CalendarEventDto(
                Guid.NewGuid(),
                calId,
                "Personal",
                "#c25438",
                "Dentist, Check-up; Annual",
                "Bring insurance card\nArrive 10m early",
                "Midtown Clinic, Suite 300",
                new DateTimeOffset(2026, 11, 28, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 28, 9, 45, 0, TimeSpan.Zero),
                false,
                "UTC",
                "confirmed",
                "dentist-uid-1@atrium.local",
                null,
                "Manual",
                now,
                now),
            new CalendarEventDto(
                Guid.NewGuid(),
                calId,
                "Personal",
                "#c25438",
                "National Holiday",
                null,
                null,
                new DateTimeOffset(2026, 12, 25, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 12, 26, 0, 0, 0, TimeSpan.Zero),
                true,
                "UTC",
                "tentative",
                "holiday-uid-2@atrium.local",
                null,
                "Manual",
                now,
                now),
        };

        var icsText = IcsParser.Serialize("Personal", "My events", "UTC", sourceEvents);
        var parsed = IcsParser.Parse(icsText);

        Assert.Equal("Personal", parsed.CalendarName);
        Assert.Equal("My events", parsed.CalendarDescription);
        Assert.Equal(2, parsed.Events.Count);
        Assert.Equal("Dentist, Check-up; Annual", parsed.Events[0].Title);
        Assert.Equal("Bring insurance card\nArrive 10m early", parsed.Events[0].Description);
        Assert.Equal("Midtown Clinic, Suite 300", parsed.Events[0].Location);
        Assert.Equal("dentist-uid-1@atrium.local", parsed.Events[0].Uid);
        Assert.Equal(new DateTimeOffset(2026, 11, 28, 9, 0, 0, TimeSpan.Zero), parsed.Events[0].StartAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 28, 9, 45, 0, TimeSpan.Zero), parsed.Events[0].EndAt);
        Assert.False(parsed.Events[0].IsAllDay);

        Assert.Equal("National Holiday", parsed.Events[1].Title);
        Assert.True(parsed.Events[1].IsAllDay);
        Assert.Equal("tentative", parsed.Events[1].Status);
        Assert.Equal(new DateTimeOffset(2026, 12, 25, 0, 0, 0, TimeSpan.Zero), parsed.Events[1].StartAt);
        Assert.Equal(new DateTimeOffset(2026, 12, 26, 0, 0, 0, TimeSpan.Zero), parsed.Events[1].EndAt);
    }
}
