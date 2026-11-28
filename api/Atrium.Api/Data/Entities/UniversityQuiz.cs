namespace Atrium.Api.Data.Entities;

public sealed class UniversityQuiz
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CourseId { get; set; }
    public Guid? UniversityActivityItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string QuestionsJson { get; set; } = "[]";
    public int QuestionCount { get; set; }
    public int AttemptCount { get; set; }
    public int? LastScore { get; set; }
    public int? BestScore { get; set; }
    public DateTimeOffset? LastPlayedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public UniversityCourse Course { get; set; } = null!;
    public UniversityActivityItem? ActivityItem { get; set; }
}

