namespace Atrium.Api.Data.Entities;

public sealed class NoteTag
{
    public Guid NoteId { get; set; }
    public Guid UserId { get; set; }
    public string Tag { get; set; } = string.Empty;

    public Note Note { get; set; } = null!;
    public User User { get; set; } = null!;
}

