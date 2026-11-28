namespace Atrium.Api.Attachments;

public static class AttachmentKinds
{
    public const string Artifact = "artifact";
    public const string InlineImage = "inline_image";
}

public static class AttachmentExtractionStatuses
{
    public const string Pending = "pending";
    public const string Completed = "completed";
    public const string Empty = "empty";
    public const string Unsupported = "unsupported";
    public const string Failed = "failed";
}

public sealed record AttachmentDto(
    Guid Id,
    string Kind,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Url,
    DateTimeOffset CreatedAt,
    string TextExtractionStatus = AttachmentExtractionStatuses.Pending,
    int? ExtractedPageCount = null);

