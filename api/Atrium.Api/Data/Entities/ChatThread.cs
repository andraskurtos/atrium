namespace Atrium.Api.Data.Entities;

public sealed class ChatThread
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? CourseId { get; set; }
    public Guid? LastModelId { get; set; }
    public string Title { get; set; } = "New Chat";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public UniversityCourse? Course { get; set; }
    public AiModel? LastModel { get; set; }
    public ICollection<ChatMessageEntity> Messages { get; } = [];
    public ICollection<ChatAttachment> Attachments { get; } = [];
}

