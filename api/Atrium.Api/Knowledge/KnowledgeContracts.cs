using Atrium.Api.Attachments;
using Atrium.Api.Calendar;
using Atrium.Api.Days;
using Atrium.Api.Notes;
using Atrium.Api.University;

namespace Atrium.Api.Knowledge;

public sealed record KnowledgeResult<T>(
    T? Value,
    string? Error,
    int StatusCode)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300 && Error is null;
}

public static class KnowledgeResult
{
    public static KnowledgeResult<T> Ok<T>(T value) => new(value, null, StatusCodes.Status200OK);
    public static KnowledgeResult<T> BadRequest<T>(string error) => new(default, error, StatusCodes.Status400BadRequest);
    public static KnowledgeResult<T> Unauthorized<T>(string error = "Unauthorized.") => new(default, error, StatusCodes.Status401Unauthorized);
    public static KnowledgeResult<T> Forbidden<T>(string error) => new(default, error, StatusCodes.Status403Forbidden);
    public static KnowledgeResult<T> NotFound<T>(string error = "Not found.") => new(default, error, StatusCodes.Status404NotFound);
}

public sealed record KnowledgeDayResponse(
    string Date,
    IReadOnlyList<DailyCheckItemDto> Checklist,
    DailyNoteDto? Note,
    bool NoteHiddenByScopeOrPrivacy);

public sealed record KnowledgeEventsResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    bool RangeClampedTo90Days,
    IReadOnlyList<CalendarEventDto> Events);

public sealed record KnowledgeCourseActivitySummaryDto(
    Guid Id,
    string ActivityType,
    string Label,
    int ItemCount,
    AttendanceSummaryDto Attendance);

public sealed record KnowledgeCourseSummaryDto(
    Guid Id,
    string Name,
    string? Code,
    string? Semester,
    string? Instructor,
    decimal? Credits,
    string Status,
    int NoteCount,
    int ArtifactCount,
    IReadOnlyList<KnowledgeCourseActivitySummaryDto> Activities);

public sealed record KnowledgeCourseDetailDto(
    UniversityCourseDto Course,
    IReadOnlyList<NoteSummaryDto> CourseNotes,
    IReadOnlyList<AttachmentDto> CourseArtifacts);

public sealed record KnowledgeNoteDetailDto(
    NoteDetailDto Note,
    IReadOnlyList<AttachmentDto> Artifacts);

public sealed record ArtifactTextChunkDto(
    int ChunkIndex,
    string PageLabel,
    string Content);

public sealed record ArtifactTextResponse(
    Guid AttachmentId,
    string FileName,
    string ContentType,
    string Status,
    int? ExtractedPageCount,
    int TotalChunks,
    int StartChunk,
    bool HasMore,
    IReadOnlyList<ArtifactTextChunkDto> Chunks);

public sealed record KnowledgeSearchHitDto(
    string SourceType,
    Guid Id,
    string Title,
    string? LinkLabel,
    string? PageLabel,
    int? ChunkIndex,
    string Snippet);

public sealed record KnowledgeSearchResponse(
    string Query,
    string Scope,
    int TotalReturned,
    IReadOnlyList<KnowledgeSearchHitDto> Hits);
