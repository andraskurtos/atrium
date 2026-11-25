namespace Atrium.Api.Data.Entities;

public sealed class RefreshSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }
    public DateTimeOffset FamilyCreatedAt { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string DeviceLabel { get; set; } = "Unknown device";
    public string IpAddress { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastActiveAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedBySessionId { get; set; }

    public User User { get; set; } = null!;
}
