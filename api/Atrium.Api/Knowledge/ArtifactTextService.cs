using Atrium.Api.Attachments;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Atrium.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Atrium.Api.Knowledge;

public sealed partial class ArtifactTextService(
    AppDbContext db,
    IAttachmentStorage storage,
    IArtifactTextExtractor extractor,
    ILogger<ArtifactTextService> logger)
{
    private static readonly TimeSpan ExtractionTimeout = TimeSpan.FromSeconds(5);

    public async Task ExtractAndPersistAsync(
        Attachment attachment,
        Stream contentStream,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        ArgumentNullException.ThrowIfNull(contentStream);

        if (!string.Equals(attachment.Kind, AttachmentKinds.Artifact, StringComparison.Ordinal))
        {
            return;
        }

        ArtifactExtractionResult result;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ExtractionTimeout);
            if (contentStream.CanSeek)
            {
                contentStream.Position = 0;
            }

            result = extractor.Extract(contentStream, attachment.FileName, attachment.ContentType, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            LogExtractionTimedOut(logger, attachment.Id, attachment.FileName);
            result = new ArtifactExtractionResult(AttachmentExtractionStatuses.Failed, null, []);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogExtractionFailed(logger, attachment.Id, attachment.FileName, ex);
            result = new ArtifactExtractionResult(AttachmentExtractionStatuses.Failed, null, []);
        }

        await PersistExtractionResultAsync(attachment, result, ct);
    }

    public async Task EnsureExtractedAsync(Guid attachmentId, Guid userId, CancellationToken ct)
    {
        var attachment = await db.Attachments
            .FirstOrDefaultAsync(
                a => a.Id == attachmentId
                    && a.UserId == userId
                    && a.DeletedAt == null
                    && a.Kind == AttachmentKinds.Artifact,
                ct);

        if (attachment is null
            || !string.Equals(attachment.TextExtractionStatus, AttachmentExtractionStatuses.Pending, StringComparison.Ordinal))
        {
            return;
        }

        await using var stream = await storage.OpenReadAsync(attachment.StorageKey, ct);
        if (stream is null)
        {
            attachment.TextExtractionStatus = AttachmentExtractionStatuses.Failed;
            attachment.TextExtractedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        await ExtractAndPersistAsync(attachment, stream, ct);
    }

    private async Task PersistExtractionResultAsync(
        Attachment attachment,
        ArtifactExtractionResult result,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var existingChunks = await db.AttachmentTextChunks
            .Where(c => c.AttachmentId == attachment.Id)
            .ToListAsync(ct);
        if (existingChunks.Count > 0)
        {
            db.AttachmentTextChunks.RemoveRange(existingChunks);
        }

        foreach (var chunk in result.Chunks)
        {
            db.AttachmentTextChunks.Add(new AttachmentTextChunk
            {
                Id = Guid.NewGuid(),
                AttachmentId = attachment.Id,
                UserId = attachment.UserId,
                ChunkIndex = chunk.ChunkIndex,
                PageLabel = chunk.PageLabel,
                Content = chunk.Content,
                CharCount = chunk.Content.Length,
                CreatedAt = now,
            });
        }

        attachment.TextExtractionStatus = result.Status;
        attachment.ExtractedPageCount = result.PageCount;
        attachment.TextExtractedAt = now;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Concurrent lazy extraction race: reload and ignore duplicate-key collision
            db.ChangeTracker.Clear();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Text extraction timed out for attachment {AttachmentId} ({FileName}).")]
    private static partial void LogExtractionTimedOut(ILogger logger, Guid attachmentId, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Text extraction failed for attachment {AttachmentId} ({FileName}).")]
    private static partial void LogExtractionFailed(ILogger logger, Guid attachmentId, string fileName, Exception exception);
}

public sealed partial class ArtifactTextBackfillService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ArtifactTextBackfillService> logger) : BackgroundService
{
    private const int BatchSize = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Atrium:Knowledge:EnableBackfill", true))
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var textService = scope.ServiceProvider.GetRequiredService<ArtifactTextService>();

                var pending = await db.Attachments
                    .AsNoTracking()
                    .Where(a => a.DeletedAt == null
                        && a.Kind == AttachmentKinds.Artifact
                        && a.TextExtractionStatus == AttachmentExtractionStatuses.Pending)
                    .Select(a => new { a.Id, a.UserId })
                    .Take(BatchSize)
                    .ToListAsync(stoppingToken);

                if (pending.Count == 0)
                {
                    break;
                }

                foreach (var item in pending)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    await textService.EnsureExtractedAsync(item.Id, item.UserId, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            LogBackfillFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Artifact text backfill stopped with an error.")]
    private static partial void LogBackfillFailed(ILogger logger, Exception exception);
}

