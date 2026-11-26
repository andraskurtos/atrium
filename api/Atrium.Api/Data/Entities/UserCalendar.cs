namespace Atrium.Api.Data.Entities;

public sealed class UserCalendar
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Color { get; set; } = "#c25438";
    public string Timezone { get; set; } = "UTC";
    public bool IsDefault { get; set; }
    public bool IsVisible { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public ICollection<CalendarEvent> Events { get; } = [];
}
