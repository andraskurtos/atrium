using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Atrium.Api.Attachments;
using Atrium.Api.Auth;
using Atrium.Api.Calendar;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Atrium.Api.Days;
using Atrium.Api.Endpoints;
using Atrium.Api.Notes;
using Atrium.Api.Storage;
using Atrium.Api.University;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace Atrium.Api.Knowledge;

public sealed record ExtractedPdfImageDto(
    Guid AttachmentId,
    int PageNumber,
    int Width,
    int Height,
    string Url,
    string Markdown);

public sealed record ExtractPdfImagesResponse(
    Guid SourceAttachmentId,
    Guid NoteId,
    IReadOnlyList<ExtractedPdfImageDto> Images);

public sealed partial class KnowledgeWriteService(
    AppDbContext db,
    IAttachmentStorage storage)
{
    private const int MaxNoteTitleLength = 200;
    private const int MaxNoteContentLength = 500_000;
    private const int MaxChecklistTitleLength = 280;
    private const int MinPdfImageDimension = 120;
    private const int MaxExtractedImagesPerCall = 6;
    private const int MaxExtractedImageBytes = 5 * 1024 * 1024;

    [GeneratedRegex(@"^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorRegex();

    public async Task<KnowledgeResult<NoteDetailDto>> CreateNoteAsync(
        ClaimsPrincipal principal,
        bool enforceAgentRules,
        string? title,
        string? content,
        Guid? folderId,
        Guid? universityCourseId,
        Guid? universityActivityItemId,
        Guid? calendarEventId,
        DateTimeOffset? occurrenceStartAt,
        string? dailyDate,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return KnowledgeResult.Unauthorized<NoteDetailDto>();

        var enforceScopes = ShouldEnforceAgentRules(principal, enforceAgentRules);
        if (enforceScopes && !HasScope(principal, "notes:write"))
        {
            return KnowledgeResult.Forbidden<NoteDetailDto>("Missing required scope: notes:write");
        }

        var rawContent = content ?? string.Empty;
        if (rawContent.Length > MaxNoteContentLength)
        {
            return KnowledgeResult.BadRequest<NoteDetailDto>($"Note content must be {MaxNoteContentLength} characters or fewer.");
        }

        var parsed = NoteFrontmatter.Parse(rawContent);
        var resolvedTitle = !string.IsNullOrWhiteSpace(title)
            ? title.Trim()
            : (!string.IsNullOrWhiteSpace(parsed.Title) ? parsed.Title.Trim() : "Untitled");
        if (resolvedTitle.Length == 0 || resolvedTitle.Length > MaxNoteTitleLength)
        {
            return KnowledgeResult.BadRequest<NoteDetailDto>($"Note title must be 1–{MaxNoteTitleLength} characters.");
        }

        Guid? resolvedFolderId = null;
        Guid? resolvedCourseId = null;
        Guid? resolvedItemId = null;
        Guid? resolvedEventId = null;
        DateTimeOffset? resolvedOccurrence = null;
        DateOnly? resolvedDailyDate = null;
        string? linkLabel = null;

        if (!string.IsNullOrWhiteSpace(dailyDate))
        {
            if (!DateOnly.TryParseExact(dailyDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDay))
            {
                return KnowledgeResult.BadRequest<NoteDetailDto>("dailyDate must be a valid yyyy-MM-dd date.");
            }

            var existingDaily = await db.Notes.AnyAsync(
                n => n.UserId == userId.Value && n.DailyDate == parsedDay && n.DeletedAt == null,
                ct);
            if (existingDaily)
            {
                return KnowledgeResult.BadRequest<NoteDetailDto>("A daily note already exists for that date; use update_note instead.");
            }

            resolvedDailyDate = parsedDay;
            linkLabel = parsedDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        else if (universityActivityItemId.HasValue)
        {
            var item = await db.UniversityActivityItems
                .AsNoTracking()
                .Include(i => i.Course)
                .Include(i => i.Activity)
                .FirstOrDefaultAsync(i => i.Id == universityActivityItemId.Value && i.UserId == userId.Value, ct);
            if (item is null)
            {
                return KnowledgeResult.NotFound<NoteDetailDto>("University activity item not found.");
            }

            resolvedCourseId = item.CourseId;
            resolvedItemId = item.Id;
            var sem = string.IsNullOrWhiteSpace(item.Course.Semester) ? "Uncategorized" : item.Course.Semester.Trim();
            var actLabel = string.IsNullOrWhiteSpace(item.Activity.Title)
                ? UniversityConstants.GetDefaultActivityLabel(item.Activity.ActivityType)
                : item.Activity.Title.Trim();
            linkLabel = $"{sem} / {item.Course.Name} / {actLabel} / {item.Title}";
        }
        else if (universityCourseId.HasValue)
        {
            var course = await db.UniversityCourses
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == universityCourseId.Value && c.UserId == userId.Value, ct);
            if (course is null)
            {
                return KnowledgeResult.NotFound<NoteDetailDto>("University course not found.");
            }

            resolvedCourseId = course.Id;
            var sem = string.IsNullOrWhiteSpace(course.Semester) ? "Uncategorized" : course.Semester.Trim();
            linkLabel = $"{sem} / {course.Name}";
        }
        else if (calendarEventId.HasValue)
        {
            var evt = await db.CalendarEvents
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == calendarEventId.Value && e.UserId == userId.Value, ct);
            if (evt is null)
            {
                return KnowledgeResult.NotFound<NoteDetailDto>("Calendar event not found.");
            }

            resolvedEventId = evt.RecurrenceMasterId ?? evt.Id;
            var isRec = !string.IsNullOrWhiteSpace(evt.RecurrenceRule) || evt.RecurrenceMasterId.HasValue;
            resolvedOccurrence = isRec ? (occurrenceStartAt ?? evt.RecurrenceOriginalStartAt)?.ToUniversalTime() : null;
            linkLabel = evt.Title;

            var exists = await db.Notes.AnyAsync(
                n => n.UserId == userId.Value
                    && n.CalendarEventId == resolvedEventId
                    && n.CalendarOccurrenceStartAt == resolvedOccurrence
                    && n.DeletedAt == null,
                ct);
            if (exists)
            {
                return KnowledgeResult.BadRequest<NoteDetailDto>("A note already exists for that event; use update_note instead.");
            }
        }
        else if (folderId.HasValue)
        {
            var folder = await db.NoteFolders
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == folderId.Value && f.UserId == userId.Value && f.DeletedAt == null, ct);
            if (folder is null)
            {
                return KnowledgeResult.NotFound<NoteDetailDto>("Folder not found.");
            }

            if (enforceScopes && !await IsFolderChainAgentReadableAsync(userId.Value, folder.Id, ct))
            {
                return KnowledgeResult.NotFound<NoteDetailDto>("Folder not found.");
            }

            resolvedFolderId = folder.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var note = new Note
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            FolderId = resolvedFolderId,
            UniversityCourseId = resolvedCourseId,
            UniversityActivityItemId = resolvedItemId,
            CalendarEventId = resolvedEventId,
            CalendarOccurrenceStartAt = resolvedOccurrence,
            DailyDate = resolvedDailyDate,
            Title = resolvedTitle,
            Content = rawContent,
            ContentHash = NoteFrontmatter.ComputeContentHash(rawContent),
            Version = 1,
            AllowAgentRead = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        foreach (var tag in parsed.Tags)
        {
            note.Tags.Add(new NoteTag
            {
                NoteId = note.Id,
                UserId = userId.Value,
                Tag = tag,
            });
        }

        db.Notes.Add(note);
        await db.SaveChangesAsync(ct);

        return KnowledgeResult.Ok(ToNoteDetailDto(note, linkLabel));
    }

    public async Task<KnowledgeResult<NoteDetailDto>> UpdateNoteAsync(
        ClaimsPrincipal principal,
        bool enforceAgentRules,
        Guid noteId,
        string? title,
        string? content,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return KnowledgeResult.Unauthorized<NoteDetailDto>();

        var enforceScopes = ShouldEnforceAgentRules(principal, enforceAgentRules);
        if (enforceScopes && !HasScope(principal, "notes:write"))
        {
            return KnowledgeResult.Forbidden<NoteDetailDto>("Missing required scope: notes:write");
        }

        var note = await db.Notes
            .Include(n => n.Tags)
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId.Value && n.DeletedAt == null, ct);
        if (note is null)
        {
            return KnowledgeResult.NotFound<NoteDetailDto>("Note not found.");
        }

        if (enforceScopes)
        {
            var folderOk = !note.FolderId.HasValue || await IsFolderChainAgentReadableAsync(userId.Value, note.FolderId.Value, ct);
            if (!note.AllowAgentRead || !folderOk)
            {
                return KnowledgeResult.NotFound<NoteDetailDto>("Note not found.");
            }
        }

        if (title is not null)
        {
            var trimmed = title.Trim();
            if (trimmed.Length == 0 || trimmed.Length > MaxNoteTitleLength)
            {
                return KnowledgeResult.BadRequest<NoteDetailDto>($"Note title must be 1–{MaxNoteTitleLength} characters.");
            }

            note.Title = trimmed;
        }

        if (content is not null)
        {
            if (content.Length > MaxNoteContentLength)
            {
                return KnowledgeResult.BadRequest<NoteDetailDto>($"Note content must be {MaxNoteContentLength} characters or fewer.");
            }

            note.Content = content;
            note.ContentHash = NoteFrontmatter.ComputeContentHash(content);

            var parsed = NoteFrontmatter.Parse(content);
            var desiredSet = new HashSet<string>(parsed.Tags, StringComparer.Ordinal);
            var toRemove = note.Tags.Where(t => !desiredSet.Contains(t.Tag)).ToList();
            foreach (var oldTag in toRemove)
            {
                note.Tags.Remove(oldTag);
                db.NoteTags.Remove(oldTag);
            }

            var existingSet = note.Tags.Select(t => t.Tag).ToHashSet(StringComparer.Ordinal);
            foreach (var tag in parsed.Tags)
            {
                if (!existingSet.Contains(tag))
                {
                    note.Tags.Add(new NoteTag
                    {
                        NoteId = note.Id,
                        UserId = userId.Value,
                        Tag = tag,
                    });
                }
            }
        }

        note.Version += 1;
        note.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return KnowledgeResult.Ok(ToNoteDetailDto(note, null));
    }

    public async Task<KnowledgeResult<ExtractPdfImagesResponse>> ExtractPdfImagesAsync(
        ClaimsPrincipal principal,
        bool enforceAgentRules,
        Guid attachmentId,
        Guid noteId,
        int? startPage,
        int? endPage,
        int maxImages,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return KnowledgeResult.Unauthorized<ExtractPdfImagesResponse>();

        var enforceScopes = ShouldEnforceAgentRules(principal, enforceAgentRules);
        if (enforceScopes && !HasScope(principal, "notes:write"))
        {
            return KnowledgeResult.Forbidden<ExtractPdfImagesResponse>("Missing required scope: notes:write");
        }

        var source = await db.Attachments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.Id == attachmentId
                    && a.UserId == userId.Value
                    && a.DeletedAt == null
                    && a.Kind == AttachmentKinds.Artifact,
                ct);
        if (source is null)
        {
            return KnowledgeResult.NotFound<ExtractPdfImagesResponse>("Source artifact not found.");
        }

        var isPdf = source.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            || source.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase);
        if (!isPdf)
        {
            return KnowledgeResult.BadRequest<ExtractPdfImagesResponse>("Image extraction is currently supported for PDF artifacts only.");
        }

        var targetNote = await db.Notes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId.Value && n.DeletedAt == null, ct);
        if (targetNote is null)
        {
            return KnowledgeResult.NotFound<ExtractPdfImagesResponse>("Target note not found.");
        }

        if (enforceScopes)
        {
            var folderOk = !targetNote.FolderId.HasValue || await IsFolderChainAgentReadableAsync(userId.Value, targetNote.FolderId.Value, ct);
            if (!source.AllowAgentRead || !targetNote.AllowAgentRead || !folderOk)
            {
                return KnowledgeResult.NotFound<ExtractPdfImagesResponse>("Resource not found.");
            }
        }

        await using var pdfStream = await storage.OpenReadAsync(source.StorageKey, ct);
        if (pdfStream is null)
        {
            return KnowledgeResult.NotFound<ExtractPdfImagesResponse>("Source PDF file is missing from storage.");
        }

        var limit = Math.Clamp(maxImages <= 0 ? 4 : maxImages, 1, MaxExtractedImagesPerCall);
        var extracted = new List<ExtractedPdfImageDto>();

        try
        {
            using var ms = new MemoryStream();
            await pdfStream.CopyToAsync(ms, ct);
            ms.Position = 0;

            using var document = PdfDocument.Open(ms);
            var totalPages = document.NumberOfPages;
            var fromPage = Math.Clamp(startPage ?? 1, 1, Math.Max(1, totalPages));
            var toPage = Math.Clamp(endPage ?? totalPages, fromPage, Math.Max(1, totalPages));

            // 1. Pre-scan image hashes across the PDF to detect repeating slide backgrounds, logos, and master templates
            var imagePageOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var p = 1; p <= totalPages; p++)
            {
                ct.ThrowIfCancellationRequested();
                var pageHashes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var img in document.GetPage(p).GetImages())
                {
                    if (img.WidthInSamples < MinPdfImageDimension || img.HeightInSamples < MinPdfImageDimension)
                    {
                        continue;
                    }

                    var rawBytes = img.RawBytes.ToArray();
                    if (rawBytes.Length == 0) continue;
                    var rawHash = Convert.ToHexStringLower(SHA256.HashData(rawBytes));
                    if (pageHashes.Add(rawHash))
                    {
                        imagePageOccurrences[rawHash] = imagePageOccurrences.GetValueOrDefault(rawHash, 0) + 1;
                    }
                }
            }

            var repeatThreshold = totalPages >= 6 ? 3 : 2;
            var templateHashes = imagePageOccurrences
                .Where(kvp => totalPages >= 3 && kvp.Value >= repeatThreshold)
                .Select(kvp => kvp.Key)
                .ToHashSet(StringComparer.Ordinal);

            var extractedHashes = new HashSet<string>(StringComparer.Ordinal);
            var pagesNeedingRenderFallback = new List<int>();

            for (var pageNum = fromPage; pageNum <= toPage && extracted.Count < limit; pageNum++)
            {
                ct.ThrowIfCancellationRequested();
                var page = document.GetPage(pageNum);
                var pageImages = page.GetImages().ToList();
                var pageHasText = !string.IsNullOrWhiteSpace(page.Text) && page.Text.Trim().Length > 15;
                var pageImgIndex = 0;

                foreach (var img in pageImages)
                {
                    if (extracted.Count >= limit) break;
                    if (img.WidthInSamples < MinPdfImageDimension || img.HeightInSamples < MinPdfImageDimension)
                    {
                        continue;
                    }

                    var raw = img.RawBytes.ToArray();
                    if (raw.Length == 0) continue;
                    var rawHash = Convert.ToHexStringLower(SHA256.HashData(raw));

                    // Skip repeating slide master backgrounds / logos / headers
                    if (templateHashes.Contains(rawHash))
                    {
                        continue;
                    }

                    // Skip full-page decorative background images when the page also has text or other content
                    var widthRatio = page.Width > 0 ? img.BoundingBox.Width / page.Width : 0;
                    var heightRatio = page.Height > 0 ? img.BoundingBox.Height / page.Height : 0;
                    if (widthRatio >= 0.88 && heightRatio >= 0.88 && (pageHasText || pageImages.Count > 1))
                    {
                        continue;
                    }

                    if (!extractedHashes.Add(rawHash))
                    {
                        continue;
                    }

                    byte[]? imageBytes = null;
                    string contentType;
                    string extension;

                    if (img.TryGetPng(out var pngBytes) && pngBytes is { Length: > 0 and <= MaxExtractedImageBytes })
                    {
                        imageBytes = pngBytes;
                        contentType = "image/png";
                        extension = "png";
                    }
                    else if (raw.Length is > 4 and <= MaxExtractedImageBytes && raw[0] == 0xFF && raw[1] == 0xD8)
                    {
                        imageBytes = raw;
                        contentType = "image/jpeg";
                        extension = "jpg";
                    }
                    else
                    {
                        continue;
                    }

                    pageImgIndex++;
                    var saved = await SaveExtractedInlineImageAsync(
                        userId.Value,
                        targetNote.Id,
                        pageNum,
                        pageImgIndex,
                        img.WidthInSamples,
                        img.HeightInSamples,
                        imageBytes,
                        contentType,
                        extension,
                        ct);
                    extracted.Add(saved);
                }

                // If caller asked for a specific narrow page range and no non-background raster figures were found
                // (e.g., slide uses vector diagrams + text), queue the slide for pdftoppm rendering
                if (pageImgIndex == 0 && (toPage - fromPage) <= 5)
                {
                    pagesNeedingRenderFallback.Add(pageNum);
                }
            }

            if (pagesNeedingRenderFallback.Count > 0 && extracted.Count < limit)
            {
                var pdfBytes = ms.ToArray();
                foreach (var pageNum in pagesNeedingRenderFallback)
                {
                    if (extracted.Count >= limit) break;
                    var renderedPng = await TryRenderPdfPageWithPdftoppmAsync(pdfBytes, pageNum, ct);
                    if (renderedPng is { Length: > 0 and <= MaxExtractedImageBytes })
                    {
                        var saved = await SaveExtractedInlineImageAsync(
                            userId.Value,
                            targetNote.Id,
                            pageNum,
                            1,
                            1280,
                            720,
                            renderedPng,
                            "image/png",
                            "png",
                            ct);
                        extracted.Add(saved);
                    }
                }
            }

            if (extracted.Count > 0)
            {
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return KnowledgeResult.BadRequest<ExtractPdfImagesResponse>($"Could not extract images from PDF: {ex.Message}");
        }

        return KnowledgeResult.Ok(new ExtractPdfImagesResponse(source.Id, targetNote.Id, extracted));
    }

    public async Task<KnowledgeResult<DailyCheckItemDto>> CreateChecklistItemAsync(
        ClaimsPrincipal principal,
        bool enforceAgentRules,
        string? date,
        string? title,
        string? dueTime,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return KnowledgeResult.Unauthorized<DailyCheckItemDto>();

        var enforceScopes = ShouldEnforceAgentRules(principal, enforceAgentRules);
        if (enforceScopes && !HasScope(principal, "tasks:write"))
        {
            return KnowledgeResult.Forbidden<DailyCheckItemDto>("Missing required scope: tasks:write");
        }

        DateOnly parsedDate;
        if (string.IsNullOrWhiteSpace(date))
        {
            parsedDate = DateOnly.FromDateTime(DateTime.UtcNow);
        }
        else if (!DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
        {
            return KnowledgeResult.BadRequest<DailyCheckItemDto>("date must be a valid yyyy-MM-dd date.");
        }

        var trimmedTitle = (title ?? string.Empty).Trim();
        if (trimmedTitle.Length == 0 || trimmedTitle.Length > MaxChecklistTitleLength)
        {
            return KnowledgeResult.BadRequest<DailyCheckItemDto>($"Checklist title must be 1–{MaxChecklistTitleLength} characters.");
        }

        string? normalizedDueTime = null;
        if (!string.IsNullOrWhiteSpace(dueTime))
        {
            if (!TimeOnly.TryParseExact(dueTime.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime))
            {
                return KnowledgeResult.BadRequest<DailyCheckItemDto>("dueTime must be in 24-hour HH:mm format.");
            }

            normalizedDueTime = parsedTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        var maxSort = await db.DailyCheckItems
            .Where(i => i.UserId == userId.Value && i.Date == parsedDate)
            .MaxAsync(i => (int?)i.SortOrder, ct);

        var now = DateTimeOffset.UtcNow;
        var item = new DailyCheckItem
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            Date = parsedDate,
            Title = trimmedTitle,
            IsCompleted = false,
            CompletedAt = null,
            DueTime = normalizedDueTime,
            SortOrder = (maxSort ?? -1) + 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.DailyCheckItems.Add(item);
        await db.SaveChangesAsync(ct);

        return KnowledgeResult.Ok(new DailyCheckItemDto(
            item.Id,
            parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            item.Title,
            item.IsCompleted,
            item.CompletedAt,
            item.DueTime,
            item.SortOrder,
            item.CreatedAt,
            item.UpdatedAt));
    }

    public async Task<KnowledgeResult<DailyCheckItemDto>> ToggleChecklistItemAsync(
        ClaimsPrincipal principal,
        bool enforceAgentRules,
        Guid itemId,
        bool isCompleted,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return KnowledgeResult.Unauthorized<DailyCheckItemDto>();

        var enforceScopes = ShouldEnforceAgentRules(principal, enforceAgentRules);
        if (enforceScopes && !HasScope(principal, "tasks:write"))
        {
            return KnowledgeResult.Forbidden<DailyCheckItemDto>("Missing required scope: tasks:write");
        }

        var item = await db.DailyCheckItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == userId.Value, ct);
        if (item is null)
        {
            return KnowledgeResult.NotFound<DailyCheckItemDto>("Checklist item not found.");
        }

        var now = DateTimeOffset.UtcNow;
        item.IsCompleted = isCompleted;
        item.CompletedAt = isCompleted ? (item.CompletedAt ?? now) : null;
        item.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return KnowledgeResult.Ok(new DailyCheckItemDto(
            item.Id,
            item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            item.Title,
            item.IsCompleted,
            item.CompletedAt,
            item.DueTime,
            item.SortOrder,
            item.CreatedAt,
            item.UpdatedAt));
    }

    public async Task<KnowledgeResult<CalendarEventDto>> CreateEventAsync(
        ClaimsPrincipal principal,
        bool enforceAgentRules,
        string? title,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        Guid? calendarId,
        bool isAllDay,
        string? location,
        string? description,
        string? recurrenceRule,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return KnowledgeResult.Unauthorized<CalendarEventDto>();

        var enforceScopes = ShouldEnforceAgentRules(principal, enforceAgentRules);
        if (enforceScopes && !HasScope(principal, "calendar:write"))
        {
            return KnowledgeResult.Forbidden<CalendarEventDto>("Missing required scope: calendar:write");
        }

        var trimmedTitle = (title ?? string.Empty).Trim();
        if (trimmedTitle.Length == 0 || trimmedTitle.Length > 200)
        {
            return KnowledgeResult.BadRequest<CalendarEventDto>("Event title must be 1–200 characters.");
        }

        if (endAt < startAt)
        {
            return KnowledgeResult.BadRequest<CalendarEventDto>("Event endAt must be on or after startAt.");
        }

        string? normalizedRule = null;
        if (!string.IsNullOrWhiteSpace(recurrenceRule))
        {
            if (!RRuleExpander.TryParse(recurrenceRule.Trim(), out _, out var err))
            {
                return KnowledgeResult.BadRequest<CalendarEventDto>(err ?? "Invalid recurrenceRule.");
            }

            normalizedRule = recurrenceRule.Trim();
        }

        UserCalendar? targetCalendar;
        if (calendarId.HasValue)
        {
            targetCalendar = await db.Calendars
                .FirstOrDefaultAsync(c => c.Id == calendarId.Value && c.UserId == userId.Value, ct);
            if (targetCalendar is null)
            {
                return KnowledgeResult.NotFound<CalendarEventDto>("Calendar not found.");
            }
        }
        else
        {
            var calendars = await CalendarEndpoints.EnsureUserCalendarsAsync(db, userId.Value, ct);
            targetCalendar = calendars.OrderByDescending(c => c.IsDefault).ThenBy(c => c.CreatedAt).First();
        }

        var now = DateTimeOffset.UtcNow;
        var evt = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            CalendarId = targetCalendar.Id,
            UserId = userId.Value,
            Title = trimmedTitle,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
            StartAt = startAt.ToUniversalTime(),
            EndAt = endAt.ToUniversalTime(),
            IsAllDay = isAllDay,
            Timezone = targetCalendar.Timezone,
            Status = "confirmed",
            RecurrenceRule = normalizedRule,
            Source = "Agent",
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.CalendarEvents.Add(evt);
        await db.SaveChangesAsync(ct);

        return KnowledgeResult.Ok(new CalendarEventDto(
            evt.Id,
            evt.CalendarId,
            targetCalendar.Name,
            targetCalendar.Color,
            evt.Title,
            evt.Description,
            evt.Location,
            evt.StartAt,
            evt.EndAt,
            evt.IsAllDay,
            evt.Timezone,
            evt.Status,
            evt.ExternalUid,
            evt.RecurrenceRule,
            evt.Source,
            evt.CreatedAt,
            evt.UpdatedAt));
    }

    public async Task<KnowledgeResult<UniversityCourseDto>> CreateCourseAsync(
        ClaimsPrincipal principal,
        bool enforceAgentRules,
        string? name,
        string? code,
        string? semester,
        string? instructor,
        decimal? credits,
        string? color,
        string? description,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return KnowledgeResult.Unauthorized<UniversityCourseDto>();

        var enforceScopes = ShouldEnforceAgentRules(principal, enforceAgentRules);
        if (enforceScopes && !HasScope(principal, "university:write"))
        {
            return KnowledgeResult.Forbidden<UniversityCourseDto>("Missing required scope: university:write");
        }

        var trimmedName = (name ?? string.Empty).Trim();
        if (trimmedName.Length == 0 || trimmedName.Length > 140)
        {
            return KnowledgeResult.BadRequest<UniversityCourseDto>("Course name must be 1–140 characters.");
        }

        var normalizedColor = string.IsNullOrWhiteSpace(color) ? UniversityConstants.DefaultCalendarColor : color.Trim();
        if (!HexColorRegex().IsMatch(normalizedColor))
        {
            return KnowledgeResult.BadRequest<UniversityCourseDto>("Color must be a 6-digit hex code like #3b6ea5.");
        }

        var now = DateTimeOffset.UtcNow;
        var course = new UniversityCourse
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            Name = trimmedName,
            Code = TrimOrNull(code, 32),
            Semester = TrimOrNull(semester, 64),
            Description = TrimOrNull(description, 2000),
            Instructor = TrimOrNull(instructor, 120),
            Credits = credits is >= 0 and <= 100 ? credits : null,
            Color = normalizedColor.ToLowerInvariant(),
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.UniversityCourses.Add(course);
        await db.SaveChangesAsync(ct);

        return KnowledgeResult.Ok(new UniversityCourseDto(
            course.Id,
            course.Name,
            course.Code,
            course.Semester,
            course.Description,
            course.Instructor,
            course.Credits,
            course.Color,
            course.Status,
            course.MetadataJson,
            [],
            course.CreatedAt,
            course.UpdatedAt));
    }

    public async Task<KnowledgeResult<UniversityCourseActivityDto>> CreateActivityAsync(
        ClaimsPrincipal principal,
        bool enforceAgentRules,
        Guid courseId,
        string? activityType,
        string? title,
        string? defaultLocation,
        int? minAttendanceCount,
        decimal? minAttendancePercent,
        int? plannedTotalCount,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return KnowledgeResult.Unauthorized<UniversityCourseActivityDto>();

        var enforceScopes = ShouldEnforceAgentRules(principal, enforceAgentRules);
        if (enforceScopes && !HasScope(principal, "university:write"))
        {
            return KnowledgeResult.Forbidden<UniversityCourseActivityDto>("Missing required scope: university:write");
        }

        var normalizedType = (activityType ?? string.Empty).Trim().ToLowerInvariant();
        if (!UniversityConstants.ValidActivityTypes.Contains(normalizedType))
        {
            return KnowledgeResult.BadRequest<UniversityCourseActivityDto>(
                "activityType must be lecture, practice, laboratory, homework_assignment, semester_assignment, midterm, or exam.");
        }

        var course = await db.UniversityCourses
            .Include(c => c.Activities)
            .FirstOrDefaultAsync(c => c.Id == courseId && c.UserId == userId.Value, ct);
        if (course is null)
        {
            return KnowledgeResult.NotFound<UniversityCourseActivityDto>("University course not found.");
        }

        var supportsAttendance = UniversityConstants.SupportsAttendance(normalizedType);
        var now = DateTimeOffset.UtcNow;
        var nextSort = course.Activities.Count > 0 ? course.Activities.Max(a => a.SortOrder) + 1 : 0;

        var activity = new UniversityCourseActivity
        {
            Id = Guid.NewGuid(),
            CourseId = course.Id,
            UserId = userId.Value,
            ActivityType = normalizedType,
            Title = TrimOrNull(title, 120),
            DefaultLocation = TrimOrNull(defaultLocation, 280),
            MinAttendanceCount = supportsAttendance ? minAttendanceCount : null,
            MinAttendancePercent = supportsAttendance ? minAttendancePercent : null,
            PlannedTotalCount = supportsAttendance ? plannedTotalCount : null,
            SortOrder = nextSort,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.UniversityCourseActivities.Add(activity);
        await db.SaveChangesAsync(ct);

        return KnowledgeResult.Ok(new UniversityCourseActivityDto(
            activity.Id,
            activity.CourseId,
            activity.ActivityType,
            activity.Title,
            activity.Description,
            activity.DefaultLocation,
            activity.MinAttendanceCount,
            activity.MinAttendancePercent,
            activity.PlannedTotalCount,
            activity.SortOrder,
            activity.MetadataJson,
            UniversityAttendanceCalculator.Calculate(activity),
            [],
            activity.CreatedAt,
            activity.UpdatedAt));
    }

    public async Task<KnowledgeResult<UniversityActivityItemDto>> CreateActivityItemAsync(
        ClaimsPrincipal principal,
        bool enforceAgentRules,
        Guid activityId,
        string? title,
        string? theme,
        string? notes,
        int? sequenceNumber,
        DateTimeOffset? startAt,
        DateTimeOffset? endAt,
        bool isAllDay,
        string? location,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return KnowledgeResult.Unauthorized<UniversityActivityItemDto>();

        var enforceScopes = ShouldEnforceAgentRules(principal, enforceAgentRules);
        if (enforceScopes && !HasScope(principal, "university:write"))
        {
            return KnowledgeResult.Forbidden<UniversityActivityItemDto>("Missing required scope: university:write");
        }

        var trimmedTitle = (title ?? string.Empty).Trim();
        if (trimmedTitle.Length == 0 || trimmedTitle.Length > 200)
        {
            return KnowledgeResult.BadRequest<UniversityActivityItemDto>("Item title must be 1–200 characters.");
        }

        if (startAt.HasValue && endAt.HasValue && endAt.Value < startAt.Value)
        {
            return KnowledgeResult.BadRequest<UniversityActivityItemDto>("endAt must be on or after startAt.");
        }

        var activity = await db.UniversityCourseActivities
            .Include(a => a.Course)
            .FirstOrDefaultAsync(a => a.Id == activityId && a.UserId == userId.Value, ct);
        if (activity is null)
        {
            return KnowledgeResult.NotFound<UniversityActivityItemDto>("University activity not found.");
        }

        var now = DateTimeOffset.UtcNow;
        var cleanTheme = TrimOrNull(theme, 280);
        var cleanNotes = TrimOrNull(notes, 4000);
        var cleanLocation = TrimOrNull(location, 280) ?? activity.DefaultLocation;

        CalendarEvent? linkedEvent = null;
        if (startAt.HasValue && endAt.HasValue)
        {
            var uniCalendar = await EnsureUniversityCalendarAsync(userId.Value, ct);
            var prefix = !string.IsNullOrWhiteSpace(activity.Course.Code) ? activity.Course.Code : activity.Course.Name;
            var eventTitle = $"{prefix} · {trimmedTitle}";
            if (eventTitle.Length > 200) eventTitle = eventTitle[..200];

            var descBuilder = new StringBuilder(activity.Title ?? UniversityConstants.GetDefaultActivityLabel(activity.ActivityType));
            if (cleanTheme is not null) descBuilder.Append("\nTheme: ").Append(cleanTheme);
            if (cleanNotes is not null) descBuilder.Append("\nNotes: ").Append(cleanNotes);

            linkedEvent = new CalendarEvent
            {
                Id = Guid.NewGuid(),
                CalendarId = uniCalendar.Id,
                UserId = userId.Value,
                Title = eventTitle,
                Description = descBuilder.ToString(),
                Location = cleanLocation,
                StartAt = startAt.Value.ToUniversalTime(),
                EndAt = endAt.Value.ToUniversalTime(),
                IsAllDay = isAllDay,
                Timezone = uniCalendar.Timezone,
                Status = "confirmed",
                Source = UniversityConstants.CalendarEventSource,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.CalendarEvents.Add(linkedEvent);
        }

        var item = new UniversityActivityItem
        {
            Id = Guid.NewGuid(),
            ActivityId = activity.Id,
            CourseId = activity.CourseId,
            UserId = userId.Value,
            CalendarEventId = linkedEvent?.Id,
            CalendarEvent = linkedEvent,
            Title = trimmedTitle,
            Theme = cleanTheme,
            Notes = cleanNotes,
            SequenceNumber = sequenceNumber,
            AttendanceStatus = "pending",
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.UniversityActivityItems.Add(item);
        await db.SaveChangesAsync(ct);

        return KnowledgeResult.Ok(new UniversityActivityItemDto(
            item.Id,
            item.ActivityId,
            item.CourseId,
            item.CalendarEventId,
            item.Title,
            item.Theme,
            item.Notes,
            item.SequenceNumber,
            item.AttendanceStatus,
            linkedEvent?.StartAt,
            linkedEvent?.EndAt,
            linkedEvent?.IsAllDay ?? false,
            linkedEvent?.Location,
            item.MetadataJson,
            item.CreatedAt,
            item.UpdatedAt));
    }

    private async Task<ExtractedPdfImageDto> SaveExtractedInlineImageAsync(
        Guid userId,
        Guid noteId,
        int pageNum,
        int figIndex,
        int width,
        int height,
        byte[] imageBytes,
        string contentType,
        string extension,
        CancellationToken ct)
    {
        var newId = Guid.NewGuid();
        var storageKey = $"{userId:N}/{newId:N}";
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(imageBytes));

        await using var imgStream = new MemoryStream(imageBytes);
        await storage.SaveAsync(storageKey, imgStream, ct);

        var fileName = string.Create(CultureInfo.InvariantCulture, $"pdf-p{pageNum}-fig{figIndex}.{extension}");
        db.Attachments.Add(new Attachment
        {
            Id = newId,
            UserId = userId,
            Kind = AttachmentKinds.InlineImage,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = imageBytes.Length,
            Sha256 = sha256,
            StorageKey = storageKey,
            NoteId = noteId,
            AllowAgentRead = true,
            TextExtractionStatus = AttachmentExtractionStatuses.Unsupported,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var url = $"/api/attachments/{newId}";
        var md = string.Create(CultureInfo.InvariantCulture, $"![Slide {pageNum} figure {figIndex}]({url})");
        return new ExtractedPdfImageDto(newId, pageNum, width, height, url, md);
    }

    private static async Task<byte[]?> TryRenderPdfPageWithPdftoppmAsync(
        byte[] pdfBytes,
        int pageNum,
        CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"atrium-pdf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var pdfPath = Path.Combine(tempDir, "input.pdf");
            var outPrefix = Path.Combine(tempDir, "slide");
            await File.WriteAllBytesAsync(pdfPath, pdfBytes, ct);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pdftoppm",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add("-png");
            process.StartInfo.ArgumentList.Add("-r");
            process.StartInfo.ArgumentList.Add("150");
            process.StartInfo.ArgumentList.Add("-f");
            process.StartInfo.ArgumentList.Add(pageNum.ToString(CultureInfo.InvariantCulture));
            process.StartInfo.ArgumentList.Add("-l");
            process.StartInfo.ArgumentList.Add(pageNum.ToString(CultureInfo.InvariantCulture));
            process.StartInfo.ArgumentList.Add(pdfPath);
            process.StartInfo.ArgumentList.Add(outPrefix);

            if (!process.Start()) return null;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return null;
            }

            if (process.ExitCode != 0) return null;

            var pngFile = Directory.GetFiles(tempDir, "slide*.png").FirstOrDefault();
            if (pngFile is null) return null;
            return await File.ReadAllBytesAsync(pngFile, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private async Task<UserCalendar> EnsureUniversityCalendarAsync(Guid userId, CancellationToken ct)
    {
        var calendars = await CalendarEndpoints.EnsureUserCalendarsAsync(db, userId, ct);
        var existing = calendars.FirstOrDefault(c =>
            string.Equals(c.Module, UniversityConstants.CalendarModuleKey, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var tz = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Timezone).FirstOrDefaultAsync(ct) ?? "UTC";
        var now = DateTimeOffset.UtcNow;
        var created = new UserCalendar
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = UniversityConstants.DefaultCalendarName,
            Description = UniversityConstants.DefaultCalendarDescription,
            Color = UniversityConstants.DefaultCalendarColor,
            Timezone = string.IsNullOrWhiteSpace(tz) ? "UTC" : tz,
            IsDefault = false,
            IsVisible = true,
            Module = UniversityConstants.CalendarModuleKey,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Calendars.Add(created);
        await db.SaveChangesAsync(ct);
        return created;
    }

    private async Task<bool> IsFolderChainAgentReadableAsync(Guid userId, Guid folderId, CancellationToken ct)
    {
        var folders = await db.NoteFolders
            .AsNoTracking()
            .Where(f => f.UserId == userId && f.DeletedAt == null)
            .ToDictionaryAsync(f => f.Id, ct);

        Guid? current = folderId;
        var guard = 0;
        while (current.HasValue && guard++ < 64)
        {
            if (!folders.TryGetValue(current.Value, out var folder) || !folder.AllowAgentRead)
            {
                return false;
            }

            current = folder.ParentFolderId;
        }

        return !current.HasValue;
    }

    private static bool ShouldEnforceAgentRules(ClaimsPrincipal principal, bool enforceAgentRules) =>
        enforceAgentRules
        || string.Equals(principal.FindFirstValue(AuthConstants.AuthTypeClaim), AuthConstants.PatAuthType, StringComparison.Ordinal);

    private static bool HasScope(ClaimsPrincipal principal, string scope) =>
        principal.FindAll(AuthConstants.ScopeClaim)
            .Any(c => string.Equals(c.Value, scope, StringComparison.Ordinal));

    private static string? TrimOrNull(string? raw, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static NoteDetailDto ToNoteDetailDto(Note n, string? linkLabel)
    {
        var linkKind = n.DailyDate.HasValue
            ? NoteLinkKinds.Daily
            : n.UniversityActivityItemId.HasValue
                ? NoteLinkKinds.UniversityItem
                : n.UniversityCourseId.HasValue
                    ? NoteLinkKinds.UniversityCourse
                    : n.CalendarEventId.HasValue
                        ? NoteLinkKinds.CalendarEvent
                        : NoteLinkKinds.General;

        return new NoteDetailDto(
            n.Id,
            n.FolderId,
            n.Title,
            n.Content,
            n.Tags.Select(t => t.Tag).OrderBy(t => t, StringComparer.Ordinal).ToList(),
            n.ContentHash,
            n.Version,
            n.AllowAgentRead,
            n.AllowAgentRead,
            n.CreatedAt,
            n.UpdatedAt,
            n.UniversityCourseId,
            n.UniversityActivityItemId,
            n.CalendarEventId,
            n.CalendarOccurrenceStartAt,
            linkKind,
            linkLabel,
            n.DailyDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}

