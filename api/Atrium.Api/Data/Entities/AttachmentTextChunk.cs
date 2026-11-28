namespace Atrium.Api.Data.Entities;

public sealed class AttachmentTextChunk
{
    public Guid Id { get; set; }
    public Guid AttachmentId { get; set; }
    public Guid UserId { get; set; }
    public int ChunkIndex { get; set; }
    public string PageLabel { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int CharCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Attachment Attachment { get; set; } = null!;
    public User User { get; set; } = null!;
}

