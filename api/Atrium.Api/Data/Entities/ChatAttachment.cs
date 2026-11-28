namespace Atrium.Api.Data.Entities;

public sealed class ChatAttachment
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ThreadId { get; set; }
    public Guid? MessageId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public string Kind { get; set; } = "document";
    public long SizeBytes { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string? ExtractedText { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User User { get; set; } = null!;
    public ChatThread? Thread { get; set; }
    public ChatMessageEntity? Message { get; set; }
}

