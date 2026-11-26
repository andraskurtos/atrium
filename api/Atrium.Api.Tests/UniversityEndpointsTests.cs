using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Atrium.Api.Auth;
using Atrium.Api.Calendar;
using Atrium.Api.University;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class UniversityEndpointsTests
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
    public async Task Workspace_AutoProvisionsDedicatedUniversityCalendar_AndProtectsFromDeletion()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var workspace = await client.GetFromJsonAsync<UniversityWorkspaceResponse>("/api/university/workspace");
        Assert.NotNull(workspace);
        Assert.Equal("University", workspace.Calendar.Name);
        Assert.Equal(UniversityConstants.CalendarModuleKey, workspace.Calendar.Module);
        Assert.False(workspace.Calendar.IsDefault);
        Assert.True(workspace.Calendar.IsVisible);
        Assert.Empty(workspace.Courses);
        Assert.Empty(workspace.Semesters);

        // Personal remains default calendar alongside University
        var calendars = await client.GetFromJsonAsync<List<CalendarDto>>("/api/calendars");
        Assert.NotNull(calendars);
        Assert.Equal(2, calendars.Count);
        Assert.Contains(calendars, c => c.Name == "Personal" && c.IsDefault && c.Module is null);
        Assert.Contains(calendars, c => c.Id == workspace.Calendar.Id && c.Module == "university");

        // Module-owned University calendar cannot be deleted directly
        var deleteRes = await client.DeleteAsync($"/api/calendars/{workspace.Calendar.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, deleteRes.StatusCode);

        // Visibility toggle still works for dashboard calendar integration
        var updateRes = await client.PutAsJsonAsync(
            $"/api/calendars/{workspace.Calendar.Id}",
            new UpdateCalendarRequest(
                workspace.Calendar.Name,
                workspace.Calendar.Description,
                workspace.Calendar.Color,
                workspace.Calendar.Timezone,
                IsVisible: false));
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        var updated = await updateRes.Content.ReadFromJsonAsync<CalendarDto>();
        Assert.NotNull(updated);
        Assert.False(updated.IsVisible);
    }

    [Fact]
    public async Task MinimalCourse_OptionalActivities_AndAllSevenActivityTypesWork()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        // Create a course with ONLY a name (all other fields optional)
        var createRes = await client.PostAsJsonAsync(
            "/api/university/courses",
            new CreateCourseRequest("Operating Systems"));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var course = await createRes.Content.ReadFromJsonAsync<UniversityCourseDto>();
        Assert.NotNull(course);
        Assert.Equal("Operating Systems", course.Name);
        Assert.Null(course.Code);
        Assert.Null(course.Semester);
        Assert.Null(course.Description);
        Assert.Null(course.Credits);
        Assert.Equal("active", course.Status);
        Assert.Empty(course.Activities);

        // Add all 7 supported activity types (with optional settings)
        foreach (var activityType in UniversityConstants.AllActivityTypes)
        {
            var actRes = await client.PostAsJsonAsync(
                $"/api/university/courses/{course.Id}/activities",
                new CreateActivityRequest(activityType));
            Assert.Equal(HttpStatusCode.Created, actRes.StatusCode);
        }

        // Attendance requirements on midterm / exam / semester_assignment must be rejected
        foreach (var nonAttendanceType in new[] { "semester_assignment", "midterm", "exam" })
        {
            var badRes = await client.PostAsJsonAsync(
                $"/api/university/courses/{course.Id}/activities",
                new CreateActivityRequest(nonAttendanceType, MinAttendanceCount: 1));
            Assert.Equal(HttpStatusCode.BadRequest, badRes.StatusCode);
        }

        var loaded = await client.GetFromJsonAsync<UniversityCourseDto>($"/api/university/courses/{course.Id}");
        Assert.NotNull(loaded);
        Assert.Equal(7, loaded.Activities.Count);
    }

    [Fact]
    public async Task Items_OptionalSchedule_ThemeSync_AttendanceTracking_AndCalendarDeletionEdgeCase()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var courseRes = await client.PostAsJsonAsync(
            "/api/university/courses",
            new CreateCourseRequest(
                Name: "Algorithms & Data Structures",
                Code: "CS-201",
                Semester: "2026/27 Fall",
                Description: "Core algorithms course",
                Instructor: "Dr. Kovacs",
                Credits: 5.0m,
                Color: "#2f6f55",
                Activities:
                [
                    new CreateActivityRequest(
                        ActivityType: "homework_assignment",
                        Title: "Weekly Homework",
                        MinAttendanceCount: 3,
                        PlannedTotalCount: 5),
                ]));
        Assert.Equal(HttpStatusCode.Created, courseRes.StatusCode);
        var course = await courseRes.Content.ReadFromJsonAsync<UniversityCourseDto>();
        Assert.NotNull(course);
        var hwActivity = Assert.Single(course.Activities);
        Assert.Equal(3, hwActivity.Attendance.RequiredCount);
        Assert.Equal(3, hwActivity.Attendance.RemainingRequired);
        Assert.Equal("on_track", hwActivity.Attendance.Status);

        // 1. Add unscheduled homework item (no calendar event, no attendance marked yet)
        var unscheduledRes = await client.PostAsJsonAsync(
            $"/api/university/activities/{hwActivity.Id}/items",
            new CreateActivityItemRequest(
                Title: "Homework 1",
                Theme: "Big-O notation and recurrences"));
        Assert.Equal(HttpStatusCode.Created, unscheduledRes.StatusCode);
        var hw1 = await unscheduledRes.Content.ReadFromJsonAsync<UniversityActivityItemDto>();
        Assert.NotNull(hw1);
        Assert.Null(hw1.CalendarEventId);
        Assert.Null(hw1.StartAt);
        Assert.Equal("Big-O notation and recurrences", hw1.Theme);
        Assert.Equal("pending", hw1.AttendanceStatus);

        // 2. Add scheduled homework item -> creates CalendarEvent in University calendar
        var start = new DateTimeOffset(2026, 10, 5, 16, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 10, 5, 17, 0, 0, TimeSpan.Zero);
        var scheduledRes = await client.PostAsJsonAsync(
            $"/api/university/activities/{hwActivity.Id}/items",
            new CreateActivityItemRequest(
                Title: "Homework 2",
                Theme: "Red-Black trees",
                Schedule: new ActivityItemScheduleInput(start, end, IsAllDay: false, Location: "Moodle")));
        Assert.Equal(HttpStatusCode.Created, scheduledRes.StatusCode);
        var hw2 = await scheduledRes.Content.ReadFromJsonAsync<UniversityActivityItemDto>();
        Assert.NotNull(hw2);
        Assert.NotNull(hw2.CalendarEventId);
        Assert.Equal(start, hw2.StartAt);
        Assert.Equal("Moodle", hw2.Location);

        // Verify CalendarEvent exists in University calendar with synced title & theme
        var workspace = await client.GetFromJsonAsync<UniversityWorkspaceResponse>("/api/university/workspace");
        Assert.NotNull(workspace);
        var calEvents = await client.GetFromJsonAsync<List<CalendarEventDto>>(
            $"/api/events?calendarId={workspace.Calendar.Id}&from=2026-10-01T00:00:00Z&to=2026-10-31T00:00:00Z");
        Assert.NotNull(calEvents);
        var calEvt = Assert.Single(calEvents);
        Assert.Equal(hw2.CalendarEventId, calEvt.Id);
        Assert.Equal("CS-201 · Homework 2", calEvt.Title);
        Assert.Contains("Red-Black trees", calEvt.Description ?? string.Empty, StringComparison.Ordinal);

        // 3. Quick attendance update via PATCH
        var patchRes = await client.PatchAsJsonAsync(
            $"/api/university/items/{hw1.Id}/attendance",
            new UpdateAttendanceStatusRequest("attended"));
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);
        var patchPayload = await patchRes.Content.ReadFromJsonAsync<UpdateAttendanceStatusResponse>();
        Assert.NotNull(patchPayload);
        Assert.Equal("attended", patchPayload.Item.AttendanceStatus);
        Assert.Equal(1, patchPayload.Attendance.AttendedCount);
        Assert.Equal(2, patchPayload.Attendance.RemainingRequired);

        // 4. Updating item theme without rescheduling preserves calendar event & syncs theme
        var updateItemRes = await client.PutAsJsonAsync(
            $"/api/university/items/{hw2.Id}",
            new UpdateActivityItemRequest(
                Title: "Homework 2 (Revised)",
                Theme: "Red-Black & AVL trees",
                AttendanceStatus: "attended"));
        Assert.Equal(HttpStatusCode.OK, updateItemRes.StatusCode);
        var updatedHw2 = await updateItemRes.Content.ReadFromJsonAsync<UniversityActivityItemDto>();
        Assert.NotNull(updatedHw2);
        Assert.Equal(hw2.CalendarEventId, updatedHw2.CalendarEventId);
        Assert.Equal("attended", updatedHw2.AttendanceStatus);

        // 5. Deleting the linked CalendarEvent from /api/events gracefully unlinks the item without losing attendance/theme
        var delCalEvtRes = await client.DeleteAsync($"/api/events/{hw2.CalendarEventId}");
        Assert.Equal(HttpStatusCode.NoContent, delCalEvtRes.StatusCode);

        var reloadedCourse = await client.GetFromJsonAsync<UniversityCourseDto>($"/api/university/courses/{course.Id}");
        Assert.NotNull(reloadedCourse);
        var reloadedHw2 = reloadedCourse.Activities[0].Items.Single(i => i.Id == hw2.Id);
        Assert.Null(reloadedHw2.CalendarEventId);
        Assert.Equal("Red-Black & AVL trees", reloadedHw2.Theme);
        Assert.Equal("attended", reloadedHw2.AttendanceStatus);
    }

    [Fact]
    public async Task GenerateWeeklySeries_CreatesIndividualLinkedItems_AndDeleteCourseCleansUpEvents()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var courseRes = await client.PostAsJsonAsync(
            "/api/university/courses",
            new CreateCourseRequest(
                Name: "Linear Algebra",
                Code: "MATH-101",
                Semester: "2026/27 Fall",
                Activities:
                [
                    new CreateActivityRequest(
                        ActivityType: "lecture",
                        DefaultLocation: "Building K, Room 204",
                        MinAttendancePercent: 75m),
                ]));
        var course = await courseRes.Content.ReadFromJsonAsync<UniversityCourseDto>();
        Assert.NotNull(course);
        var lectureActivity = Assert.Single(course.Activities);

        // Before series: percentage only, 0 items -> no_data
        Assert.Equal("no_data", lectureActivity.Attendance.Status);

        // Generate 4 weekly lectures
        var firstStart = new DateTimeOffset(2026, 9, 7, 8, 15, 0, TimeSpan.Zero);
        var firstEnd = new DateTimeOffset(2026, 9, 7, 9, 45, 0, TimeSpan.Zero);
        var seriesRes = await client.PostAsJsonAsync(
            $"/api/university/activities/{lectureActivity.Id}/items/generate-series",
            new GenerateActivitySeriesRequest(
                StartAt: firstStart,
                EndAt: firstEnd,
                Count: 4,
                IntervalWeeks: 1,
                TitlePrefix: "Lecture",
                StartNumber: 1));
        Assert.Equal(HttpStatusCode.Created, seriesRes.StatusCode);
        var series = await seriesRes.Content.ReadFromJsonAsync<GenerateActivitySeriesResponse>();
        Assert.NotNull(series);
        Assert.Equal(4, series.CreatedItems.Count);
        Assert.Equal(3, series.Attendance.RequiredCount); // 75% of 4 = 3
        Assert.Equal(3, series.Attendance.RemainingRequired);
        Assert.Equal("Building K, Room 204", series.CreatedItems[0].Location);
        Assert.Equal(firstStart.AddDays(21), series.CreatedItems[3].StartAt);

        // Give Week 2 its own unique theme
        var week2 = series.CreatedItems[1];
        var editWeek2Res = await client.PutAsJsonAsync(
            $"/api/university/items/{week2.Id}",
            new UpdateActivityItemRequest(
                Title: week2.Title,
                Theme: "Eigenvalues and eigenvectors",
                SequenceNumber: 2));
        Assert.Equal(HttpStatusCode.OK, editWeek2Res.StatusCode);

        // Verify all 4 events exist in University calendar
        var workspace = await client.GetFromJsonAsync<UniversityWorkspaceResponse>("/api/university/workspace");
        Assert.NotNull(workspace);
        var eventsBeforeDelete = await client.GetFromJsonAsync<List<CalendarEventDto>>(
            $"/api/events?calendarId={workspace.Calendar.Id}&from=2026-09-01T00:00:00Z&to=2026-10-31T00:00:00Z");
        Assert.NotNull(eventsBeforeDelete);
        Assert.Equal(4, eventsBeforeDelete.Count);

        // Delete the course -> all 4 linked calendar events are cleaned up
        var delCourseRes = await client.DeleteAsync($"/api/university/courses/{course.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delCourseRes.StatusCode);

        var eventsAfterDelete = await client.GetFromJsonAsync<List<CalendarEventDto>>(
            $"/api/events?calendarId={workspace.Calendar.Id}&from=2026-09-01T00:00:00Z&to=2026-10-31T00:00:00Z");
        Assert.NotNull(eventsAfterDelete);
        Assert.Empty(eventsAfterDelete);
    }

    [Fact]
    public async Task SemesterFiltering_UserIsolation_AndPatScopesWork()
    {
        using var factory = new TestAppFactory();
        using var ownerClient = factory.CreateClient();
        using var memberClient = factory.CreateClient();
        await SetupOwnerAsync(ownerClient);
        await CreateInvitedMemberAsync(ownerClient, memberClient);

        // Create courses in different semesters + unassigned
        var c1Res = await ownerClient.PostAsJsonAsync(
            "/api/university/courses",
            new CreateCourseRequest("Databases", Semester: "2026/27 Fall"));
        var c1 = await c1Res.Content.ReadFromJsonAsync<UniversityCourseDto>();
        Assert.NotNull(c1);

        await ownerClient.PostAsJsonAsync(
            "/api/university/courses",
            new CreateCourseRequest("Thesis Prep", Semester: null));

        var fallWorkspace = await ownerClient.GetFromJsonAsync<UniversityWorkspaceResponse>(
            "/api/university/workspace?semester=2026%2F27%20Fall");
        Assert.NotNull(fallWorkspace);
        Assert.Single(fallWorkspace.Courses);
        Assert.Equal("Databases", fallWorkspace.Courses[0].Name);
        Assert.Contains("2026/27 Fall", fallWorkspace.Semesters);

        var unassignedWorkspace = await ownerClient.GetFromJsonAsync<UniversityWorkspaceResponse>(
            "/api/university/workspace?semester=__unassigned__");
        Assert.NotNull(unassignedWorkspace);
        Assert.Single(unassignedWorkspace.Courses);
        Assert.Equal("Thesis Prep", unassignedWorkspace.Courses[0].Name);

        // Member cannot see or delete Owner's course
        var memberGet = await memberClient.GetAsync($"/api/university/courses/{c1.Id}");
        Assert.Equal(HttpStatusCode.NotFound, memberGet.StatusCode);

        var memberDel = await memberClient.DeleteAsync($"/api/university/courses/{c1.Id}");
        Assert.Equal(HttpStatusCode.NotFound, memberDel.StatusCode);

        // PAT with university:read can read workspace, cannot mutate
        var patReadRes = await ownerClient.PostAsJsonAsync(
            "/api/account/tokens",
            new CreateAgentTokenRequest("Uni Agent", ["university:read"]));
        Assert.Equal(HttpStatusCode.Created, patReadRes.StatusCode);
        var patRead = await patReadRes.Content.ReadFromJsonAsync<CreateAgentTokenResponse>();
        Assert.NotNull(patRead);

        using var patReadClient = factory.CreateClient();
        patReadClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", patRead.Secret);
        var patWorkspaceRes = await patReadClient.GetAsync("/api/university/workspace");
        Assert.Equal(HttpStatusCode.OK, patWorkspaceRes.StatusCode);

        var patWriteRes = await patReadClient.PostAsJsonAsync(
            "/api/university/courses",
            new CreateCourseRequest("Blocked"));
        Assert.Equal(HttpStatusCode.Unauthorized, patWriteRes.StatusCode);

        // PAT without university:read gets 403
        var patNoScopeRes = await ownerClient.PostAsJsonAsync(
            "/api/account/tokens",
            new CreateAgentTokenRequest("Vault Agent", ["vault:read"]));
        var patNoScope = await patNoScopeRes.Content.ReadFromJsonAsync<CreateAgentTokenResponse>();
        Assert.NotNull(patNoScope);

        using var patNoScopeClient = factory.CreateClient();
        patNoScopeClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", patNoScope.Secret);
        var forbiddenRes = await patNoScopeClient.GetAsync("/api/university/workspace");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRes.StatusCode);
    }
}
