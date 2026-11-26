using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Atrium.Api.Auth;
using Atrium.Api.Calendar;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class RecurringEventsTests
{
    private const string OwnerPassword = "Atrium-Owner-Pass-2026!";

    private static async Task<AuthSessionResponse> SetupOwnerAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync(
            "/api/auth/setup",
            new SetupRequest("andras", "andras@atrium.local", "András", OwnerPassword));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var session = await res.Content.ReadFromJsonAsync<AuthSessionResponse>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return session;
    }

    [Fact]
    public async Task RecurringEvent_CreatesSeries_AndExpandsOccurrencesAcrossDateRange()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        // Monday Nov 2, 2026 09:00-09:45 UTC repeating Mon/Wed/Fri
        var createRes = await client.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(
                null,
                "Morning Standup",
                new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 2, 9, 45, 0, TimeSpan.Zero),
                Location: "Huddle Room",
                Description: "Daily team check-in",
                Timezone: "UTC",
                RecurrenceRule: "FREQ=WEEKLY;INTERVAL=1;BYDAY=MO,WE,FR"));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var master = await createRes.Content.ReadFromJsonAsync<CalendarEventDto>();
        Assert.NotNull(master);
        Assert.True(master.IsRecurring);
        Assert.Equal(master.Id, master.SeriesMasterId);

        var events = await client.GetFromJsonAsync<List<CalendarEventDto>>(
            "/api/events?from=2026-11-01T00:00:00Z&to=2026-11-15T00:00:00Z");
        Assert.NotNull(events);
        Assert.Equal(6, events.Count);
        Assert.Equal([2, 4, 6, 9, 11, 13], events.Select(e => e.StartAt.Day).ToArray());
        Assert.All(events, e =>
        {
            Assert.True(e.IsRecurring);
            Assert.False(e.IsRecurrenceException);
            Assert.Equal(master.Id, e.SeriesMasterId);
            Assert.Equal(e.StartAt, e.OccurrenceStartAt);
            Assert.Equal("Morning Standup", e.Title);
            Assert.Equal("Huddle Room", e.Location);
            Assert.Equal(TimeSpan.FromMinutes(45), e.EndAt - e.StartAt);
        });
    }

    [Fact]
    public async Task RecurringEvent_EditSingleOccurrence_ModifiesOnlyThatInstance_EvenWhenMovedToAnotherDayAndCalendar()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var work = await (await client.PostAsJsonAsync(
            "/api/calendars",
            new CreateCalendarRequest("Work", null, "#2f6f55", "UTC")))
            .Content.ReadFromJsonAsync<CalendarDto>();
        Assert.NotNull(work);

        var master = await (await client.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(
                null,
                "Weekly Review",
                new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 2, 11, 0, 0, TimeSpan.Zero),
                Location: "Desk",
                RecurrenceRule: "FREQ=WEEKLY;COUNT=3")))
            .Content.ReadFromJsonAsync<CalendarEventDto>();
        Assert.NotNull(master);

        var secondOccurrenceStart = new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

        // Move ONLY the 2nd occurrence (Nov 9 -> Nov 10 at 15:00) and change calendar to Work
        var editSingleRes = await client.PutAsJsonAsync(
            $"/api/events/{master.Id}",
            new UpdateEventRequest(
                work.Id,
                "Weekly Review (Rescheduled)",
                new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 10, 16, 30, 0, TimeSpan.Zero),
                Location: "Boardroom",
                Description: "Special quarterly edition",
                Scope: "single",
                OccurrenceStartAt: secondOccurrenceStart));
        Assert.Equal(HttpStatusCode.OK, editSingleRes.StatusCode);
        var edited = await editSingleRes.Content.ReadFromJsonAsync<CalendarEventDto>();
        Assert.NotNull(edited);
        Assert.True(edited.IsRecurring);
        Assert.True(edited.IsRecurrenceException);
        Assert.Equal(master.Id, edited.SeriesMasterId);
        Assert.Equal(secondOccurrenceStart, edited.OccurrenceStartAt);
        Assert.Equal(work.Id, edited.CalendarId);

        var events = await client.GetFromJsonAsync<List<CalendarEventDto>>(
            "/api/events?from=2026-11-01T00:00:00Z&to=2026-11-30T00:00:00Z");
        Assert.NotNull(events);
        Assert.Equal(3, events.Count);

        // Occurrence #1 unchanged
        Assert.Equal("Weekly Review", events[0].Title);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero), events[0].StartAt);
        Assert.Equal("Personal", events[0].CalendarName);
        Assert.False(events[0].IsRecurrenceException);

        // Occurrence #2 replaced by edited exception (Nov 9 slot is gone, Nov 10 appears)
        Assert.Equal("Weekly Review (Rescheduled)", events[1].Title);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.Zero), events[1].StartAt);
        Assert.Equal("Work", events[1].CalendarName);
        Assert.Equal("Boardroom", events[1].Location);
        Assert.True(events[1].IsRecurrenceException);

        // Occurrence #3 unchanged
        Assert.Equal("Weekly Review", events[2].Title);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero), events[2].StartAt);
        Assert.Equal("Personal", events[2].CalendarName);
        Assert.False(events[2].IsRecurrenceException);
    }

    [Fact]
    public async Task RecurringEvent_EditWholeSeriesFromLaterOccurrence_UpdatesAllOccurrencesWithoutErasingEarlierOnes()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var master = await (await client.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(
                null,
                "Piano Practice",
                new DateTimeOffset(2026, 11, 2, 18, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 2, 18, 45, 0, TimeSpan.Zero),
                Location: "Living Room",
                RecurrenceRule: "FREQ=WEEKLY;COUNT=3")))
            .Content.ReadFromJsonAsync<CalendarEventDto>();
        Assert.NotNull(master);

        // User clicks "Edit whole series" while viewing the 2nd occurrence (Nov 9 18:00),
        // shifts time to 19:00-20:00, and updates title & location.
        var editSeriesRes = await client.PutAsJsonAsync(
            $"/api/events/{master.Id}",
            new UpdateEventRequest(
                master.CalendarId,
                "Piano & Theory",
                new DateTimeOffset(2026, 11, 9, 19, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 9, 20, 0, 0, TimeSpan.Zero),
                Location: "Music Studio",
                RecurrenceRule: "FREQ=WEEKLY;COUNT=3",
                Scope: "series",
                OccurrenceStartAt: new DateTimeOffset(2026, 11, 9, 18, 0, 0, TimeSpan.Zero)));
        Assert.Equal(HttpStatusCode.OK, editSeriesRes.StatusCode);

        var events = await client.GetFromJsonAsync<List<CalendarEventDto>>(
            "/api/events?from=2026-11-01T00:00:00Z&to=2026-11-30T00:00:00Z");
        Assert.NotNull(events);
        Assert.Equal(3, events.Count);

        // All 3 dates (Nov 2, Nov 9, Nov 16) preserved and shifted to 19:00-20:00!
        Assert.Equal([2, 9, 16], events.Select(e => e.StartAt.Day).ToArray());
        Assert.All(events, e =>
        {
            Assert.Equal("Piano & Theory", e.Title);
            Assert.Equal("Music Studio", e.Location);
            Assert.Equal(19, e.StartAt.Hour);
            Assert.Equal(TimeSpan.FromHours(1), e.EndAt - e.StartAt);
        });
    }

    [Fact]
    public async Task RecurringEvent_DeleteSingleOccurrence_RemovesOnlyThatInstance_EvenWhenPreviouslyEdited()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var master = await (await client.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(
                null,
                "Gym",
                new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero),
                RecurrenceRule: "FREQ=DAILY;COUNT=4")))
            .Content.ReadFromJsonAsync<CalendarEventDto>();
        Assert.NotNull(master);

        // Delete unedited 2nd occurrence (Nov 2)
        var del2 = await client.DeleteAsync(
            $"/api/events/{master.Id}?scope=single&occurrenceStartAt=2026-11-02T07:00:00Z");
        Assert.Equal(HttpStatusCode.NoContent, del2.StatusCode);

        // Edit 3rd occurrence (Nov 3) then delete that edited override as single occurrence
        var edited3 = await (await client.PutAsJsonAsync(
            $"/api/events/{master.Id}",
            new UpdateEventRequest(
                master.CalendarId,
                "Leg Day",
                new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero),
                Scope: "single",
                OccurrenceStartAt: new DateTimeOffset(2026, 11, 3, 7, 0, 0, TimeSpan.Zero))))
            .Content.ReadFromJsonAsync<CalendarEventDto>();
        Assert.NotNull(edited3);

        var del3 = await client.DeleteAsync($"/api/events/{edited3.Id}?scope=single");
        Assert.Equal(HttpStatusCode.NoContent, del3.StatusCode);

        // Only Nov 1 and Nov 4 should remain (Nov 3 must NOT resurrect!)
        var remaining = await client.GetFromJsonAsync<List<CalendarEventDto>>(
            "/api/events?from=2026-11-01T00:00:00Z&to=2026-11-10T00:00:00Z");
        Assert.NotNull(remaining);
        Assert.Equal([1, 4], remaining.Select(e => e.StartAt.Day).ToArray());
    }

    [Fact]
    public async Task RecurringEvent_DeleteWholeSeries_FromOverrideOrMaster_RemovesEntireSeries()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var master = await (await client.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(
                null,
                "Book Club",
                new DateTimeOffset(2026, 11, 5, 19, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 5, 20, 0, 0, TimeSpan.Zero),
                RecurrenceRule: "FREQ=WEEKLY;COUNT=4")))
            .Content.ReadFromJsonAsync<CalendarEventDto>();
        Assert.NotNull(master);

        // Create a single override on week 2
        var override2 = await (await client.PutAsJsonAsync(
            $"/api/events/{master.Id}",
            new UpdateEventRequest(
                master.CalendarId,
                "Book Club (Guest Author)",
                new DateTimeOffset(2026, 11, 12, 19, 30, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 12, 21, 0, 0, TimeSpan.Zero),
                Scope: "single",
                OccurrenceStartAt: new DateTimeOffset(2026, 11, 12, 19, 0, 0, TimeSpan.Zero))))
            .Content.ReadFromJsonAsync<CalendarEventDto>();
        Assert.NotNull(override2);

        // Delete the whole series using the override's ID with scope=series
        var delSeries = await client.DeleteAsync($"/api/events/{override2.Id}?scope=series");
        Assert.Equal(HttpStatusCode.NoContent, delSeries.StatusCode);

        var remaining = await client.GetFromJsonAsync<List<CalendarEventDto>>(
            "/api/events?from=2026-11-01T00:00:00Z&to=2026-12-31T00:00:00Z");
        Assert.NotNull(remaining);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task RecurringEvent_Validation_RejectsInvalidRRuleAndMissingOccurrenceStart()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var badRRule = await client.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(
                null,
                "Bad Rule",
                new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero),
                RecurrenceRule: "FREQ=NOT_A_FREQ"));
        Assert.Equal(HttpStatusCode.BadRequest, badRRule.StatusCode);

        var master = await (await client.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(
                null,
                "Good Rule",
                new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero),
                RecurrenceRule: "FREQ=DAILY;COUNT=3")))
            .Content.ReadFromJsonAsync<CalendarEventDto>();
        Assert.NotNull(master);

        var missingOccOnDelete = await client.DeleteAsync($"/api/events/{master.Id}?scope=single");
        Assert.Equal(HttpStatusCode.BadRequest, missingOccOnDelete.StatusCode);

        var missingOccOnUpdate = await client.PutAsJsonAsync(
            $"/api/events/{master.Id}",
            new UpdateEventRequest(
                master.CalendarId,
                "Edited",
                new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 2, 11, 0, 0, TimeSpan.Zero),
                Scope: "single"));
        Assert.Equal(HttpStatusCode.BadRequest, missingOccOnUpdate.StatusCode);
    }

    [Fact]
    public async Task RecurringEvent_ExportAndReimportIcs_PreservesRRuleExdateAndOverrides()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var personal = (await client.GetFromJsonAsync<List<CalendarDto>>("/api/calendars"))![0];
        var copyCal = await (await client.PostAsJsonAsync(
            "/api/calendars",
            new CreateCalendarRequest("Imported Copy", null, "#3b6ea5", "UTC")))
            .Content.ReadFromJsonAsync<CalendarDto>();
        Assert.NotNull(copyCal);

        var master = await (await client.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(
                personal.Id,
                "Sync",
                new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 1, 10, 30, 0, TimeSpan.Zero),
                RecurrenceRule: "FREQ=DAILY;COUNT=4")))
            .Content.ReadFromJsonAsync<CalendarEventDto>();
        Assert.NotNull(master);

        // Delete Nov 2, edit Nov 3
        await client.DeleteAsync($"/api/events/{master.Id}?scope=single&occurrenceStartAt=2026-11-02T10:00:00Z");
        await client.PutAsJsonAsync(
            $"/api/events/{master.Id}",
            new UpdateEventRequest(
                personal.Id,
                "Sync (Extended)",
                new DateTimeOffset(2026, 11, 3, 11, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero),
                Scope: "single",
                OccurrenceStartAt: new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero)));

        var exportRes = await client.GetAsync($"/api/calendars/{personal.Id}/export/ics");
        Assert.Equal(HttpStatusCode.OK, exportRes.StatusCode);
        var ics = await exportRes.Content.ReadAsStringAsync();
        Assert.Contains("RRULE:FREQ=DAILY;COUNT=4", ics, StringComparison.Ordinal);
        Assert.Contains("EXDATE:20261102T100000Z", ics, StringComparison.Ordinal);
        Assert.Contains("RECURRENCE-ID:20261103T100000Z", ics, StringComparison.Ordinal);

        // Re-import into copyCal and verify expanded occurrences match exactly
        using var content = new StringContent(ics, Encoding.UTF8, "text/calendar");
        var importRes = await client.PostAsync($"/api/calendars/{copyCal.Id}/import/ics", content);
        Assert.Equal(HttpStatusCode.OK, importRes.StatusCode);

        var importedEvents = await client.GetFromJsonAsync<List<CalendarEventDto>>(
            $"/api/events?calendarId={copyCal.Id}&from=2026-11-01T00:00:00Z&to=2026-11-10T00:00:00Z");
        Assert.NotNull(importedEvents);
        Assert.Equal(3, importedEvents.Count);
        Assert.Equal([1, 3, 4], importedEvents.Select(e => e.StartAt.Day).ToArray());
        Assert.Equal("Sync (Extended)", importedEvents[1].Title);
        Assert.Equal(11, importedEvents[1].StartAt.Hour);
    }
}
