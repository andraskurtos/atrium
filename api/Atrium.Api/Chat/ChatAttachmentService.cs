using System.Globalization;
using System.Text;
using System.Text.Json;
using Atrium.Api.Calendar;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Atrium.Api.Knowledge;
using Atrium.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Atrium.Api.Chat;

public static class ChatAttachmentKinds
{
    public const string Image = "image";
    public const string Ics = "ics";
    public const string Document = "document";
}

public sealed record ChatAttachmentUploadResult(
    bool Success,
    string? Error,
    ChatAttachmentDto? Attachment);

public sealed class ChatAttachmentService(
    AppDbContext db,
    IAttachmentStorage storage,
    IArtifactTextExtractor textExtractor)
{
    public const long MaxFileSizeBytes = 15 * 1024 * 1024;
    public const int MaxAttachmentsPerMessage = 5;
    public const long MaxVisionImageBytes = 5 * 1024 * 1024;
    public const long MaxHistoryImageBudgetBytes = 8 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp",
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".pptx", ".txt", ".md", ".csv", ".json",
    };

    public async Task<ChatAttachmentUploadResult> UploadAsync(
        Guid userId,
        IFormFile file,
        string userTimezone,
        CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
        {
            return new ChatAttachmentUploadResult(false, "File is empty.", null);
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return new ChatAttachmentUploadResult(false, "File exceeds the 15 MB upload limit.", null);
        }

        var fileName = Path.GetFileName(file.FileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "attachment";
        }

        if (fileName.Length > 255)
        {
            fileName = fileName[..255];
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        string kind;
        string contentType;

        if (ext == ".ics")
        {
            kind = ChatAttachmentKinds.Ics;
            contentType = "text/calendar";
        }
        else if (ImageExtensions.Contains(ext))
        {
            kind = ChatAttachmentKinds.Image;
            contentType = ext switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg",
            };
        }
        else if (DocumentExtensions.Contains(ext))
        {
            kind = ChatAttachmentKinds.Document;
            contentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType.Trim();
            if (contentType.Length > 120) contentType = contentType[..120];
        }
        else
        {
            return new ChatAttachmentUploadResult(
                false,
                "Unsupported file type. Allowed: .ics, .pdf, .docx, .pptx, .txt, .md, .csv, .json, .png, .jpg, .webp.",
                null);
        }

        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        string? extractedText = null;
        string? metadataJson = null;

        if (kind == ChatAttachmentKinds.Ics)
        {
            try
            {
                var icsText = Encoding.UTF8.GetString(buffer.ToArray());
                var parsed = IcsParser.Parse(icsText, string.IsNullOrWhiteSpace(userTimezone) ? "UTC" : userTimezone);
                var distinctSummaries = parsed.Events
                    .GroupBy(e => e.Title.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => new { summary = g.First().Title.Trim(), count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .ThenBy(x => x.summary, StringComparer.OrdinalIgnoreCase)
                    .Take(25)
                    .ToList();

                DateTimeOffset? minStart = parsed.Events.Count > 0 ? parsed.Events.Min(e => e.StartAt) : null;
                DateTimeOffset? maxEnd = parsed.Events.Count > 0 ? parsed.Events.Max(e => e.EndAt) : null;
                var summaryText = parsed.Events.Count == 0
                    ? "Empty calendar (0 events)"
                    : $"{parsed.Events.Count} events ({minStart?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} to {maxEnd?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})";

                metadataJson = JsonSerializer.Serialize(
                    new
                    {
                        summary = summaryText,
                        eventCount = parsed.Events.Count,
                        dateRangeStart = minStart?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        dateRangeEnd = maxEnd?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        timezone = parsed.Timezone ?? userTimezone,
                        distinctSummaries,
                    },
                    JsonOptions);
            }
            catch (FormatException ex)
            {
                return new ChatAttachmentUploadResult(false, $"Invalid ICS file: {ex.Message}", null);
            }
        }
        else if (kind == ChatAttachmentKinds.Document)
        {
            var extracted = textExtractor.Extract(buffer, fileName, contentType, ct);
            buffer.Position = 0;
            if (extracted.Chunks.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var chunk in extracted.Chunks)
                {
                    if (sb.Length > 0) sb.Append("\n\n");
                    sb.Append('[').Append(chunk.PageLabel).Append("]\n").Append(chunk.Content);
                    if (sb.Length >= DefaultArtifactTextExtractor.MaxTotalChars)
                    {
                        sb.Length = DefaultArtifactTextExtractor.MaxTotalChars;
                        break;
                    }
                }

                extractedText = sb.ToString();
            }

            var pagePart = extracted.PageCount.HasValue ? $"{extracted.PageCount.Value} pages, " : string.Empty;
            var summaryText = extractedText is not null
                ? $"{pagePart}{extractedText.Length:N0} chars extracted"
                : $"Status: {extracted.Status}";

            metadataJson = JsonSerializer.Serialize(
                new
                {
                    summary = summaryText,
                    pageCount = extracted.PageCount,
                    charCount = extractedText?.Length ?? 0,
                    status = extracted.Status,
                },
                JsonOptions);
        }
        else
        {
            metadataJson = JsonSerializer.Serialize(
                new
                {
                    summary = $"Image ({file.Length / 1024.0:F0} KB)",
                },
                JsonOptions);
        }

        var id = Guid.NewGuid();
        var storageKey = $"chat/{userId:N}/{id:N}{ext}";
        buffer.Position = 0;
        await storage.SaveAsync(storageKey, buffer, ct);

        var entity = new ChatAttachment
        {
            Id = id,
            UserId = userId,
            FileName = fileName,
            ContentType = contentType,
            Kind = kind,
            SizeBytes = file.Length,
            StorageKey = storageKey,
            ExtractedText = extractedText,
            MetadataJson = metadataJson,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.ChatAttachments.Add(entity);
        await db.SaveChangesAsync(ct);

        return new ChatAttachmentUploadResult(true, null, ToDto(entity));
    }

    public async Task<IReadOnlyList<ChatAttachment>> BindToMessageAsync(
        Guid userId,
        Guid threadId,
        Guid messageId,
        IReadOnlyList<Guid>? attachmentIds,
        CancellationToken ct)
    {
        if (attachmentIds is null || attachmentIds.Count == 0)
        {
            return [];
        }

        var distinctIds = attachmentIds.Distinct().Take(MaxAttachmentsPerMessage).ToList();
        var attachments = await db.ChatAttachments
            .Where(a => a.UserId == userId
                && distinctIds.Contains(a.Id)
                && (a.ThreadId == null || a.ThreadId == threadId))
            .ToListAsync(ct);

        foreach (var att in attachments)
        {
            att.ThreadId = threadId;
            att.MessageId ??= messageId;
        }

        await db.SaveChangesAsync(ct);
        return attachments;
    }

    public async Task<(bool Success, string? Error, bool HasImages)> ValidateAttachmentIdsAsync(
        Guid userId,
        Guid? threadId,
        IReadOnlyList<Guid>? attachmentIds,
        CancellationToken ct)
    {
        if (attachmentIds is null || attachmentIds.Count == 0)
        {
            return (true, null, false);
        }

        var distinct = attachmentIds.Distinct().ToList();
        if (distinct.Count > MaxAttachmentsPerMessage)
        {
            return (false, $"At most {MaxAttachmentsPerMessage} attachments can be sent per message.", false);
        }

        var found = await db.ChatAttachments
            .AsNoTracking()
            .Where(a => a.UserId == userId
                && distinct.Contains(a.Id)
                && (a.ThreadId == null || (threadId.HasValue && a.ThreadId == threadId.Value)))
            .Select(a => new { a.Id, a.Kind })
            .ToListAsync(ct);

        if (found.Count != distinct.Count)
        {
            return (false, "One or more attached files were not found or cannot be attached to this conversation.", false);
        }

        var hasImages = found.Any(a => a.Kind == ChatAttachmentKinds.Image);
        return (true, null, hasImages);
    }

    public async Task<bool> AnyImagesAsync(
        Guid userId,
        IReadOnlyList<Guid>? attachmentIds,
        CancellationToken ct)
    {
        if (attachmentIds is null || attachmentIds.Count == 0) return false;
        var distinct = attachmentIds.Distinct().ToList();
        return await db.ChatAttachments
            .AsNoTracking()
            .AnyAsync(
                a => a.UserId == userId && distinct.Contains(a.Id) && a.Kind == ChatAttachmentKinds.Image,
                ct);
    }

    public async Task DeleteThreadFilesAsync(Guid userId, Guid threadId, CancellationToken ct)
    {
        var keys = await db.ChatAttachments
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.ThreadId == threadId)
            .Select(a => a.StorageKey)
            .ToListAsync(ct);

        foreach (var key in keys)
        {
            await storage.DeleteAsync(key, ct);
        }
    }

    public async Task<string?> BuildThreadManifestAsync(Guid userId, Guid threadId, CancellationToken ct)
    {
        var attachments = (await db.ChatAttachments
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.ThreadId == threadId && a.Kind != ChatAttachmentKinds.Image)
            .ToListAsync(ct))
            .OrderBy(a => a.CreatedAt)
            .ToList();

        if (attachments.Count == 0) return null;

        var sb = new StringBuilder();
        sb.AppendLine("Attached files in this chat thread (use `query_chat_ics` for .ics calendars or `read_chat_attachment` for documents):");
        foreach (var att in attachments)
        {
            var summary = ExtractSummary(att.MetadataJson) ?? $"{att.SizeBytes:N0} bytes";
            sb.Append("- `").Append(att.Id).Append("` — ").Append(att.FileName)
                .Append(" (").Append(att.Kind).Append("): ").AppendLine(summary);
        }

        return sb.ToString().TrimEnd();
    }

    public async Task<string> QueryIcsAsync(
        Guid userId,
        Guid threadId,
        Guid attachmentId,
        string? query,
        string? startDate,
        string? endDate,
        int limit,
        string fallbackTimezone,
        CancellationToken ct)
    {
        var att = await db.ChatAttachments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.Id == attachmentId && a.UserId == userId && a.ThreadId == threadId,
                ct);

        if (att is null)
        {
            return JsonSerializer.Serialize(new { error = "Attachment not found in this thread." }, JsonOptions);
        }

        if (att.Kind != ChatAttachmentKinds.Ics)
        {
            return JsonSerializer.Serialize(new { error = "Attachment is not an .ics calendar file. Use read_chat_attachment instead." }, JsonOptions);
        }

        await using var stream = await storage.OpenReadAsync(att.StorageKey, ct);
        if (stream is null)
        {
            return JsonSerializer.Serialize(new { error = "Attachment file could not be read from storage." }, JsonOptions);
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync(ct);
        var parsed = IcsParser.Parse(raw, string.IsNullOrWhiteSpace(fallbackTimezone) ? "UTC" : fallbackTimezone);

        var clampedLimit = Math.Clamp(limit <= 0 ? 40 : limit, 1, 100);
        var q = query?.Trim();
        DateOnly? fromDate = DateOnly.TryParseExact(startDate?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sd) ? sd : null;
        DateOnly? toDate = DateOnly.TryParseExact(endDate?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ed) ? ed : null;

        var filtered = parsed.Events.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            filtered = filtered.Where(e =>
                e.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(e.Description) && e.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(e.Location) && e.Location.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        if (fromDate.HasValue)
        {
            filtered = filtered.Where(e => DateOnly.FromDateTime(e.StartAt.UtcDateTime) >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            filtered = filtered.Where(e => DateOnly.FromDateTime(e.StartAt.UtcDateTime) <= toDate.Value);
        }

        var ordered = filtered.OrderBy(e => e.StartAt).ToList();
        var distinctSummaries = parsed.Events
            .GroupBy(e => e.Title.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new { summary = g.First().Title.Trim(), count = g.Count() })
            .OrderByDescending(x => x.count)
            .ThenBy(x => x.summary, StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToList();

        var events = ordered.Take(clampedLimit).Select(e => new
        {
            summary = e.Title,
            startAt = e.StartAt.ToString("O", CultureInfo.InvariantCulture),
            endAt = e.EndAt.ToString("O", CultureInfo.InvariantCulture),
            isAllDay = e.IsAllDay,
            location = e.Location,
            description = string.IsNullOrWhiteSpace(e.Description) ? null : e.Description,
        }).ToList();

        return JsonSerializer.Serialize(
            new
            {
                attachmentId = att.Id,
                fileName = att.FileName,
                timezone = parsed.Timezone ?? fallbackTimezone,
                totalCalendarEvents = parsed.Events.Count,
                matchedCount = ordered.Count,
                returnedCount = events.Count,
                distinctSummaries = string.IsNullOrWhiteSpace(q) ? distinctSummaries : null,
                events,
            },
            JsonOptions);
    }

    public async Task<string> ReadAttachmentAsync(
        Guid userId,
        Guid threadId,
        Guid attachmentId,
        string? query,
        int offset,
        int maxChars,
        CancellationToken ct)
    {
        var att = await db.ChatAttachments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.Id == attachmentId && a.UserId == userId && a.ThreadId == threadId,
                ct);

        if (att is null)
        {
            return JsonSerializer.Serialize(new { error = "Attachment not found in this thread." }, JsonOptions);
        }

        if (att.Kind == ChatAttachmentKinds.Ics)
        {
            return await QueryIcsAsync(userId, threadId, attachmentId, query, null, null, 30, "UTC", ct);
        }

        if (att.Kind == ChatAttachmentKinds.Image)
        {
            return JsonSerializer.Serialize(new { error = "This attachment is an image; images are provided visually when using a vision-capable model." }, JsonOptions);
        }

        var text = att.ExtractedText ?? string.Empty;
        if (text.Length == 0)
        {
            return JsonSerializer.Serialize(new { attachmentId = att.Id, fileName = att.FileName, totalChars = 0, content = string.Empty }, JsonOptions);
        }

        var clampedMax = Math.Clamp(maxChars <= 0 ? 12_000 : maxChars, 500, 20_000);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            var excerpts = new List<string>();
            var searchPos = 0;
            var totalLen = 0;
            while (searchPos < text.Length && excerpts.Count < 8 && totalLen < clampedMax)
            {
                var idx = text.IndexOf(q, searchPos, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                var start = Math.Max(0, idx - 300);
                var len = Math.Min(text.Length - start, 1_200);
                var slice = text.Substring(start, len);
                excerpts.Add(slice);
                totalLen += slice.Length;
                searchPos = idx + q.Length + 300;
            }

            return JsonSerializer.Serialize(
                new
                {
                    attachmentId = att.Id,
                    fileName = att.FileName,
                    query = q,
                    totalChars = text.Length,
                    matchCount = excerpts.Count,
                    content = string.Join("\n---\n", excerpts),
                },
                JsonOptions);
        }

        var safeOffset = Math.Clamp(offset, 0, Math.Max(0, text.Length - 1));
        var sliceLen = Math.Min(clampedMax, text.Length - safeOffset);
        var content = text.Substring(safeOffset, sliceLen);

        return JsonSerializer.Serialize(
            new
            {
                attachmentId = att.Id,
                fileName = att.FileName,
                offset = safeOffset,
                totalChars = text.Length,
                hasMore = safeOffset + sliceLen < text.Length,
                content,
            },
            JsonOptions);
    }

    public async Task<(string? DataUrl, long ByteLength)> LoadImageDataUrlAsync(
        ChatAttachment attachment,
        CancellationToken ct)
    {
        if (attachment.Kind != ChatAttachmentKinds.Image || attachment.SizeBytes > MaxVisionImageBytes)
        {
            return (null, 0);
        }

        await using var stream = await storage.OpenReadAsync(attachment.StorageKey, ct);
        if (stream is null) return (null, 0);

        await using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        if (bytes.Length > MaxVisionImageBytes) return (null, 0);

        var dataUrl = $"data:{attachment.ContentType};base64,{Convert.ToBase64String(bytes)}";
        return (dataUrl, bytes.Length);
    }

    public static ChatAttachmentDto ToDto(ChatAttachment a) =>
        new(
            a.Id,
            a.FileName,
            a.ContentType,
            a.Kind,
            a.SizeBytes,
            ExtractSummary(a.MetadataJson),
            $"/api/chat/attachments/{a.Id}",
            a.CreatedAt);

    private static string? ExtractSummary(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.TryGetProperty("summary", out var el) && el.ValueKind == JsonValueKind.String)
            {
                return el.GetString();
            }
        }
        catch (JsonException)
        {
            // Ignore malformed metadata
        }

        return null;
    }
}

