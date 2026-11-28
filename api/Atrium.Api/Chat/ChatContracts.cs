namespace Atrium.Api.Chat;

public sealed record ChatMessageInput(
    string Role,
    string Content);

public sealed record ChatStreamRequest(
    IReadOnlyList<ChatMessageInput> Messages,
    Guid? CourseId = null,
    Guid? ThreadId = null,
    Guid? ModelId = null,
    IReadOnlyList<Guid>? AttachmentIds = null,
    bool EnableWebSearch = true,
    bool AllowWriteTools = true);

public sealed record ChatCitationDto(
    string SourceType,
    Guid Id,
    string Title,
    string? LinkLabel,
    string? PageLabel);

public sealed record ChatAttachmentDto(
    Guid Id,
    string FileName,
    string ContentType,
    string Kind,
    long SizeBytes,
    string? Summary,
    string Url,
    DateTimeOffset CreatedAt);

public sealed record ChatThreadSummaryDto(
    Guid Id,
    Guid? CourseId,
    string? CourseLabel,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int MessageCount);

public sealed record ChatMessageDto(
    Guid Id,
    string Role,
    string Content,
    string? Reasoning,
    IReadOnlyList<ChatCitationDto> Citations,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ChatAttachmentDto>? Attachments = null);

public sealed record ChatThreadDetailDto(
    Guid Id,
    Guid? CourseId,
    string? CourseLabel,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ChatMessageDto> Messages,
    Guid? LastModelId = null);

public sealed record ChatStreamSummary(
    string AssistantText,
    string? ReasoningText,
    IReadOnlyList<ChatCitationDto> Citations,
    bool HadError);
