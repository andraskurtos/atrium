using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Atrium.Api.Knowledge;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Atrium.Api.Mcp;

[McpServerToolType]
public sealed class AtriumMcpTools(
    KnowledgeReadService knowledge,
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

