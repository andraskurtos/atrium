namespace Atrium.Api.Notes;

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
    DateTimeOffset UpdatedAt);

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
    DateTimeOffset UpdatedAt);

public sealed record NotesTreeResponse(
    IReadOnlyList<NoteFolderDto> Folders,
    IReadOnlyList<NoteSummaryDto> Notes,
    IReadOnlyList<NoteTagCountDto> Tags);

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
    bool AllowAgentRead = true);

public sealed record UpdateNoteRequest(
    string? Title = null,
    string? Content = null,
    Guid? FolderId = null,
    bool MoveToRoot = false,
    bool? AllowAgentRead = null,
    int? ExpectedVersion = null);

