namespace Atrium.Api.Data.Entities;

public sealed class UniversityCourseActivity
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public Guid UserId { get; set; }
    public string ActivityType { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? DefaultLocation { get; set; }
    public int? MinAttendanceCount { get; set; }
    public decimal? MinAttendancePercent { get; set; }
    public int? PlannedTotalCount { get; set; }
    public int SortOrder { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public UniversityCourse Course { get; set; } = null!;
    public User User { get; set; } = null!;
    public ICollection<UniversityActivityItem> Items { get; } = [];
}
