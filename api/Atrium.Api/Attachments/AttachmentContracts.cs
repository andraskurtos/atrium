namespace Atrium.Api.Attachments;

public static class AttachmentKinds
{
    public const string Artifact = "artifact";
    public const string InlineImage = "inline_image";
}

public sealed record AttachmentDto(
    Guid Id,
    string Kind,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Url,
    DateTimeOffset CreatedAt);

