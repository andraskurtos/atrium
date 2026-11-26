namespace Atrium.Api.Calendar;

public sealed record CalendarDto(
    Guid Id,
    string Name,
    string Description,
    string Color,
    string Timezone,
    bool IsDefault,
    bool IsVisible,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateCalendarRequest(
    string Name,
    string? Description = null,
    string? Color = null,
    string? Timezone = null,
    bool IsDefault = false,
    bool IsVisible = true);

public sealed record UpdateCalendarRequest(
    string Name,
    string? Description = null,
    string? Color = null,
    string? Timezone = null,
    bool? IsDefault = null,
    bool? IsVisible = null);

public sealed record CalendarEventDto(
    Guid Id,
    Guid CalendarId,
    string CalendarName,
    string CalendarColor,
    string Title,
    string? Description,
    string? Location,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    bool IsAllDay,
    string? Timezone,
    string Status,
    string? ExternalUid,
    string? RecurrenceRule,
    string? Source,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateEventRequest(
    Guid? CalendarId,
    string Title,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    bool IsAllDay = false,
    string? Location = null,
    string? Description = null,
    string? Timezone = null,
    string? Status = null,
    string? RecurrenceRule = null,
    string? Source = null);

public sealed record UpdateEventRequest(
    Guid CalendarId,
    string Title,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    bool IsAllDay = false,
    string? Location = null,
    string? Description = null,
    string? Timezone = null,
    string? Status = null,
    string? RecurrenceRule = null);

public sealed record IcsImportResponse(
    CalendarDto Calendar,
    int ImportedCount,
    int UpdatedCount,
    int SkippedCount,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<CalendarEventDto> Events);

public sealed record ParsedIcsEvent(
    string? Uid,
    string Title,
    string? Description,
    string? Location,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    bool IsAllDay,
    string? Timezone,
    string Status,
    string? RecurrenceRule);

public sealed record ParsedIcsCalendar(
    string? CalendarName,
    string? CalendarDescription,
    string? Timezone,
    IReadOnlyList<ParsedIcsEvent> Events,
    IReadOnlyList<string> Warnings);
