namespace Atrium.Api.Chat;

public sealed record ChatMessageInput(
    string Role,
    string Content);

public sealed record ChatStreamRequest(
    IReadOnlyList<ChatMessageInput> Messages,
    Guid? CourseId = null);

public sealed record ChatCitationDto(
    string SourceType,
    Guid Id,
    string Title,
    string? LinkLabel,
    string? PageLabel);

