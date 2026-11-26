using Atrium.Api.Calendar;

namespace Atrium.Api.University;

public static class UniversityConstants
{
    public const string CalendarModuleKey = "university";
    public const string DefaultCalendarName = "University";
    public const string DefaultCalendarDescription = "University lectures, practices, labs, assignments, and exams";
    public const string DefaultCalendarColor = "#3b6ea5";
    public const string CalendarEventSource = "University";

    public static readonly IReadOnlyList<string> AllActivityTypes =
    [
        "lecture",
        "practice",
        "laboratory",
        "homework_assignment",
        "semester_assignment",
        "midterm",
        "exam",
    ];

    public static readonly IReadOnlySet<string> AttendanceCapableActivityTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "lecture",
        "practice",
        "laboratory",
        "homework_assignment",
    };

    public static readonly IReadOnlySet<string> ValidActivityTypes = new HashSet<string>(AllActivityTypes, StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlySet<string> ValidCourseStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "completed",
        "archived",
    };

    public static readonly IReadOnlySet<string> ValidAttendanceStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "attended",
        "missed",
        "excused",
    };

    public static string GetDefaultActivityLabel(string activityType) => activityType switch
    {
        "lecture" => "Lecture",
        "practice" => "Practice",
        "laboratory" => "Laboratory",
        "homework_assignment" => "Homework Assignment",
        "semester_assignment" => "Semester Assignment",
        "midterm" => "Midterm",
        "exam" => "Exam",
        _ => activityType,
    };

    public static bool SupportsAttendance(string activityType) =>
        AttendanceCapableActivityTypes.Contains(activityType);
}

public sealed record AttendanceSummaryDto(
    bool SupportsAttendance,
    bool HasRequirement,
    int AttendedCount,
    int MissedCount,
    int ExcusedCount,
    int PendingCount,
    int TotalItemsCount,
    int EffectiveTotalCount,
    int? MinAttendanceCount,
    decimal? MinAttendancePercent,
    int? PlannedTotalCount,
    int? RequiredCount,
    int? RemainingRequired,
    int? RemainingOpportunities,
    int? MaxAllowedMissesLeft,
    decimal? CurrentRatePercent,
    decimal? RecordedRatePercent,
    string Status);

public sealed record UniversityActivityItemDto(
    Guid Id,
    Guid ActivityId,
    Guid CourseId,
    Guid? CalendarEventId,
    string Title,
    string? Theme,
    string? Notes,
    int? SequenceNumber,
    string AttendanceStatus,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    bool IsAllDay,
    string? Location,
    string? MetadataJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UniversityCourseActivityDto(
    Guid Id,
    Guid CourseId,
    string ActivityType,
    string? Title,
    string? Description,
    string? DefaultLocation,
    int? MinAttendanceCount,
    decimal? MinAttendancePercent,
    int? PlannedTotalCount,
    int SortOrder,
    string? MetadataJson,
    AttendanceSummaryDto Attendance,
    IReadOnlyList<UniversityActivityItemDto> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UniversityCourseDto(
    Guid Id,
    string Name,
    string? Code,
    string? Semester,
    string? Description,
    string? Instructor,
    decimal? Credits,
    string Color,
    string Status,
    string? MetadataJson,
    IReadOnlyList<UniversityCourseActivityDto> Activities,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UniversityWorkspaceResponse(
    CalendarDto Calendar,
    IReadOnlyList<string> Semesters,
    IReadOnlyList<UniversityCourseDto> Courses);

public sealed record CreateCourseRequest(
    string Name,
    string? Code = null,
    string? Semester = null,
    string? Description = null,
    string? Instructor = null,
    decimal? Credits = null,
    string? Color = null,
    string? Status = null,
    string? MetadataJson = null,
    IReadOnlyList<CreateActivityRequest>? Activities = null);

public sealed record UpdateCourseRequest(
    string Name,
    string? Code = null,
    string? Semester = null,
    string? Description = null,
    string? Instructor = null,
    decimal? Credits = null,
    string? Color = null,
    string? Status = null,
    string? MetadataJson = null);

public sealed record CreateActivityRequest(
    string ActivityType,
    string? Title = null,
    string? Description = null,
    string? DefaultLocation = null,
    int? MinAttendanceCount = null,
    decimal? MinAttendancePercent = null,
    int? PlannedTotalCount = null,
    int? SortOrder = null,
    string? MetadataJson = null);

public sealed record UpdateActivityRequest(
    string? Title = null,
    string? Description = null,
    string? DefaultLocation = null,
    int? MinAttendanceCount = null,
    decimal? MinAttendancePercent = null,
    int? PlannedTotalCount = null,
    int? SortOrder = null,
    string? MetadataJson = null);

public sealed record ActivityItemScheduleInput(
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    bool IsAllDay = false,
    string? Location = null);

public sealed record CreateActivityItemRequest(
    string Title,
    string? Theme = null,
    string? Notes = null,
    int? SequenceNumber = null,
    string? AttendanceStatus = null,
    ActivityItemScheduleInput? Schedule = null,
    string? MetadataJson = null);

public sealed record UpdateActivityItemRequest(
    string Title,
    string? Theme = null,
    string? Notes = null,
    int? SequenceNumber = null,
    string? AttendanceStatus = null,
    ActivityItemScheduleInput? Schedule = null,
    bool RemoveCalendarEvent = false,
    string? MetadataJson = null);

public sealed record UpdateAttendanceStatusRequest(
    string AttendanceStatus);

public sealed record UpdateAttendanceStatusResponse(
    UniversityActivityItemDto Item,
    AttendanceSummaryDto Attendance);

public sealed record GenerateActivitySeriesRequest(
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    int Count,
    int IntervalWeeks = 1,
    string? TitlePrefix = null,
    int StartNumber = 1,
    bool IsAllDay = false,
    string? Location = null,
    string? DefaultTheme = null);

public sealed record GenerateActivitySeriesResponse(
    IReadOnlyList<UniversityActivityItemDto> CreatedItems,
    AttendanceSummaryDto Attendance);
