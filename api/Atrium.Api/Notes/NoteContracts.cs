using Atrium.Api.Attachments;

namespace Atrium.Api.Notes;

public static class NoteLinkKinds
{
    public const string General = "general";
    public const string UniversityCourse = "university_course";
    public const string UniversityItem = "university_item";
    public const string CalendarEvent = "calendar_event";
    public const string Daily = "daily";
}

public sealed record NoteFolderDto(
    Guid Id,
    Guid? ParentFolderId,
    string Name,
    bool AllowAgentRead,
    bool EffectiveAllowAgentRead,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record NoteTagCountDto(
    string Tag,
    int Count);

public sealed record NoteSummaryDto(
    Guid Id,
    Guid? FolderId,
    string Title,
    IReadOnlyList<string> Tags,
    string Excerpt,
    bool AllowAgentRead,
    bool EffectiveAllowAgentRead,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? UniversityCourseId = null,
    Guid? UniversityActivityItemId = null,
    Guid? CalendarEventId = null,
    DateTimeOffset? CalendarOccurrenceStartAt = null,
    string LinkKind = NoteLinkKinds.General,
    string? LinkLabel = null,
    string? DailyDate = null);

public sealed record NoteDetailDto(
    Guid Id,
    Guid? FolderId,
    string Title,
    string Content,
    IReadOnlyList<string> Tags,
    string ContentHash,
    int Version,
    bool AllowAgentRead,
    bool EffectiveAllowAgentRead,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? UniversityCourseId = null,
    Guid? UniversityActivityItemId = null,
    Guid? CalendarEventId = null,
    DateTimeOffset? CalendarOccurrenceStartAt = null,
    string LinkKind = NoteLinkKinds.General,
    string? LinkLabel = null,
    string? DailyDate = null);

public sealed record UniversityItemNotesTreeDto(
    Guid Id,
    string Title,
    int? SequenceNumber,
    NoteSummaryDto? Note,
    IReadOnlyList<AttachmentDto> Artifacts,
    IReadOnlyList<NoteSummaryDto>? Notes = null);

public sealed record UniversityActivityNotesTreeDto(
    Guid Id,
    string ActivityType,
    string Title,
    IReadOnlyList<UniversityItemNotesTreeDto> Items);

public sealed record UniversityCourseNotesTreeDto(
    Guid Id,
    string Name,
    string? Code,
    string Color,
    string Semester,
    IReadOnlyList<NoteSummaryDto> Notes,
    IReadOnlyList<AttachmentDto> Artifacts,
    IReadOnlyList<UniversityActivityNotesTreeDto> Activities);

public sealed record UniversitySemesterNotesTreeDto(
    string Semester,
    IReadOnlyList<UniversityCourseNotesTreeDto> Courses);

public sealed record EventOccurrenceNotesDto(
    DateTimeOffset OccurrenceStartAt,
    NoteSummaryDto? Note,
    IReadOnlyList<AttachmentDto> Artifacts);

public sealed record EventNotesGroupDto(
    Guid CalendarEventId,
    string Title,
    DateTimeOffset StartAt,
    bool IsRecurring,
    NoteSummaryDto? SharedNote,
    IReadOnlyList<AttachmentDto> SharedArtifacts,
    IReadOnlyList<EventOccurrenceNotesDto> Occurrences);

public sealed record NotesTreeResponse(
    IReadOnlyList<NoteFolderDto> Folders,
    IReadOnlyList<NoteSummaryDto> Notes,
    IReadOnlyList<NoteTagCountDto> Tags,
    IReadOnlyList<UniversitySemesterNotesTreeDto> University,
    IReadOnlyList<EventNotesGroupDto> Events,
    IReadOnlyList<NoteSummaryDto> Daily);

public sealed record NoteConflictResponse(
    string Error,
    NoteDetailDto CurrentNote);

public sealed record CreateNoteFolderRequest(
    string Name,
    Guid? ParentFolderId = null,
    bool AllowAgentRead = true);

public sealed record UpdateNoteFolderRequest(
    string? Name = null,
    Guid? ParentFolderId = null,
    bool MoveToRoot = false,
    bool? AllowAgentRead = null);

public sealed record CreateNoteRequest(
    string? Title = null,
    Guid? FolderId = null,
    string? Content = null,
    bool AllowAgentRead = true,
    Guid? UniversityCourseId = null,
    Guid? UniversityActivityItemId = null);

public sealed record UpdateNoteRequest(
    string? Title = null,
    string? Content = null,
    Guid? FolderId = null,
    bool MoveToRoot = false,
    bool? AllowAgentRead = null,
    int? ExpectedVersion = null);

public sealed record OpenLinkedNoteRequest(
    string Kind,
    Guid? UniversityActivityItemId = null,
    Guid? CalendarEventId = null,
    DateTimeOffset? OccurrenceStartAt = null,
    string? DailyDate = null);
