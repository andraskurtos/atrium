namespace Atrium.Api.Data.Entities;

public sealed class NoteFolder
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ParentFolderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool AllowAgentRead { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public User User { get; set; } = null!;
    public NoteFolder? ParentFolder { get; set; }
    public ICollection<NoteFolder> ChildFolders { get; } = [];
    public ICollection<Note> Notes { get; } = [];
}

