namespace Atrium.Api.Data.Entities;

public sealed class ChatMessageEntity
{
    public Guid Id { get; set; }
    public Guid ThreadId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public string? Reasoning { get; set; }
    public string? CitationsJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ChatThread Thread { get; set; } = null!;
    public User User { get; set; } = null!;
    public ICollection<ChatAttachment> Attachments { get; } = [];
}

