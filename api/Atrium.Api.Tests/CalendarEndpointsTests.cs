using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Atrium.Api.Auth;
using Atrium.Api.Calendar;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class CalendarEndpointsTests
{
    private const string OwnerPassword = "Atrium-Owner-Pass-2026!";
    private const string MemberPassword = "Atrium-Member-Pass-2026!";

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

    private static async Task<AuthSessionResponse> CreateInvitedMemberAsync(
        HttpClient ownerClient,
        HttpClient memberClient,
        string username = "maya")
    {
        var inviteRes = await ownerClient.PostAsJsonAsync(
            "/api/account/invites",
            new CreateInviteRequest("Family", 7));
        Assert.Equal(HttpStatusCode.Created, inviteRes.StatusCode);
        var invite = await inviteRes.Content.ReadFromJsonAsync<CreateInviteResponse>();
        Assert.NotNull(invite);

        var regRes = await memberClient.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterWithInviteRequest(
                invite.Code,
                username,
                $"{username}@atrium.local",
                "Maya",
                MemberPassword));
        Assert.Equal(HttpStatusCode.OK, regRes.StatusCode);
        var session = await regRes.Content.ReadFromJsonAsync<AuthSessionResponse>();
        Assert.NotNull(session);
        memberClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return session;
    }

    [Fact]
    public async Task Calendars_DefaultCreatedAutomatically_AndMultipleCalendarsLifecycleWorks()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        // User gets a default Personal calendar automatically
        var initialCalendars = await client.GetFromJsonAsync<List<CalendarDto>>("/api/calendars");
        Assert.NotNull(initialCalendars);
        var personal = Assert.Single(initialCalendars);
        Assert.Equal("Personal", personal.Name);
        Assert.True(personal.IsDefault);
        Assert.True(personal.IsVisible);

        // Cannot delete the last remaining calendar
        var deleteOnlyRes = await client.DeleteAsync($"/api/calendars/{personal.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, deleteOnlyRes.StatusCode);

        // Create second and third calendars
        var workRes = await client.PostAsJsonAsync(
            "/api/calendars",
            new CreateCalendarRequest("Work", "Meetings and sprints", "#2f6f55", "Europe/Budapest"));
        Assert.Equal(HttpStatusCode.Created, workRes.StatusCode);
        var work = await workRes.Content.ReadFromJsonAsync<CalendarDto>();
        Assert.NotNull(work);
        Assert.Equal("Work", work.Name);
        Assert.Equal("#2f6f55", work.Color);
        Assert.Equal("Europe/Budapest", work.Timezone);
        Assert.False(work.IsDefault);

        var familyRes = await client.PostAsJsonAsync(
            "/api/calendars",
            new CreateCalendarRequest("Family", null, "#3b6ea5", "UTC", IsDefault: true, IsVisible: true));
        Assert.Equal(HttpStatusCode.Created, familyRes.StatusCode);
        var family = await familyRes.Content.ReadFromJsonAsync<CalendarDto>();
        Assert.NotNull(family);
        Assert.True(family.IsDefault);

        // Verify only Family is default now
        var all = await client.GetFromJsonAsync<List<CalendarDto>>("/api/calendars");
        Assert.NotNull(all);
        Assert.Equal(3, all.Count);
        Assert.Single(all, c => c.IsDefault);
        Assert.Equal(family.Id, all.Single(c => c.IsDefault).Id);

        // Toggle visibility and rename Work
        var updateRes = await client.PutAsJsonAsync(
            $"/api/calendars/{work.Id}",
            new UpdateCalendarRequest("Engineering", "Updated desc", "#8b5cf6", "UTC", IsDefault: null, IsVisible: false));
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        var updatedWork = await updateRes.Content.ReadFromJsonAsync<CalendarDto>();
        Assert.NotNull(updatedWork);
        Assert.Equal("Engineering", updatedWork.Name);
        Assert.False(updatedWork.IsVisible);

        // Deleting current default calendar promotes another calendar to default
        var deleteDefaultRes = await client.DeleteAsync($"/api/calendars/{family.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteDefaultRes.StatusCode);

        var remaining = await client.GetFromJsonAsync<List<CalendarDto>>("/api/calendars");
        Assert.NotNull(remaining);
        Assert.Equal(2, remaining.Count);
        Assert.Single(remaining, c => c.IsDefault);
    }

    [Fact]
    public async Task Events_AllowOverlappingEvents_WithinSameCalendarAndAcrossMultipleCalendars()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var calendars = await client.GetFromJsonAsync<List<CalendarDto>>("/api/calendars");
        Assert.NotNull(calendars);
        var personal = calendars[0];

        var workRes = await client.PostAsJsonAsync(
            "/api/calendars",
            new CreateCalendarRequest("Work", null, "#2f6f55", "UTC"));
        var work = await workRes.Content.ReadFromJsonAsync<CalendarDto>();
        Assert.NotNull(work);

        // Create two overlapping events in Work and one overlapping event in Personal
        var e1Res = await client.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(
                work.Id,
                "Sprint Planning",
                new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 26, 10, 30, 0, TimeSpan.Zero),
                Location: "Conf Room A"));
        Assert.Equal(HttpStatusCode.Created, e1Res.StatusCode);

        var e2Res = await client.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(
                work.Id,
                "Urgent Incident Triage",
                new DateTimeOffset(2026, 11, 26, 9, 30, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 26, 11, 0, 0, TimeSpan.Zero),
                Location: "War Room"));
        Assert.Equal(HttpStatusCode.Created, e2Res.StatusCode);

        var e3Res = await client.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(
                personal.Id,
                "Doctor Callback",
                new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 26, 10, 45, 0, TimeSpan.Zero),
                Location: "Phone"));
        Assert.Equal(HttpStatusCode.Created, e3Res.StatusCode);

        var events = await client.GetFromJsonAsync<List<CalendarEventDto>>(
            "/api/events?from=2026-11-26T00:00:00Z&to=2026-11-27T00:00:00Z");
        Assert.NotNull(events);
        Assert.Equal(3, events.Count);

        Assert.Equal("Sprint Planning", events[0].Title);
        Assert.Equal("Work", events[0].CalendarName);
        Assert.Equal("#2f6f55", events[0].CalendarColor);

        Assert.Equal("Urgent Incident Triage", events[1].Title);
        Assert.Equal("Work", events[1].CalendarName);

        Assert.Equal("Doctor Callback", events[2].Title);
        Assert.Equal("Personal", events[2].CalendarName);
    }

    [Fact]
    public async Task Events_RangeQueryCalendarFilterAndVisibilityFilter_ReturnExpectedSubset()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var personal = (await client.GetFromJsonAsync<List<CalendarDto>>("/api/calendars"))![0];
        var hiddenCal = await (await client.PostAsJsonAsync(
            "/api/calendars",
            new CreateCalendarRequest("Birthdays", null, "#b4691e", "UTC", IsVisible: false)))
            .Content.ReadFromJsonAsync<CalendarDto>();
        Assert.NotNull(hiddenCal);

        // Event completely before window
        await client.PostAsJsonAsync("/api/events", new CreateEventRequest(
            personal.Id, "Before",
            new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero)));

        // Event starting before window and ending inside window (must be included!)
        await client.PostAsJsonAsync("/api/events", new CreateEventRequest(
            personal.Id, "Spans Start",
            new DateTimeOffset(2026, 11, 25, 23, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 26, 1, 0, 0, TimeSpan.Zero)));

        // Zero-duration milestone inside window (must be included!)
        await client.PostAsJsonAsync("/api/events", new CreateEventRequest(
            personal.Id, "Release Tag",
            new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero)));

        // Event in hidden calendar inside window
        await client.PostAsJsonAsync("/api/events", new CreateEventRequest(
            hiddenCal.Id, "Sam's Birthday",
            new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero),
            IsAllDay: true));

        // Event completely after window
        await client.PostAsJsonAsync("/api/events", new CreateEventRequest(
            personal.Id, "After",
            new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 28, 11, 0, 0, TimeSpan.Zero)));

        var allInWindow = await client.GetFromJsonAsync<List<CalendarEventDto>>(
            "/api/events?from=2026-11-26T00:00:00Z&to=2026-11-27T00:00:00Z");
        Assert.NotNull(allInWindow);
        Assert.Equal(3, allInWindow.Count);
        Assert.Contains(allInWindow, e => e.Title == "Spans Start");
        Assert.Contains(allInWindow, e => e.Title == "Release Tag");
        Assert.Contains(allInWindow, e => e.Title == "Sam's Birthday");

        var visibleOnly = await client.GetFromJsonAsync<List<CalendarEventDto>>(
            "/api/events?from=2026-11-26T00:00:00Z&to=2026-11-27T00:00:00Z&visibleOnly=true");
        Assert.NotNull(visibleOnly);
        Assert.Equal(2, visibleOnly.Count);
        Assert.DoesNotContain(visibleOnly, e => e.Title == "Sam's Birthday");

        var filteredByCal = await client.GetFromJsonAsync<List<CalendarEventDto>>(
            $"/api/events?from=2026-11-26T00:00:00Z&to=2026-11-27T00:00:00Z&calendarId={hiddenCal.Id}");
        Assert.NotNull(filteredByCal);
        var bday = Assert.Single(filteredByCal);
        Assert.Equal("Sam's Birthday", bday.Title);
    }

    [Fact]
    public async Task Events_CrudLifecycle_TracksLocationDescriptionAndMovesBetweenCalendars()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var work = await (await client.PostAsJsonAsync(
            "/api/calendars",
            new CreateCalendarRequest("Work", null, "#2f6f55", "UTC")))
            .Content.ReadFromJsonAsync<CalendarDto>();
        Assert.NotNull(work);

        // Omit CalendarId -> defaults to user's default calendar
        var createRes = await client.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(
                null,
                "Coffee with Alex",
                new DateTimeOffset(2026, 11, 26, 15, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 26, 15, 45, 0, TimeSpan.Zero),
                Location: "Roastery Downtown",
                Description: "Catch up on Q4 plans"));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<CalendarEventDto>();
        Assert.NotNull(created);
        Assert.Equal("Personal", created.CalendarName);
        Assert.Equal("Roastery Downtown", created.Location);
        Assert.Equal("Catch up on Q4 plans", created.Description);

        // Update event details and move to Work calendar
        var updateRes = await client.PutAsJsonAsync(
            $"/api/events/{created.Id}",
            new UpdateEventRequest(
                work.Id,
                "Project Sync with Alex",
                new DateTimeOffset(2026, 11, 26, 16, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 26, 17, 0, 0, TimeSpan.Zero),
                IsAllDay: false,
                Location: "Office Room 2",
                Description: "Updated agenda",
                Timezone: "Europe/Budapest",
                Status: "tentative"));
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        var updated = await updateRes.Content.ReadFromJsonAsync<CalendarEventDto>();
        Assert.NotNull(updated);
        Assert.Equal(work.Id, updated.CalendarId);
        Assert.Equal("Work", updated.CalendarName);
        Assert.Equal("Project Sync with Alex", updated.Title);
        Assert.Equal("Office Room 2", updated.Location);
        Assert.Equal("tentative", updated.Status);

        // Fetch by ID
        var fetched = await client.GetFromJsonAsync<CalendarEventDto>($"/api/events/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal("Project Sync with Alex", fetched.Title);

        // Delete
        var delRes = await client.DeleteAsync($"/api/events/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        var afterDel = await client.GetAsync($"/api/events/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDel.StatusCode);
    }

    [Fact]
    public async Task Events_Validation_RejectsBlankTitleAndInvertedRange()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var blankTitle = await client.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(
                null,
                "   ",
                new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 26, 11, 0, 0, TimeSpan.Zero)));
        Assert.Equal(HttpStatusCode.BadRequest, blankTitle.StatusCode);

        var inverted = await client.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(
                null,
                "Backwards",
                new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 26, 11, 0, 0, TimeSpan.Zero)));
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);

        var missingCal = await client.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(
                Guid.NewGuid(),
                "Orphan",
                new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 26, 13, 0, 0, TimeSpan.Zero)));
        Assert.Equal(HttpStatusCode.NotFound, missingCal.StatusCode);
    }

    [Fact]
    public async Task CalendarsAndEvents_EnforceStrictUserIsolation()
    {
        using var factory = new TestAppFactory();
        using var ownerClient = factory.CreateClient();
        using var memberClient = factory.CreateClient();

        await SetupOwnerAsync(ownerClient);
        await CreateInvitedMemberAsync(ownerClient, memberClient);

        var ownerCalendars = await ownerClient.GetFromJsonAsync<List<CalendarDto>>("/api/calendars");
        var ownerCal = ownerCalendars![0];

        var ownerEvent = await (await ownerClient.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(
                ownerCal.Id,
                "Owner Private Event",
                new DateTimeOffset(2026, 11, 26, 14, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 26, 15, 0, 0, TimeSpan.Zero))))
            .Content.ReadFromJsonAsync<CalendarEventDto>();
        Assert.NotNull(ownerEvent);

        // Member cannot see owner's calendar or events
        var memberEvents = await memberClient.GetFromJsonAsync<List<CalendarEventDto>>("/api/events");
        Assert.NotNull(memberEvents);
        Assert.Empty(memberEvents);

        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.GetAsync($"/api/calendars/{ownerCal.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.GetAsync($"/api/events/{ownerEvent.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.DeleteAsync($"/api/events/{ownerEvent.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.DeleteAsync($"/api/calendars/{ownerCal.Id}")).StatusCode);
    }

    [Fact]
    public async Task AgentPat_AllowsCalendarReadWithScope_BlocksWithoutScope_AndBlocksMutations()
    {
        using var factory = new TestAppFactory();
        using var ownerClient = factory.CreateClient();
        await SetupOwnerAsync(ownerClient);

        var cal = (await ownerClient.GetFromJsonAsync<List<CalendarDto>>("/api/calendars"))![0];
        await ownerClient.PostAsJsonAsync("/api/events", new CreateEventRequest(
            cal.Id,
            "Readable Event",
            new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.Zero)));

        var calPat = await (await ownerClient.PostAsJsonAsync(
            "/api/account/tokens",
            new CreateAgentTokenRequest("Cal Reader", ["calendar:read"])))
            .Content.ReadFromJsonAsync<CreateAgentTokenResponse>();
        Assert.NotNull(calPat);

        var vaultPat = await (await ownerClient.PostAsJsonAsync(
            "/api/account/tokens",
            new CreateAgentTokenRequest("Vault Only", ["vault:read"])))
            .Content.ReadFromJsonAsync<CreateAgentTokenResponse>();
        Assert.NotNull(vaultPat);

        using var calAgentClient = factory.CreateClient();
        calAgentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", calPat.Secret);

        // Allowed to read calendars and events
        var agentCals = await calAgentClient.GetAsync("/api/calendars");
        Assert.Equal(HttpStatusCode.OK, agentCals.StatusCode);
        var agentEvents = await calAgentClient.GetFromJsonAsync<List<CalendarEventDto>>("/api/events");
        Assert.NotNull(agentEvents);
        Assert.Single(agentEvents);

        // Forbidden from mutating calendars/events
        var mutateRes = await calAgentClient.PostAsJsonAsync("/api/events", new CreateEventRequest(
            cal.Id,
            "Unauthorized Write",
            new DateTimeOffset(2026, 11, 26, 11, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero)));
        Assert.True(mutateRes.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);

        // Vault-only PAT gets 403 on calendar reads
        using var vaultAgentClient = factory.CreateClient();
        vaultAgentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", vaultPat.Secret);
        var forbiddenRead = await vaultAgentClient.GetAsync("/api/events");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRead.StatusCode);
    }

    [Fact]
    public async Task ImportIcs_IntoExistingCalendar_ImportsAndUpsertsByUidWithoutDuplicates()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var cal = (await client.GetFromJsonAsync<List<CalendarDto>>("/api/calendars"))![0];

        const string firstIcs = """
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:-//Google Inc//Google Calendar 70.9054//EN
            BEGIN:VEVENT
            UID:google-evt-1@google.com
            DTSTART:20261205T140000Z
            DTEND:20261205T150000Z
            SUMMARY:Product Demo
            LOCATION:Zoom Link
            DESCRIPTION:Initial demo notes
            END:VEVENT
            BEGIN:VEVENT
            UID:google-evt-2@google.com
            DTSTART;VALUE=DATE:20261206
            SUMMARY:Company Offsite
            END:VEVENT
            END:VCALENDAR
            """;

        using var form1 = new MultipartFormDataContent();
        var fileContent1 = new ByteArrayContent(Encoding.UTF8.GetBytes(firstIcs));
        fileContent1.Headers.ContentType = new MediaTypeHeaderValue("text/calendar");
        form1.Add(fileContent1, "file", "google-export.ics");

        var importRes1 = await client.PostAsync($"/api/calendars/{cal.Id}/import/ics", form1);
        Assert.Equal(HttpStatusCode.OK, importRes1.StatusCode);
        var summary1 = await importRes1.Content.ReadFromJsonAsync<IcsImportResponse>();
        Assert.NotNull(summary1);
        Assert.Equal(2, summary1.ImportedCount);
        Assert.Equal(0, summary1.UpdatedCount);
        Assert.Equal(0, summary1.SkippedCount);

        // Re-import with updated first event + one brand new event
        const string secondIcs = """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VEVENT
            UID:google-evt-1@google.com
            DTSTART:20261205T143000Z
            DTEND:20261205T153000Z
            SUMMARY:Product Demo (Rescheduled)
            LOCATION:Main Auditorium
            DESCRIPTION:Updated demo notes
            END:VEVENT
            BEGIN:VEVENT
            UID:google-evt-3@google.com
            DTSTART:20261205T150000Z
            DTEND:20261205T160000Z
            SUMMARY:Overlapping Debrief
            END:VEVENT
            END:VCALENDAR
            """;

        using var form2 = new MultipartFormDataContent();
        var fileContent2 = new ByteArrayContent(Encoding.UTF8.GetBytes(secondIcs));
        fileContent2.Headers.ContentType = new MediaTypeHeaderValue("text/calendar");
        form2.Add(fileContent2, "file", "google-export-v2.ics");

        var importRes2 = await client.PostAsync($"/api/calendars/{cal.Id}/import/ics", form2);
        Assert.Equal(HttpStatusCode.OK, importRes2.StatusCode);
        var summary2 = await importRes2.Content.ReadFromJsonAsync<IcsImportResponse>();
        Assert.NotNull(summary2);
        Assert.Equal(1, summary2.ImportedCount);
        Assert.Equal(1, summary2.UpdatedCount);

        var allEvents = await client.GetFromJsonAsync<List<CalendarEventDto>>("/api/events");
        Assert.NotNull(allEvents);
        Assert.Equal(3, allEvents.Count);
        var rescheduled = Assert.Single(allEvents, e => e.ExternalUid == "google-evt-1@google.com");
        Assert.Equal("Product Demo (Rescheduled)", rescheduled.Title);
        Assert.Equal("Main Auditorium", rescheduled.Location);
    }

    [Fact]
    public async Task ImportIcs_AsNewCalendar_AndExportRoundTrip_WorksAcrossCalendars()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            X-WR-CALNAME:Team Releases
            X-WR-CALDESC:Production rollout dates
            X-WR-TIMEZONE:UTC
            BEGIN:VEVENT
            UID:rel-1@atrium.local
            DTSTART:20261210T100000Z
            DTEND:20261210T110000Z
            SUMMARY:v1.0 Launch Window
            LOCATION:Ops Bridge
            END:VEVENT
            BEGIN:VEVENT
            UID:rel-2@atrium.local
            DTSTART:20261210T103000Z
            DTEND:20261210T113000Z
            SUMMARY:Press Announcement
            END:VEVENT
            END:VCALENDAR
            """;

        // Test importing via raw text/calendar body into a brand new calendar
        using var rawContent = new StringContent(ics, Encoding.UTF8, "text/calendar");
        var importRes = await client.PostAsync("/api/calendars/import/ics", rawContent);
        Assert.Equal(HttpStatusCode.Created, importRes.StatusCode);
        var result = await importRes.Content.ReadFromJsonAsync<IcsImportResponse>();
        Assert.NotNull(result);
        Assert.Equal("Team Releases", result.Calendar.Name);
        Assert.Equal(2, result.ImportedCount);

        // Export the newly created calendar as .ics
        var exportRes = await client.GetAsync($"/api/calendars/{result.Calendar.Id}/export/ics");
        Assert.Equal(HttpStatusCode.OK, exportRes.StatusCode);
        Assert.Equal("text/calendar", exportRes.Content.Headers.ContentType?.MediaType);
        var exportedIcs = await exportRes.Content.ReadAsStringAsync();
        Assert.Contains("BEGIN:VCALENDAR", exportedIcs, StringComparison.Ordinal);
        Assert.Contains("v1.0 Launch Window", exportedIcs, StringComparison.Ordinal);
        Assert.Contains("Ops Bridge", exportedIcs, StringComparison.Ordinal);

        // Re-import the exported .ics into Personal calendar -> same UIDs allowed in different calendars!
        var personal = (await client.GetFromJsonAsync<List<CalendarDto>>("/api/calendars"))!
            .Single(c => c.Name == "Personal");
        using var reimportContent = new StringContent(exportedIcs, Encoding.UTF8, "text/calendar");
        var reimportRes = await client.PostAsync($"/api/calendars/{personal.Id}/import/ics", reimportContent);
        Assert.Equal(HttpStatusCode.OK, reimportRes.StatusCode);
        var reimportSummary = await reimportRes.Content.ReadFromJsonAsync<IcsImportResponse>();
        Assert.NotNull(reimportSummary);
        Assert.Equal(2, reimportSummary.ImportedCount);

        // Total events across both calendars = 4 (2 in Team Releases + 2 overlapping copies in Personal)
        var allEvents = await client.GetFromJsonAsync<List<CalendarEventDto>>("/api/events");
        Assert.NotNull(allEvents);
        Assert.Equal(4, allEvents.Count);
    }
}
