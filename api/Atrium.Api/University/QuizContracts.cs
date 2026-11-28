namespace Atrium.Api.University;

public sealed record QuizQuestionDto(
    string Prompt,
    IReadOnlyList<string> Options,
    int CorrectIndex,
    string Explanation,
    string? SourceReference = null);

public sealed record UniversityQuizSummaryDto(
    Guid Id,
    Guid CourseId,
    Guid? UniversityActivityItemId,
    string Title,
    string? Description,
    int QuestionCount,
    int AttemptCount,
    int? LastScore,
    int? BestScore,
    DateTimeOffset? LastPlayedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UniversityQuizDetailDto(
    Guid Id,
    Guid CourseId,
    Guid? UniversityActivityItemId,
    string Title,
    string? Description,
    int QuestionCount,
    int AttemptCount,
    int? LastScore,
    int? BestScore,
    DateTimeOffset? LastPlayedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<QuizQuestionDto> Questions);

public sealed record RecordQuizAttemptRequest(
    int Score);

