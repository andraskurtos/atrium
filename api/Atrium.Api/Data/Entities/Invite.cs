namespace Atrium.Api.Data.Entities;

public sealed class Invite
{
    public Guid Id { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string CodePrefix { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public string? Label { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public Guid? UsedByUserId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User CreatedByUser { get; set; } = null!;
    public User? UsedByUser { get; set; }
}
