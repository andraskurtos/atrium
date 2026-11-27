namespace Atrium.Api.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Atrium:Storage";

    public string UploadsPath { get; set; } = "uploads";
    public long MaxArtifactBytes { get; set; } = 25 * 1024 * 1024;
    public long MaxImageBytes { get; set; } = 10 * 1024 * 1024;
}

