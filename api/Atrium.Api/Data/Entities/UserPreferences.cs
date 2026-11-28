namespace Atrium.Api.Data.Entities;

public sealed class UserPreferences
{
    public Guid UserId { get; set; }
    public string Theme { get; set; } = "system";
    public string WeekStartsOn { get; set; } = "monday";
    public string TimeFormat { get; set; } = "24h";
    public bool RequireAgentApproval { get; set; } = true;
    public bool MorningBrief { get; set; } = true;
    public bool EventReminders { get; set; } = true;
    public string? AiInferenceUrl { get; set; }
    public string? AiModel { get; set; }
    public string? AiApiKeyEncrypted { get; set; }

    public User User { get; set; } = null!;
}
