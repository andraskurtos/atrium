namespace Atrium.Api.Data.Entities;

public sealed class UniversityCourse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Semester { get; set; }
    public string? Description { get; set; }
    public string? Instructor { get; set; }
    public decimal? Credits { get; set; }
    public string Color { get; set; } = "#3b6ea5";
    public string Status { get; set; } = "active";
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public ICollection<UniversityCourseActivity> Activities { get; } = [];
}
