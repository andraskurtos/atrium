namespace Atrium.Api.Data.Entities;

public sealed class CalendarEvent
{
    public Guid Id { get; set; }
    public Guid CalendarId { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public bool IsAllDay { get; set; }
    public string? Timezone { get; set; }
    public string Status { get; set; } = "confirmed";
    public string? ExternalUid { get; set; }
    public string? RecurrenceRule { get; set; }
    public Guid? RecurrenceMasterId { get; set; }
    public DateTimeOffset? RecurrenceOriginalStartAt { get; set; }
    public bool IsRecurrenceExceptionDeleted { get; set; }
    public string? Source { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public UserCalendar Calendar { get; set; } = null!;
    public User User { get; set; } = null!;
    public CalendarEvent? RecurrenceMaster { get; set; }
    public ICollection<CalendarEvent> RecurrenceOverrides { get; } = [];
}
