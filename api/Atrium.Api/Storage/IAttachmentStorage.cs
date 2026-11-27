using Microsoft.Extensions.Options;

namespace Atrium.Api.Storage;

public interface IAttachmentStorage
{
    Task SaveAsync(string storageKey, Stream content, CancellationToken ct);
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct);
    Task DeleteAsync(string storageKey, CancellationToken ct);
}

public sealed class LocalDiskAttachmentStorage : IAttachmentStorage
{
    private readonly string _rootPath;

    public LocalDiskAttachmentStorage(IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        var configured = options.Value.UploadsPath;
        var rawPath = string.IsNullOrWhiteSpace(configured) ? "uploads" : configured.Trim();
        _rootPath = Path.GetFullPath(rawPath, environment.ContentRootPath);
        if (!_rootPath.EndsWith(Path.DirectorySeparatorChar))
        {
            _rootPath += Path.DirectorySeparatorChar;
        }
    }

    public async Task SaveAsync(string storageKey, Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var fullPath = ResolveSafePath(storageKey);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var fileStream = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            useAsync: true);
        await content.CopyToAsync(fileStream, ct);
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fullPath = ResolveSafePath(storageKey);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            useAsync: true);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fullPath = ResolveSafePath(storageKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private string ResolveSafePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("Storage key must not be empty.", nameof(storageKey));
        }

        var normalized = storageKey.Replace('\\', '/').TrimStart('/');
        var combined = Path.GetFullPath(Path.Combine(_rootPath, normalized));
        if (!combined.StartsWith(_rootPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Storage key resolved outside the configured uploads root.");
        }

        return combined;
    }
}

