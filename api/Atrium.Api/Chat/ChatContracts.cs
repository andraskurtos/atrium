namespace Atrium.Api.Chat;

public sealed record ChatMessageInput(
    string Role,
    string Content);

public sealed record ChatStreamRequest(
    IReadOnlyList<ChatMessageInput> Messages,
    Guid? CourseId = null,
    Guid? ThreadId = null);

public sealed record ChatCitationDto(
    string SourceType,
    Guid Id,
    string Title,
    string? LinkLabel,
    string? PageLabel);

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
    DateTimeOffset CreatedAt);

public sealed record ChatThreadDetailDto(
    Guid Id,
    Guid? CourseId,
    string? CourseLabel,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ChatMessageDto> Messages);

public sealed record ChatStreamSummary(
    string AssistantText,
    string? ReasoningText,
    IReadOnlyList<ChatCitationDto> Citations,
    bool HadError);
