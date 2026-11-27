using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Atrium.Api.Attachments;
using Atrium.Api.Auth;
using Atrium.Api.Calendar;
using Atrium.Api.Notes;
using Atrium.Api.University;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class LinkedNoteEndpointsTests
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
    public async Task OpenLinkedNote_UniversityItem_CreatesOnce_SeedsLegacyNotes_AndIsIdempotent()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var course = await (await client.PostAsJsonAsync(
            "/api/university/courses",
            new CreateCourseRequest("Algorithms", "CS301", "2026 Fall", null, null, null, "#c25438", null)))
            .Content.ReadFromJsonAsync<UniversityCourseDto>();
        Assert.NotNull(course);

        var activity = await (await client.PostAsJsonAsync(
            $"/api/university/courses/{course.Id}/activities",
            new CreateActivityRequest("lecture", "Lectures")))
            .Content.ReadFromJsonAsync<UniversityCourseActivityDto>();
        Assert.NotNull(activity);

        var item = await (await client.PostAsJsonAsync(
            $"/api/university/activities/{activity.Id}/items",
            new CreateActivityItemRequest("Lecture 1", "Sorting", "Review Big-O notation", 1)))
            .Content.ReadFromJsonAsync<UniversityActivityItemDto>();
        Assert.NotNull(item);

        // First call creates note and seeds legacy Notes text
        var firstRes = await client.PostAsJsonAsync(
            "/api/notes/linked",
            new OpenLinkedNoteRequest(NoteLinkKinds.UniversityItem, UniversityActivityItemId: item.Id));
        Assert.Equal(HttpStatusCode.Created, firstRes.StatusCode);
        var firstNote = await firstRes.Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(firstNote);
        Assert.Equal("Review Big-O notation", firstNote.Content);
        Assert.Equal(NoteLinkKinds.UniversityItem, firstNote.LinkKind);
        Assert.Equal(course.Id, firstNote.UniversityCourseId);
        Assert.Equal(item.Id, firstNote.UniversityActivityItemId);

        // Second call returns existing note (200 OK)
        var secondRes = await client.PostAsJsonAsync(
            "/api/notes/linked",
            new OpenLinkedNoteRequest(NoteLinkKinds.UniversityItem, UniversityActivityItemId: item.Id));
        Assert.Equal(HttpStatusCode.OK, secondRes.StatusCode);
        var secondNote = await secondRes.Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(secondNote);
        Assert.Equal(firstNote.Id, secondNote.Id);
    }

    [Fact]
    public async Task CourseFreeformNotes_AllowMultipleNotes_AndAppearUnderCourseInTree()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var course = await (await client.PostAsJsonAsync(
            "/api/university/courses",
            new CreateCourseRequest("Operating Systems", "CS401", "2026 Fall", null, null, null, "#3b6ea5", null)))
            .Content.ReadFromJsonAsync<UniversityCourseDto>();
        Assert.NotNull(course);

        var n1 = await (await client.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("Exam Prep", null, "Deadlocks", UniversityCourseId: course.Id)))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        var n2 = await (await client.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("Formula Sheet", null, "Page tables", UniversityCourseId: course.Id)))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(n1);
        Assert.NotNull(n2);

        using var form = new MultipartFormDataContent();
        var pdf = new ByteArrayContent("%PDF-1.4"u8.ToArray());
        pdf.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(pdf, "file", "syllabus.pdf");
        form.Add(new StringContent(AttachmentKinds.Artifact), "kind");
        form.Add(new StringContent(course.Id.ToString()), "universityCourseId");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/attachments", form)).StatusCode);

        var tree = await client.GetFromJsonAsync<NotesTreeResponse>("/api/notes/tree");
        Assert.NotNull(tree);
        Assert.Empty(tree.Notes); // Course notes must NOT appear in General
        Assert.Single(tree.University);
        Assert.Equal("2026 Fall", tree.University[0].Semester);
        var cNode = Assert.Single(tree.University[0].Courses);
        Assert.Equal(2, cNode.Notes.Count);
        Assert.Single(cNode.Artifacts);
        Assert.Equal("syllabus.pdf", cNode.Artifacts[0].FileName);
    }

    [Fact]
    public async Task CalendarEventNotes_RecurringOccurrencesAndSharedNote_AndAntiDuplication()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        // Create a recurring personal event and an empty personal event
        var start = new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);
        var recurring = await (await client.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(null, "Weekly Sync", start, start.AddHours(1), RecurrenceRule: "FREQ=WEEKLY;COUNT=4")))
            .Content.ReadFromJsonAsync<CalendarEventDto>();
        var emptyEvent = await (await client.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(null, "Dentist", start.AddDays(1), start.AddDays(1).AddHours(1))))
            .Content.ReadFromJsonAsync<CalendarEventDto>();
        Assert.NotNull(recurring);
        Assert.NotNull(emptyEvent);

        // Create a university item WITH a schedule (creates a uni calendar event)
        var course = await (await client.PostAsJsonAsync(
            "/api/university/courses",
            new CreateCourseRequest("Physics", "PH101", "2026 Fall", null, null, null, "#c25438", null)))
            .Content.ReadFromJsonAsync<UniversityCourseDto>();
        Assert.NotNull(course);
        var act = await (await client.PostAsJsonAsync(
            $"/api/university/courses/{course.Id}/activities",
            new CreateActivityRequest("lecture", "Lectures")))
            .Content.ReadFromJsonAsync<UniversityCourseActivityDto>();
        Assert.NotNull(act);
        var uniItem = await (await client.PostAsJsonAsync(
            $"/api/university/activities/{act.Id}/items",
            new CreateActivityItemRequest("Lecture 1", null, null, 1, null, new ActivityItemScheduleInput(start.AddHours(2), start.AddHours(3)))))
            .Content.ReadFromJsonAsync<UniversityActivityItemDto>();
        Assert.NotNull(uniItem);
        Assert.NotNull(uniItem.CalendarEventId);

        // University calendar event MUST be rejected as a calendar_event note
        var rejectedUni = await client.PostAsJsonAsync(
            "/api/notes/linked",
            new OpenLinkedNoteRequest(NoteLinkKinds.CalendarEvent, CalendarEventId: uniItem.CalendarEventId));
        Assert.Equal(HttpStatusCode.BadRequest, rejectedUni.StatusCode);

        // Open occurrence note + shared note on Weekly Sync
        var occ1Start = start;
        var occ2Start = start.AddDays(7);
        var occ1Note = await (await client.PostAsJsonAsync(
            "/api/notes/linked",
            new OpenLinkedNoteRequest(NoteLinkKinds.CalendarEvent, CalendarEventId: recurring.Id, OccurrenceStartAt: occ1Start)))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        var occ2Note = await (await client.PostAsJsonAsync(
            "/api/notes/linked",
            new OpenLinkedNoteRequest(NoteLinkKinds.CalendarEvent, CalendarEventId: recurring.Id, OccurrenceStartAt: occ2Start)))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        var sharedNote = await (await client.PostAsJsonAsync(
            "/api/notes/linked",
            new OpenLinkedNoteRequest(NoteLinkKinds.CalendarEvent, CalendarEventId: recurring.Id, OccurrenceStartAt: null)))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(occ1Note);
        Assert.NotNull(occ2Note);
        Assert.NotNull(sharedNote);
        Assert.NotEqual(occ1Note.Id, occ2Note.Id);
        Assert.NotEqual(occ1Note.Id, sharedNote.Id);

        var tree = await client.GetFromJsonAsync<NotesTreeResponse>("/api/notes/tree");
        Assert.NotNull(tree);
        // Only Weekly Sync appears in Events (not Dentist, not uni Lecture 1)
        var evtGroup = Assert.Single(tree.Events);
        Assert.Equal(recurring.Id, evtGroup.CalendarEventId);
        Assert.NotNull(evtGroup.SharedNote);
        Assert.Equal(2, evtGroup.Occurrences.Count);

        // Agent PAT with notes:read only sees empty university & events sections
        var pat = await (await client.PostAsJsonAsync(
            "/api/account/tokens",
            new CreateAgentTokenRequest("Notes Only", ["notes:read"])))
            .Content.ReadFromJsonAsync<CreateAgentTokenResponse>();
        Assert.NotNull(pat);

        using var patClient = factory.CreateClient();
        patClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pat.Secret);
        var patTree = await patClient.GetFromJsonAsync<NotesTreeResponse>("/api/notes/tree");
        Assert.NotNull(patTree);
        Assert.Empty(patTree.University);
        Assert.Empty(patTree.Events);

        // Deleting the recurring event preserves its notes in General Unfiled and clears Events tree
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/events/{recurring.Id}?scope=series")).StatusCode);
        var afterDeleteTree = await client.GetFromJsonAsync<NotesTreeResponse>("/api/notes/tree");
        Assert.NotNull(afterDeleteTree);
        Assert.Empty(afterDeleteTree.Events);
        Assert.Equal(3, afterDeleteTree.Notes.Count);
    }

    [Fact]
    public async Task SearchNotes_FindsLinkedNotesAcrossAllSections()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var course = await (await client.PostAsJsonAsync(
            "/api/university/courses",
            new CreateCourseRequest("Databases", "CS305", "2026 Fall", null, null, null, "#c25438", null)))
            .Content.ReadFromJsonAsync<UniversityCourseDto>();
        Assert.NotNull(course);

        await client.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("B-Trees", null, "UniqueNeedle in course notes", UniversityCourseId: course.Id));

        var results = await client.GetFromJsonAsync<List<NoteSummaryDto>>("/api/notes?q=UniqueNeedle&searchIn=content");
        Assert.NotNull(results);
        var found = Assert.Single(results);
        Assert.Equal(NoteLinkKinds.UniversityCourse, found.LinkKind);
        Assert.Contains("Databases", found.LinkLabel, StringComparison.Ordinal);
    }
}

