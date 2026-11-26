namespace Atrium.Api.Data.Entities;

public sealed class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public string Timezone { get; set; } = "UTC";
    public string? AvatarDataUrl { get; set; }
    public string Role { get; set; } = "Member";
    public bool IsActive { get; set; } = true;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTimeOffset PasswordUpdatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public UserPreferences? Preferences { get; set; }
    public ICollection<RefreshSession> RefreshSessions { get; } = [];
    public ICollection<AgentToken> AgentTokens { get; } = [];
    public ICollection<UserCalendar> Calendars { get; } = [];
}
