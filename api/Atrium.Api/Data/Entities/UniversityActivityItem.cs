namespace Atrium.Api.Data.Entities;

public sealed class UniversityActivityItem
{
    public Guid Id { get; set; }
    public Guid ActivityId { get; set; }
    public Guid CourseId { get; set; }
    public Guid UserId { get; set; }
    public Guid? CalendarEventId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Theme { get; set; }
    public string? Notes { get; set; }
    public int? SequenceNumber { get; set; }
    public string AttendanceStatus { get; set; } = "pending";
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public UniversityCourseActivity Activity { get; set; } = null!;
    public UniversityCourse Course { get; set; } = null!;
    public User User { get; set; } = null!;
    public CalendarEvent? CalendarEvent { get; set; }
}
