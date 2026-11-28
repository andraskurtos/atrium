using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Atrium.Api.Attachments;
using Atrium.Api.Auth;
using Atrium.Api.Calendar;
using Atrium.Api.Days;
using Atrium.Api.Knowledge;
using Atrium.Api.Notes;
using Atrium.Api.University;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class KnowledgeAndMcpTests
{
    private const string OwnerPassword = "Atrium-Owner-Pass-2026!";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    private static async Task<string> CreatePatAsync(HttpClient client, string name, params string[] scopes)
    {
        var res = await client.PostAsJsonAsync(
            "/api/account/tokens",
            new CreateAgentTokenRequest(name, scopes));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var created = await res.Content.ReadFromJsonAsync<CreateAgentTokenResponse>();
        Assert.NotNull(created);
        return created.Secret;
    }

    private static async Task<HttpResponseMessage> PostMcpAsync(HttpClient client, string bearerToken, object jsonRpcPayload)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        req.Content = new StringContent(JsonSerializer.Serialize(jsonRpcPayload, JsonOptions), Encoding.UTF8, "application/json");
        return await client.SendAsync(req);
    }

    private static async Task<JsonDocument> ReadMcpJsonRpcAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            var dataLine = raw
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(line => line.StartsWith("data:", StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(dataLine), $"Expected SSE data line in response:\n{raw}");
            var json = dataLine!["data:".Length..].Trim();
            return JsonDocument.Parse(json);
        }

        return JsonDocument.Parse(raw);
    }

    private static async Task<(bool IsError, string Text)> CallMcpToolAsync(
        HttpClient client,
        string pat,
        string toolName,
        object arguments)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments,
            },
        };

        using var res = await PostMcpAsync(client, pat, payload);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var doc = await ReadMcpJsonRpcAsync(res);
        var result = doc.RootElement.GetProperty("result");
        var isError = result.TryGetProperty("isError", out var errProp) && errProp.ValueKind == JsonValueKind.True;
        var text = result.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
        return (isError, text);
    }

    private static async Task<AttachmentDto> UploadTextArtifactAsync(
        HttpClient client,
        string fileName,
        string content,
        Guid? noteId = null,
        Guid? courseId = null,
        Guid? itemId = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(AttachmentKinds.Artifact), "kind");
        if (noteId.HasValue) form.Add(new StringContent(noteId.Value.ToString()), "noteId");
        if (courseId.HasValue) form.Add(new StringContent(courseId.Value.ToString()), "universityCourseId");
        if (itemId.HasValue) form.Add(new StringContent(itemId.Value.ToString()), "universityActivityItemId");

        var res = await client.PostAsync("/api/attachments", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<AttachmentDto>();
        Assert.NotNull(dto);
        return dto;
    }

    [Fact]
    public async Task McpEndpoint_RejectsHumanJwt_AndAllowsAgentPat()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        var session = await SetupOwnerAsync(client);
        var pat = await CreatePatAsync(client, "ReadOnly", "notes:read");

        var listToolsPayload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/list",
            @params = new { },
        };

        // Human JWT must be rejected on /mcp
        using var jwtRes = await PostMcpAsync(client, session.AccessToken, listToolsPayload);
        Assert.True(
            jwtRes.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected 401 or 403 for Human JWT on /mcp, got {jwtRes.StatusCode}");

        // Agent PAT must succeed and return all 9 read-only tools
        using var patRes = await PostMcpAsync(client, pat, listToolsPayload);
        Assert.Equal(HttpStatusCode.OK, patRes.StatusCode);

        using var doc = await ReadMcpJsonRpcAsync(patRes);
        var tools = doc.RootElement
            .GetProperty("result")
            .GetProperty("tools")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToHashSet(StringComparer.Ordinal);

        var expectedTools = new[]
        {
            "get_day",
            "list_events",
            "list_courses",
            "get_course",
            "list_notes_tree",
            "read_note",
            "list_artifacts",
            "read_artifact_text",
            "search_knowledge",
            "create_note",
            "update_note",
            "extract_pdf_images",
            "create_checklist_item",
            "toggle_checklist_item",
            "create_event",
            "create_course",
            "create_activity",
            "create_activity_item",
            "create_activity_items",
            "create_quiz",
        };

        Assert.Equal(expectedTools.Length, tools.Count);
        foreach (var expected in expectedTools)
        {
            Assert.Contains(expected, tools);
        }
    }

    [Fact]
    public async Task McpTools_EnforceModuleScopes_AndReturnCleanScopeError()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        // Seed general note + university course note with shared keyword
        await client.PostAsJsonAsync("/api/notes", new CreateNoteRequest("General Algo", null, "BellmanFord shortest path general"));
        var courseRes = await client.PostAsJsonAsync(
            "/api/university/courses",
            new CreateCourseRequest("Algorithms", "CS201", "2026 Fall", null, null, null, "#3b6ea5", "active", null, null));
        var course = await courseRes.Content.ReadFromJsonAsync<UniversityCourseDto>();
        Assert.NotNull(course);
        await client.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("Uni Algo", null, "BellmanFord university secret", true, course.Id));

        var notesOnlyPat = await CreatePatAsync(client, "NotesOnly", "notes:read");

        var (evErr, evText) = await CallMcpToolAsync(client, notesOnlyPat, "list_events", new { });
        Assert.True(evErr);
        Assert.Contains("calendar:read", evText, StringComparison.Ordinal);

        var (courseErr, courseText) = await CallMcpToolAsync(client, notesOnlyPat, "list_courses", new { });
        Assert.True(courseErr);
        Assert.Contains("university:read", courseText, StringComparison.Ordinal);

        var (dayErr, dayText) = await CallMcpToolAsync(client, notesOnlyPat, "get_day", new { });
        Assert.True(dayErr);
        Assert.Contains("tasks:read", dayText, StringComparison.Ordinal);

        var (searchErr, searchText) = await CallMcpToolAsync(
            client,
            notesOnlyPat,
            "search_knowledge",
            new { query = "BellmanFord" });
        Assert.False(searchErr, searchText);

        var searchDto = JsonSerializer.Deserialize<KnowledgeSearchResponse>(searchText, JsonOptions);
        Assert.NotNull(searchDto);
        Assert.Single(searchDto.Hits);
        Assert.Equal("General Algo", searchDto.Hits[0].Title);
    }

    [Fact]
    public async Task McpTools_AndRestSearch_EnforceAllowAgentReadPrivacy()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        // 1. Direct hidden note
        var hiddenNote = await (await client.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("Private Diary", null, "QuasarPrivacyToken direct hidden", AllowAgentRead: false)))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(hiddenNote);

        // 2. Note inside hidden folder
        var hiddenFolder = await (await client.PostAsJsonAsync(
            "/api/notes/folders",
            new CreateNoteFolderRequest("Secret Vault", null, AllowAgentRead: false)))
            .Content.ReadFromJsonAsync<NoteFolderDto>();
        Assert.NotNull(hiddenFolder);

        var noteInHiddenFolder = await (await client.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("Folder Secret", hiddenFolder.Id, "QuasarPrivacyToken folder hidden", AllowAgentRead: true)))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(noteInHiddenFolder);

        // 3. University item with hidden linked note + attached artifact
        var course = await (await client.PostAsJsonAsync(
            "/api/university/courses",
            new CreateCourseRequest("Physics", "PH101", "2026 Fall", null, null, null, "#3b6ea5", "active", null,
                [new CreateActivityRequest("lecture", "Lectures", null, null, null, null, null, 0, null)])))
            .Content.ReadFromJsonAsync<UniversityCourseDto>();
        Assert.NotNull(course);
        var lectureAct = course.Activities[0];
        var item = await (await client.PostAsJsonAsync(
            $"/api/university/activities/{lectureAct.Id}/items",
            new CreateActivityItemRequest("Lecture 1", "Quantum", null, 1, "pending", null, null)))
            .Content.ReadFromJsonAsync<UniversityActivityItemDto>();
        Assert.NotNull(item);

        var linkedNote = await (await client.PostAsJsonAsync(
            "/api/notes/linked",
            new OpenLinkedNoteRequest(NoteLinkKinds.UniversityItem, item.Id, null, null, null)))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(linkedNote);

        // Mark linked note private
        var hideRes = await client.PutAsJsonAsync(
            $"/api/notes/{linkedNote.Id}",
            new UpdateNoteRequest(linkedNote.Title, "QuasarPrivacyToken uni hidden", null, false, false, linkedNote.Version));
        Assert.Equal(HttpStatusCode.OK, hideRes.StatusCode);

        var hiddenItemArtifact = await UploadTextArtifactAsync(
            client,
            "quantum-slides.txt",
            "QuasarPrivacyToken artifact on hidden item",
            itemId: item.Id);

        // 4. Visible note
        var visibleNote = await (await client.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("Public Study", null, "QuasarPrivacyToken visible note", AllowAgentRead: true)))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(visibleNote);

        // Human JWT on REST sees ALL 5 matches (4 notes + 1 artifact)
        var humanSearchRes = await client.GetFromJsonAsync<KnowledgeSearchResponse>("/api/knowledge/search?q=QuasarPrivacyToken");
        Assert.NotNull(humanSearchRes);
        Assert.Equal(5, humanSearchRes.TotalReturned);

        var humanArtText = await client.GetFromJsonAsync<ArtifactTextResponse>($"/api/attachments/{hiddenItemArtifact.Id}/text");
        Assert.NotNull(humanArtText);
        Assert.Single(humanArtText.Chunks);

        // Full-scope Agent PAT on /mcp sees ONLY the visible note
        var fullPat = await CreatePatAsync(client, "FullRead", "tasks:read", "calendar:read", "university:read", "notes:read");

        var (searchErr, searchText) = await CallMcpToolAsync(client, fullPat, "search_knowledge", new { query = "QuasarPrivacyToken" });
        Assert.False(searchErr, searchText);
        var patSearch = JsonSerializer.Deserialize<KnowledgeSearchResponse>(searchText, JsonOptions);
        Assert.NotNull(patSearch);
        Assert.Single(patSearch.Hits);
        Assert.Equal(visibleNote.Id, patSearch.Hits[0].Id);

        var (readHiddenErr, _) = await CallMcpToolAsync(client, fullPat, "read_note", new { noteId = noteInHiddenFolder.Id });
        Assert.True(readHiddenErr);

        var (readArtErr, _) = await CallMcpToolAsync(client, fullPat, "read_artifact_text", new { attachmentId = hiddenItemArtifact.Id });
        Assert.True(readArtErr);
    }

    [Fact]
    public async Task McpTools_ReadArtifactText_PaginatesAndClampsBounds_AndListEventsCaps90Days()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var note = await (await client.PostAsJsonAsync("/api/notes", new CreateNoteRequest("Long Doc", null, "Ref")))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(note);

        // Create ~12,000 char document -> 3+ chunks
        var longText = string.Join("\n\n", Enumerable.Range(1, 120).Select(i =>
            $"Paragraph {i:000}: Distributed consensus algorithms like Raft and Paxos ensure replicated state machines agree."));
        var artifact = await UploadTextArtifactAsync(client, "consensus.txt", longText, noteId: note.Id);

        var pat = await CreatePatAsync(client, "BoundsPat", "notes:read", "calendar:read");

        var (err1, page1Json) = await CallMcpToolAsync(
            client,
            pat,
            "read_artifact_text",
            new { attachmentId = artifact.Id, startChunk = 0, maxChunks = 2 });
        Assert.False(err1, page1Json);
        var page1 = JsonSerializer.Deserialize<ArtifactTextResponse>(page1Json, JsonOptions);
        Assert.NotNull(page1);
        Assert.True(page1.TotalChunks >= 3);
        Assert.Equal(2, page1.Chunks.Count);
        Assert.True(page1.HasMore);

        var (err2, page2Json) = await CallMcpToolAsync(
            client,
            pat,
            "read_artifact_text",
            new { attachmentId = artifact.Id, startChunk = 2, maxChunks = 100 });
        Assert.False(err2, page2Json);
        var page2 = JsonSerializer.Deserialize<ArtifactTextResponse>(page2Json, JsonOptions);
        Assert.NotNull(page2);
        Assert.False(page2.HasMore);
        Assert.Equal(page1.TotalChunks - 2, page2.Chunks.Count);

        // 200-day range must be clamped to 90 days
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(200);
        var (evErr, evJson) = await CallMcpToolAsync(
            client,
            pat,
            "list_events",
            new { from = from.ToString("O"), to = to.ToString("O") });
        Assert.False(evErr, evJson);
        var evResp = JsonSerializer.Deserialize<KnowledgeEventsResponse>(evJson, JsonOptions);
        Assert.NotNull(evResp);
        Assert.True(evResp.RangeClampedTo90Days);
        Assert.Equal(TimeSpan.FromDays(90), evResp.To - evResp.From);
    }

    [Fact]
    public async Task McpTools_EndToEnd_DayCalendarUniversityNotesAndArtifacts()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        // Seed day checklist + note
        await client.PostAsJsonAsync("/api/days/2026-11-28/checklist", new CreateDailyCheckItemRequest("Review slides", "18:00"));
        await client.PutAsJsonAsync("/api/days/2026-11-28/note", new UpsertDailyNoteRequest("Focus on compilers today."));

        // Seed calendar event
        var start = new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero);
        await client.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(null, "Study Group", start, start.AddHours(2), false, "Library", "Review lab", "UTC", "confirmed", null, "Manual"));

        // Seed university course + lecture + artifact
        var course = await (await client.PostAsJsonAsync(
            "/api/university/courses",
            new CreateCourseRequest("Compilers", "CS410", "2026 Fall", null, "Dr. Ada", 5, "#3b6ea5", "active", null,
                [new CreateActivityRequest("lecture", "Lectures", null, "Room 204", 10, null, 12, 0, null)])))
            .Content.ReadFromJsonAsync<UniversityCourseDto>();
        Assert.NotNull(course);
        var item = await (await client.PostAsJsonAsync(
            $"/api/university/activities/{course.Activities[0].Id}/items",
            new CreateActivityItemRequest("Lecture 4", "LR Parsing", null, 4, "attended", null, null)))
            .Content.ReadFromJsonAsync<UniversityActivityItemDto>();
        Assert.NotNull(item);

        var courseArtifact = await UploadTextArtifactAsync(
            client,
            "lr-parsing.txt",
            "Shift-reduce parsers use an action table and goto table.",
            courseId: course.Id);

        // Seed general note
        var note = await (await client.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("Compiler Cheatsheet", null, "# LR(1)\nShift-reduce conflicts explained.")))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(note);

        var pat = await CreatePatAsync(client, "FullAssistant", "tasks:read", "calendar:read", "university:read", "notes:read");

        var (dayErr, dayJson) = await CallMcpToolAsync(client, pat, "get_day", new { date = "2026-11-28" });
        Assert.False(dayErr, dayJson);
        var day = JsonSerializer.Deserialize<KnowledgeDayResponse>(dayJson, JsonOptions);
        Assert.NotNull(day);
        Assert.Single(day.Checklist);
        Assert.NotNull(day.Note);
        Assert.False(day.NoteHiddenByScopeOrPrivacy);
        Assert.Contains("compilers", day.Note.Content, StringComparison.OrdinalIgnoreCase);

        // A tasks:read-only PAT gets checklist items but hides the daily note
        var tasksOnlyPat = await CreatePatAsync(client, "TasksOnly", "tasks:read");
        var (dayRestrictedErr, dayRestrictedJson) = await CallMcpToolAsync(client, tasksOnlyPat, "get_day", new { date = "2026-11-28" });
        Assert.False(dayRestrictedErr, dayRestrictedJson);
        var dayRestricted = JsonSerializer.Deserialize<KnowledgeDayResponse>(dayRestrictedJson, JsonOptions);
        Assert.NotNull(dayRestricted);
        Assert.Single(dayRestricted.Checklist);
        Assert.Null(dayRestricted.Note);
        Assert.True(dayRestricted.NoteHiddenByScopeOrPrivacy);

        var (evErr, evJson) = await CallMcpToolAsync(
            client,
            pat,
            "list_events",
            new { from = "2026-11-28T00:00:00Z", to = "2026-11-29T00:00:00Z" });
        Assert.False(evErr, evJson);
        var events = JsonSerializer.Deserialize<KnowledgeEventsResponse>(evJson, JsonOptions);
        Assert.NotNull(events);
        Assert.Contains(events.Events, e => e.Title == "Study Group");

        var (coursesErr, coursesJson) = await CallMcpToolAsync(client, pat, "list_courses", new { semester = "2026 Fall" });
        Assert.False(coursesErr, coursesJson);
        var courses = JsonSerializer.Deserialize<List<KnowledgeCourseSummaryDto>>(coursesJson, JsonOptions);
        Assert.NotNull(courses);
        Assert.Single(courses);
        Assert.Equal("Compilers", courses[0].Name);

        var (courseErr, courseJson) = await CallMcpToolAsync(client, pat, "get_course", new { courseId = course.Id });
        Assert.False(courseErr, courseJson);
        var courseDetail = JsonSerializer.Deserialize<KnowledgeCourseDetailDto>(courseJson, JsonOptions);
        Assert.NotNull(courseDetail);
        Assert.Single(courseDetail.CourseArtifacts);

        var (treeErr, treeJson) = await CallMcpToolAsync(client, pat, "list_notes_tree", new { });
        Assert.False(treeErr, treeJson);
        var tree = JsonSerializer.Deserialize<NotesTreeResponse>(treeJson, JsonOptions);
        Assert.NotNull(tree);
        Assert.Contains(tree.Notes, n => n.Id == note.Id);

        var (readNoteErr, readNoteJson) = await CallMcpToolAsync(client, pat, "read_note", new { noteId = note.Id });
        Assert.False(readNoteErr, readNoteJson);
        var readNote = JsonSerializer.Deserialize<KnowledgeNoteDetailDto>(readNoteJson, JsonOptions);
        Assert.NotNull(readNote);
        Assert.Contains("Shift-reduce conflicts", readNote.Note.Content, StringComparison.Ordinal);

        var (listArtErr, listArtJson) = await CallMcpToolAsync(client, pat, "list_artifacts", new { universityCourseId = course.Id });
        Assert.False(listArtErr, listArtJson);
        var artifacts = JsonSerializer.Deserialize<List<AttachmentDto>>(listArtJson, JsonOptions);
        Assert.NotNull(artifacts);
        Assert.Single(artifacts);
        Assert.Equal(courseArtifact.Id, artifacts[0].Id);

        var (readArtErr, readArtJson) = await CallMcpToolAsync(client, pat, "read_artifact_text", new { attachmentId = courseArtifact.Id });
        Assert.False(readArtErr, readArtJson);
        var artText = JsonSerializer.Deserialize<ArtifactTextResponse>(readArtJson, JsonOptions);
        Assert.NotNull(artText);
        Assert.Contains("action table", artText.Chunks[0].Content, StringComparison.Ordinal);

        var (searchErr, searchJson) = await CallMcpToolAsync(client, pat, "search_knowledge", new { query = "Shift-reduce" });
        Assert.False(searchErr, searchJson);
        var search = JsonSerializer.Deserialize<KnowledgeSearchResponse>(searchJson, JsonOptions);
        Assert.NotNull(search);
        Assert.Equal(2, search.TotalReturned);
        Assert.Contains(search.Hits, h => h.SourceType == "note" && h.Id == note.Id);
        Assert.Contains(search.Hits, h => h.SourceType == "artifact" && h.Id == courseArtifact.Id);
    }

    [Fact]
    public async Task McpWriteTools_CreateCoursesLecturesMultipleNotesEventsTasks_AndEnforceScopesAndPrivacy()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        // Private note must NOT be updatable by an agent PAT even with notes:write
        var privateNote = await (await client.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("Private Journal", null, "Do not overwrite", AllowAgentRead: false)))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(privateNote);

        var readOnlyPat = await CreatePatAsync(client, "ReadOnly", "notes:read", "university:read");
        var (roErr, roText) = await CallMcpToolAsync(
            client,
            readOnlyPat,
            "create_course",
            new { name = "Blocked Course" });
        Assert.True(roErr);
        Assert.Contains("university:write", roText, StringComparison.Ordinal);

        var writePat = await CreatePatAsync(
            client,
            "FullReadWrite",
            "notes:read", "notes:write",
            "tasks:read", "tasks:write",
            "calendar:read", "calendar:write",
            "university:read", "university:write");

        // 1. Create course -> activity -> lecture item via MCP
        var (cErr, cJson) = await CallMcpToolAsync(client, writePat, "create_course", new
        {
            name = "Operating Systems",
            code = "CS320",
            semester = "2026 Fall",
        });
        Assert.False(cErr, cJson);
        var createdCourse = JsonSerializer.Deserialize<UniversityCourseDto>(cJson, JsonOptions);
        Assert.NotNull(createdCourse);

        var (aErr, aJson) = await CallMcpToolAsync(client, writePat, "create_activity", new
        {
            courseId = createdCourse.Id,
            activityType = "lecture",
            title = "Lectures",
        });
        Assert.False(aErr, aJson);
        var createdAct = JsonSerializer.Deserialize<UniversityCourseActivityDto>(aJson, JsonOptions);
        Assert.NotNull(createdAct);

        var (iErr, iJson) = await CallMcpToolAsync(client, writePat, "create_activity_item", new
        {
            activityId = createdAct.Id,
            title = "Lecture 1",
            theme = "Processes & Threads",
            sequenceNumber = 1,
            startAt = "2026-11-28T09:00:00Z",
            endAt = "2026-11-28T10:30:00Z",
        });
        Assert.False(iErr, iJson);
        var createdItem = JsonSerializer.Deserialize<UniversityActivityItemDto>(iJson, JsonOptions);
        Assert.NotNull(createdItem);
        Assert.NotNull(createdItem.CalendarEventId);

        // 2. Attach MULTIPLE notes to the same lecture via MCP
        var (n1Err, n1Json) = await CallMcpToolAsync(client, writePat, "create_note", new
        {
            title = "Lecture 1 Raw Notes",
            content = "Context switches save registers.",
            universityActivityItemId = createdItem.Id,
        });
        Assert.False(n1Err, n1Json);
        var note1 = JsonSerializer.Deserialize<NoteDetailDto>(n1Json, JsonOptions);
        Assert.NotNull(note1);

        var (n2Err, n2Json) = await CallMcpToolAsync(client, writePat, "create_note", new
        {
            title = "Study Guide — Lecture 1",
            content = "# Study Guide\n\n> [!note] Key Concept\n> PCB stores process state.",
            universityActivityItemId = createdItem.Id,
        });
        Assert.False(n2Err, n2Json);
        var note2 = JsonSerializer.Deserialize<NoteDetailDto>(n2Json, JsonOptions);
        Assert.NotNull(note2);
        Assert.NotEqual(note1.Id, note2.Id);

        // Notes tree returns BOTH notes for that lecture
        var tree = await client.GetFromJsonAsync<NotesTreeResponse>("/api/notes/tree");
        Assert.NotNull(tree);
        var treeItem = tree.University
            .SelectMany(s => s.Courses)
            .SelectMany(c => c.Activities)
            .SelectMany(a => a.Items)
            .First(i => i.Id == createdItem.Id);
        Assert.NotNull(treeItem.Notes);
        Assert.Equal(2, treeItem.Notes.Count);

        // 3. Update agent-readable note succeeds; updating private note is blocked
        var (uErr, uJson) = await CallMcpToolAsync(client, writePat, "update_note", new
        {
            noteId = note2.Id,
            content = "# Study Guide\nUpdated with exam tips.",
        });
        Assert.False(uErr, uJson);

        var (privErr, _) = await CallMcpToolAsync(client, writePat, "update_note", new
        {
            noteId = privateNote.Id,
            content = "Attempted hijack",
        });
        Assert.True(privErr);

        // 4. Create & toggle checklist item + create calendar event
        var (tErr, tJson) = await CallMcpToolAsync(client, writePat, "create_checklist_item", new
        {
            title = "Review OS study guide",
            date = "2026-11-28",
            dueTime = "19:00",
        });
        Assert.False(tErr, tJson);
        var task = JsonSerializer.Deserialize<DailyCheckItemDto>(tJson, JsonOptions);
        Assert.NotNull(task);

        var (togErr, togJson) = await CallMcpToolAsync(client, writePat, "toggle_checklist_item", new
        {
            itemId = task.Id,
            isCompleted = true,
        });
        Assert.False(togErr, togJson);
        var toggled = JsonSerializer.Deserialize<DailyCheckItemDto>(togJson, JsonOptions);
        Assert.NotNull(toggled);
        Assert.True(toggled.IsCompleted);

        var (evErr, evJson) = await CallMcpToolAsync(client, writePat, "create_event", new
        {
            title = "OS Office Hours",
            startAt = "2026-11-29T14:00:00Z",
            endAt = "2026-11-29T15:00:00Z",
            location = "Room 301",
        });
        Assert.False(evErr, evJson);
        var createdEvent = JsonSerializer.Deserialize<CalendarEventDto>(evJson, JsonOptions);
        Assert.NotNull(createdEvent);
        Assert.Equal("OS Office Hours", createdEvent.Title);

        // 5. Batch create_activity_items creates multiple scheduled items + calendar events and enforces 50-item limit
        var batchItems = Enumerable.Range(2, 4).Select(i => new
        {
            title = $"Lecture {i}",
            theme = $"Topic {i}",
            sequenceNumber = i,
            startAt = new DateTimeOffset(2026, 9, 8, 10, 15, 0, TimeSpan.Zero).AddDays((i - 1) * 7).ToString("O"),
            endAt = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero).AddDays((i - 1) * 7).ToString("O"),
            location = "IE220",
        }).ToArray();

        var (batchErr, batchJson) = await CallMcpToolAsync(client, writePat, "create_activity_items", new
        {
            activityId = createdItem.ActivityId,
            items = batchItems,
        });
        Assert.False(batchErr, batchJson);
        var batchRes = JsonSerializer.Deserialize<BatchCreateActivityItemsResponse>(batchJson, JsonOptions);
        Assert.NotNull(batchRes);
        Assert.Equal(4, batchRes.CreatedCount);
        Assert.Equal(4, batchRes.Items.Count);
        Assert.All(batchRes.Items, it => Assert.NotNull(it.CalendarEventId));

        // Read-only PAT is denied
        var (roBatchErr, _) = await CallMcpToolAsync(client, readOnlyPat, "create_activity_items", new
        {
            activityId = createdItem.ActivityId,
            items = batchItems,
        });
        Assert.True(roBatchErr);

        // > 50 items rejected
        var tooMany = Enumerable.Range(1, 51).Select(i => new { title = $"Item {i}" }).ToArray();
        var (limitErr, _) = await CallMcpToolAsync(client, writePat, "create_activity_items", new
        {
            activityId = createdItem.ActivityId,
            items = tooMany,
        });
        Assert.True(limitErr);
    }
}
