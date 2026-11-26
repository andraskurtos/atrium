namespace Atrium.Api.Days;

public sealed record DailyCheckItemDto(
    Guid Id,
    string Date,
    string Title,
    bool IsCompleted,
    DateTimeOffset? CompletedAt,
    string? DueTime,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DailyNoteDto(
    Guid Id,
    string Date,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DailyTrackerResponse(
    string Date,
    IReadOnlyList<DailyCheckItemDto> ChecklistItems,
    DailyNoteDto? Note);

public sealed record CreateDailyCheckItemRequest(
    string Title,
    string? DueTime = null);

public sealed record UpdateDailyCheckItemRequest(
    string? Title = null,
    bool? IsCompleted = null,
    string? Date = null,
    string? DueTime = null,
    bool ClearDueTime = false);

public sealed record UpsertDailyNoteRequest(
    string Content);

public sealed record UpsertDailyNoteResponse(
    string Date,
    DailyNoteDto? Note);

