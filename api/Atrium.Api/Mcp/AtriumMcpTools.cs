using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Atrium.Api.Knowledge;
using Atrium.Api.University;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Atrium.Api.Mcp;

[McpServerToolType]
public sealed class AtriumMcpTools(
    KnowledgeReadService knowledge,
    KnowledgeWriteService writeService,
    IHttpContextAccessor httpContextAccessor)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    [McpServerTool(Name = "get_day", ReadOnly = true, Idempotent = true)]
    [Description("Get a user's daily checklist items and (if permitted by notes:read scope and privacy) daily Markdown note for a yyyy-MM-dd date. Defaults to today in the user's timezone.")]
    public async Task<CallToolResult> GetDayAsync(
        [Description("Optional date in yyyy-MM-dd format. Defaults to today.")] string? date = null,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        var result = await knowledge.GetDayAsync(principal, enforceAgentPrivacy: true, date, ct);
        return ToToolResult(result);
    }

    [McpServerTool(Name = "list_events", ReadOnly = true, Idempotent = true)]
    [Description("List expanded calendar events (including recurring occurrences) in a date range. Defaults to today through +7 days; maximum span is 90 days.")]
    public async Task<CallToolResult> ListEventsAsync(
        [Description("Optional ISO-8601 start timestamp (UTC or with offset). Defaults to start of today UTC.")] string? from = null,
        [Description("Optional ISO-8601 end timestamp (UTC or with offset). Defaults to from + 7 days; capped at 90 days.")] string? to = null,
        [Description("Optional calendar GUID filter.")] Guid? calendarId = null,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        if (!TryParseOptionalDateTimeOffset(from, out var parsedFrom))
        {
            return ErrorResult("Invalid 'from' ISO-8601 timestamp.");
        }

        if (!TryParseOptionalDateTimeOffset(to, out var parsedTo))
        {
            return ErrorResult("Invalid 'to' ISO-8601 timestamp.");
        }

        var result = await knowledge.ListEventsAsync(principal, enforceAgentPrivacy: true, parsedFrom, parsedTo, calendarId, ct);
        return ToToolResult(result);
    }

    [McpServerTool(Name = "list_courses", ReadOnly = true, Idempotent = true)]
    [Description("List university courses with attendance summaries, note counts, and artifact counts.")]
    public async Task<CallToolResult> ListCoursesAsync(
        [Description("Optional semester filter (e.g. '2026 Fall') or 'all'.")] string? semester = null,
        [Description("Optional status filter: 'active', 'completed', 'archived', or 'all'.")] string? status = null,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        var result = await knowledge.ListCoursesAsync(principal, enforceAgentPrivacy: true, semester, status, ct);
        return ToToolResult(result);
    }

    [McpServerTool(Name = "get_course", ReadOnly = true, Idempotent = true)]
    [Description("Get full details for a university course including activities, lectures/items, schedules, attendance, course notes, and artifacts.")]
    public async Task<CallToolResult> GetCourseAsync(
        [Description("The university course GUID.")] Guid courseId,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        var result = await knowledge.GetCourseAsync(principal, enforceAgentPrivacy: true, courseId, ct);
        return ToToolResult(result);
    }

    [McpServerTool(Name = "list_notes_tree", ReadOnly = true, Idempotent = true)]
    [Description("List agent-readable folders, general notes, tags, and optionally university notes, event notes, and daily notes.")]
    public async Task<CallToolResult> ListNotesTreeAsync(
        [Description("Include university course/item notes tree (requires university:read). Defaults to true.")] bool includeUniversity = true,
        [Description("Include calendar event notes tree (requires calendar:read). Defaults to true.")] bool includeEvents = true,
        [Description("Include daily Markdown notes. Defaults to true.")] bool includeDaily = true,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        var result = await knowledge.ListNotesTreeAsync(
            principal,
            enforceAgentPrivacy: true,
            includeUniversity,
            includeEvents,
            includeDaily,
            ct);
        return ToToolResult(result);
    }

    [McpServerTool(Name = "read_note", ReadOnly = true, Idempotent = true)]
    [Description("Read the full Markdown content, tags, link context, and visible artifact metadata of a single note.")]
    public async Task<CallToolResult> ReadNoteAsync(
        [Description("The note GUID.")] Guid noteId,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        var result = await knowledge.ReadNoteAsync(principal, enforceAgentPrivacy: true, noteId, ct);
        return ToToolResult(result);
    }

    [McpServerTool(Name = "list_artifacts", ReadOnly = true, Idempotent = true)]
    [Description("List visible uploaded artifacts (PDFs, slides, docs, text files) attached to exactly one target owner: a note, university course, university activity item, or calendar event.")]
    public async Task<CallToolResult> ListArtifactsAsync(
        [Description("Optional owning note GUID.")] Guid? noteId = null,
        [Description("Optional owning university course GUID.")] Guid? universityCourseId = null,
        [Description("Optional owning university activity item GUID.")] Guid? universityActivityItemId = null,
        [Description("Optional owning calendar event GUID.")] Guid? calendarEventId = null,
        [Description("Optional ISO-8601 occurrence start timestamp for recurring calendar events.")] string? occurrenceStartAt = null,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        if (!TryParseOptionalDateTimeOffset(occurrenceStartAt, out var parsedOcc))
        {
            return ErrorResult("Invalid 'occurrenceStartAt' ISO-8601 timestamp.");
        }

        var result = await knowledge.ListArtifactsAsync(
            principal,
            enforceAgentPrivacy: true,
            noteId,
            universityCourseId,
            universityActivityItemId,
            calendarEventId,
            parsedOcc,
            ct);
        return ToToolResult(result);
    }

    [McpServerTool(Name = "read_artifact_text", ReadOnly = true, Idempotent = true)]
    [Description("Read paginated extracted plain text chunks (by page/slide/section) from an uploaded artifact. maxChunks defaults to 10 and is clamped to 1..20.")]
    public async Task<CallToolResult> ReadArtifactTextAsync(
        [Description("The artifact attachment GUID.")] Guid attachmentId,
        [Description("0-based starting chunk index. Defaults to 0.")] int startChunk = 0,
        [Description("Maximum number of chunks to return (1..20). Defaults to 10.")] int maxChunks = KnowledgeReadService.DefaultArtifactMaxChunks,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        var result = await knowledge.ReadArtifactTextAsync(
            principal,
            enforceAgentPrivacy: true,
            attachmentId,
            startChunk,
            maxChunks,
            ct);
        return ToToolResult(result);
    }

    [McpServerTool(Name = "search_knowledge", ReadOnly = true, Idempotent = true)]
    [Description("Full-text search across agent-readable Markdown notes and extracted artifact text (PDFs, slides, docs). Returns ranked snippets with source IDs and page/slide labels.")]
    public async Task<CallToolResult> SearchKnowledgeAsync(
        [Description("Search query text.")] string query,
        [Description("Optional scope: 'all' (default), 'notes', or 'artifacts'.")] string? scope = "all",
        [Description("Optional university course GUID to restrict results to a single course.")] Guid? courseId = null,
        [Description("Maximum number of hits to return (1..25). Defaults to 10.")] int limit = KnowledgeReadService.DefaultSearchLimit,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        var result = await knowledge.SearchKnowledgeAsync(
            principal,
            enforceAgentPrivacy: true,
            query,
            scope,
            courseId,
            limit,
            ct);
        return ToToolResult(result);
    }

    [McpServerTool(Name = "create_note", ReadOnly = false, Destructive = false)]
    [Description("Create a new Markdown note (general, in a folder, linked to a university course or lecture/activity item, linked to a calendar event, or daily). Requires notes:write scope.")]
    public async Task<CallToolResult> CreateNoteAsync(
        [Description("Note title.")] string title,
        [Description("Markdown content (supports headings, GFM tables, callouts, code blocks, LaTeX math, and embedded images).")] string content,
        [Description("Optional folder GUID.")] Guid? folderId = null,
        [Description("Optional university course GUID.")] Guid? universityCourseId = null,
        [Description("Optional university lecture/activity item GUID (supports multiple notes per lecture).")] Guid? universityActivityItemId = null,
        [Description("Optional calendar event GUID.")] Guid? calendarEventId = null,
        [Description("Optional ISO-8601 occurrence start timestamp for recurring calendar events.")] string? occurrenceStartAt = null,
        [Description("Optional daily date in yyyy-MM-dd format.")] string? dailyDate = null,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        if (!TryParseOptionalDateTimeOffset(occurrenceStartAt, out var parsedOcc))
        {
            return ErrorResult("Invalid 'occurrenceStartAt' ISO-8601 timestamp.");
        }

        var result = await writeService.CreateNoteAsync(
            principal,
            enforceAgentRules: true,
            title,
            content,
            folderId,
            universityCourseId,
            universityActivityItemId,
            calendarEventId,
            parsedOcc,
            dailyDate,
            ct);
        return ToToolResult(result);
    }

    [McpServerTool(Name = "update_note", ReadOnly = false, Destructive = false)]
    [Description("Update an existing agent-readable Markdown note's title and/or content. Requires notes:write scope.")]
    public async Task<CallToolResult> UpdateNoteAsync(
        [Description("Note GUID to update.")] Guid noteId,
        [Description("Optional new title.")] string? title = null,
        [Description("Optional new Markdown content.")] string? content = null,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        var result = await writeService.UpdateNoteAsync(principal, enforceAgentRules: true, noteId, title, content, ct);
        return ToToolResult(result);
    }

    [McpServerTool(Name = "extract_pdf_images", ReadOnly = false, Destructive = false)]
    [Description("Extract diagrams/figures from specific pages of a PDF artifact (skipping slide backgrounds and rendering vector-diagram slides to PNG), attach them as inline images on a target note, and return Markdown embed snippets. Requires notes:write scope.")]
    public async Task<CallToolResult> ExtractPdfImagesAsync(
        [Description("Source PDF artifact attachment GUID.")] Guid attachmentId,
        [Description("Target note GUID where the extracted images will be attached.")] Guid noteId,
        [Description("Optional list of specific 1-based page/slide numbers to extract or render (e.g. [5, 11, 39]).")] int[]? pages = null,
        [Description("Optional 1-based start page.")] int? startPage = null,
        [Description("Optional 1-based end page.")] int? endPage = null,
        [Description("Maximum images to extract (1-6, default 4).")] int maxImages = 4,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        var result = await writeService.ExtractPdfImagesAsync(
            principal,
            enforceAgentRules: true,
            attachmentId,
            noteId,
            startPage,
            endPage,
            maxImages,
            ct,
            pages);
        return ToToolResult(result);
    }

    [McpServerTool(Name = "create_checklist_item", ReadOnly = false, Destructive = false)]
    [Description("Add a daily checklist task for a yyyy-MM-dd date (defaults to today). Requires tasks:write scope.")]
    public async Task<CallToolResult> CreateChecklistItemAsync(
        [Description("Checklist task title.")] string title,
        [Description("Optional yyyy-MM-dd date. Defaults to today UTC.")] string? date = null,
        [Description("Optional 24-hour HH:mm due time.")] string? dueTime = null,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        var result = await writeService.CreateChecklistItemAsync(principal, enforceAgentRules: true, date, title, dueTime, ct);
        return ToToolResult(result);
    }

    [McpServerTool(Name = "toggle_checklist_item", ReadOnly = false, Destructive = false)]
    [Description("Mark a daily checklist item as completed or incomplete. Requires tasks:write scope.")]
    public async Task<CallToolResult> ToggleChecklistItemAsync(
        [Description("Checklist item GUID.")] Guid itemId,
        [Description("True to mark completed, false to mark open.")] bool isCompleted,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        var result = await writeService.ToggleChecklistItemAsync(principal, enforceAgentRules: true, itemId, isCompleted, ct);
        return ToToolResult(result);
    }

    [McpServerTool(Name = "create_event", ReadOnly = false, Destructive = false)]
    [Description("Create a new calendar event. Requires calendar:write scope.")]
    public async Task<CallToolResult> CreateEventAsync(
        [Description("Event title.")] string title,
        [Description("ISO-8601 start timestamp.")] string startAt,
        [Description("ISO-8601 end timestamp.")] string endAt,
        [Description("Optional calendar GUID (defaults to user's default calendar).")] Guid? calendarId = null,
        [Description("Whether this is an all-day event.")] bool isAllDay = false,
        [Description("Optional location.")] string? location = null,
        [Description("Optional description.")] string? description = null,
        [Description("Optional RFC 5545 RRULE string.")] string? recurrenceRule = null,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        if (!TryParseOptionalDateTimeOffset(startAt, out var parsedStart) || !parsedStart.HasValue)
        {
            return ErrorResult("Valid ISO-8601 'startAt' is required.");
        }

        if (!TryParseOptionalDateTimeOffset(endAt, out var parsedEnd) || !parsedEnd.HasValue)
        {
            return ErrorResult("Valid ISO-8601 'endAt' is required.");
        }

        var result = await writeService.CreateEventAsync(
            principal,
            enforceAgentRules: true,
            title,
            parsedStart.Value,
            parsedEnd.Value,
            calendarId,
            isAllDay,
            location,
            description,
            recurrenceRule,
            ct);
        return ToToolResult(result);
    }

    [McpServerTool(Name = "create_course", ReadOnly = false, Destructive = false)]
    [Description("Create a new university course. Requires university:write scope.")]
    public async Task<CallToolResult> CreateCourseAsync(
        [Description("Course name.")] string name,
        [Description("Optional course code (e.g. CS410).")] string? code = null,
        [Description("Optional semester (e.g. 2026 Fall).")] string? semester = null,
        [Description("Optional instructor name.")] string? instructor = null,
        [Description("Optional credits.")] decimal? credits = null,
        [Description("Optional 6-digit hex color (e.g. #3b6ea5).")] string? color = null,
        [Description("Optional course description.")] string? description = null,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        var result = await writeService.CreateCourseAsync(
            principal,
            enforceAgentRules: true,
            name,
            code,
            semester,
            instructor,
            credits,
            color,
            description,
            ct);
        return ToToolResult(result);
    }

    [McpServerTool(Name = "create_activity", ReadOnly = false, Destructive = false)]
    [Description("Add an activity group (lecture, practice, laboratory, homework_assignment, semester_assignment, midterm, exam) to a university course. Requires university:write scope.")]
    public async Task<CallToolResult> CreateActivityAsync(
        [Description("Owning university course GUID.")] Guid courseId,
        [Description("Activity type: lecture, practice, laboratory, homework_assignment, semester_assignment, midterm, or exam.")] string activityType,
        [Description("Optional custom title.")] string? title = null,
        [Description("Optional default room/location.")] string? defaultLocation = null,
        [Description("Optional minimum attendance count.")] int? minAttendanceCount = null,
        [Description("Optional minimum attendance percent (0-100).")] decimal? minAttendancePercent = null,
        [Description("Optional planned total session count.")] int? plannedTotalCount = null,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        var result = await writeService.CreateActivityAsync(
            principal,
            enforceAgentRules: true,
            courseId,
            activityType,
            title,
            defaultLocation,
            minAttendanceCount,
            minAttendancePercent,
            plannedTotalCount,
            ct);
        return ToToolResult(result);
    }

    [McpServerTool(Name = "create_activity_item", ReadOnly = false, Destructive = false)]
    [Description("Add a lecture, lab, assignment, or exam item to a university course activity (and optionally schedule it on the University calendar). Requires university:write scope.")]
    public async Task<CallToolResult> CreateActivityItemAsync(
        [Description("Owning university course activity GUID.")] Guid activityId,
        [Description("Item title (e.g. Lecture 5).")] string title,
        [Description("Optional topic/theme.")] string? theme = null,
        [Description("Optional short summary notes.")] string? notes = null,
        [Description("Optional sequence number.")] int? sequenceNumber = null,
        [Description("Optional ISO-8601 start timestamp to schedule on the University calendar.")] string? startAt = null,
        [Description("Optional ISO-8601 end timestamp.")] string? endAt = null,
        [Description("Whether the scheduled event is all-day.")] bool isAllDay = false,
        [Description("Optional room/location.")] string? location = null,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        if (!TryParseOptionalDateTimeOffset(startAt, out var parsedStart))
        {
            return ErrorResult("Invalid 'startAt' ISO-8601 timestamp.");
        }

        if (!TryParseOptionalDateTimeOffset(endAt, out var parsedEnd))
        {
            return ErrorResult("Invalid 'endAt' ISO-8601 timestamp.");
        }

        var result = await writeService.CreateActivityItemAsync(
            principal,
            enforceAgentRules: true,
            activityId,
            title,
            theme,
            notes,
            sequenceNumber,
            parsedStart,
            parsedEnd,
            isAllDay,
            location,
            ct);
        return ToToolResult(result);
    }

    public sealed record McpBatchActivityItemDto(
        string Title,
        string? Theme = null,
        string? Notes = null,
        int? SequenceNumber = null,
        string? StartAt = null,
        string? EndAt = null,
        bool IsAllDay = false,
        string? Location = null);

    [McpServerTool(Name = "create_activity_items", ReadOnly = false, Destructive = false)]
    [Description("Batch-create up to 50 lectures, labs, assignments, or exams for a university course activity in one call (optionally scheduling each on the University calendar). Requires university:write scope.")]
    public async Task<CallToolResult> CreateActivityItemsAsync(
        [Description("Owning university course activity GUID.")] Guid activityId,
        [Description("Array of 1–50 items to create.")] IReadOnlyList<McpBatchActivityItemDto> items,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        if (items is null || items.Count == 0)
        {
            return ErrorResult("At least 1 item is required.");
        }

        var parsedItems = new List<BatchActivityItemInput>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var entry = items[i];
            if (!TryParseOptionalDateTimeOffset(entry.StartAt, out var parsedStart))
            {
                return ErrorResult($"Item #{i + 1}: invalid 'startAt' ISO-8601 timestamp.");
            }

            if (!TryParseOptionalDateTimeOffset(entry.EndAt, out var parsedEnd))
            {
                return ErrorResult($"Item #{i + 1}: invalid 'endAt' ISO-8601 timestamp.");
            }

            parsedItems.Add(new BatchActivityItemInput(
                entry.Title,
                entry.Theme,
                entry.Notes,
                entry.SequenceNumber,
                parsedStart,
                parsedEnd,
                entry.IsAllDay,
                entry.Location));
        }

        var result = await writeService.CreateActivityItemsBatchAsync(
            principal,
            enforceAgentRules: true,
            activityId,
            parsedItems,
            ct);
        return ToToolResult(result);
    }

    [McpServerTool(Name = "create_quiz", ReadOnly = false, Destructive = false)]
    [Description("Create a playable multiple-choice quiz with explanations for a university lecture/activity item or an entire course. Requires university:write scope.")]
    public async Task<CallToolResult> CreateQuizAsync(
        [Description("Quiz title.")] string title,
        [Description("Array of 1–50 multiple-choice questions (prompt, 2–6 options, 0-based correctIndex, explanation, optional sourceReference).")] IReadOnlyList<QuizQuestionDto> questions,
        [Description("Optional university course GUID (required if universityActivityItemId is omitted).")] Guid? courseId = null,
        [Description("Optional university lecture/activity item GUID (omit for a whole-course quiz).")] Guid? universityActivityItemId = null,
        [Description("Optional short description of what the quiz covers.")] string? description = null,
        CancellationToken ct = default)
    {
        var principal = GetPrincipal();
        if (principal is null) return ErrorResult("Unauthorized.");

        var result = await writeService.CreateQuizAsync(
            principal,
            enforceAgentRules: true,
            courseId,
            universityActivityItemId,
            title,
            description,
            questions,
            ct);
        return ToToolResult(result);
    }

    private ClaimsPrincipal? GetPrincipal() => httpContextAccessor.HttpContext?.User;

    private static CallToolResult ToToolResult<T>(KnowledgeResult<T> result)
    {
        if (!result.IsSuccess)
        {
            return ErrorResult(result.Error ?? "Request failed.");
        }

        return new CallToolResult
        {
            IsError = false,
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(result.Value, JsonOptions),
                },
            ],
        };
    }

    private static CallToolResult ErrorResult(string message) => new()
    {
        IsError = true,
        Content =
        [
            new TextContentBlock
            {
                Text = message,
            },
        ],
    };

    private static bool TryParseOptionalDateTimeOffset(string? raw, out DateTimeOffset? parsed)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            parsed = null;
            return true;
        }

        if (DateTimeOffset.TryParse(
            raw.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value))
        {
            parsed = value;
            return true;
        }

        parsed = null;
        return false;
    }
}

