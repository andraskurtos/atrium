using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Atrium.Api.Attachments;
using Atrium.Api.Auth;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Atrium.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Atrium.Api.Endpoints;

public static class AttachmentEndpoints
{
    private static readonly HashSet<string> AllowedInlineImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp",
        "image/gif",
    };

    private static readonly HashSet<string> BlockedArtifactExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".sh", ".ps1", ".msi", ".com", ".scr",
        ".vbs", ".js", ".mjs", ".cjs", ".html", ".htm", ".xhtml", ".svg",
    };

    public static void MapAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/attachments");

        group.MapPost("/", async (
            HttpRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            IAttachmentStorage storage,
            IOptions<StorageOptions> storageOptions,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var userExists = await db.Users.AnyAsync(u => u.Id == userId.Value && u.IsActive, ct);
            if (!userExists) return Results.Unauthorized();

            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Expected multipart/form-data upload." });
            }

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? (form.Files.Count > 0 ? form.Files[0] : null);
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "A non-empty file is required." });
            }

            var kindRaw = form["kind"].ToString().Trim();
            var kind = string.IsNullOrEmpty(kindRaw) ? AttachmentKinds.Artifact : kindRaw.ToLowerInvariant();
            if (kind is not (AttachmentKinds.Artifact or AttachmentKinds.InlineImage))
            {
                return Results.BadRequest(new { error = "kind must be 'artifact' or 'inline_image'." });
            }

            var options = storageOptions.Value;
            var maxBytes = kind == AttachmentKinds.InlineImage ? options.MaxImageBytes : options.MaxArtifactBytes;
            if (file.Length > maxBytes)
            {
                return Results.Json(
                    new { error = $"File exceeds maximum size of {maxBytes / (1024 * 1024)} MB." },
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            var sanitizedName = SanitizeFileName(file.FileName);
            var contentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType.Trim();
            if (contentType.Length > 127)
            {
                contentType = contentType[..127];
            }

            if (kind == AttachmentKinds.InlineImage)
            {
                if (!AllowedInlineImageMimeTypes.Contains(contentType))
                {
                    return Results.BadRequest(new { error = "Inline images must be PNG, JPEG, WebP, or GIF." });
                }
            }
            else
            {
                var ext = Path.GetExtension(sanitizedName);
                if (!string.IsNullOrEmpty(ext) && BlockedArtifactExtensions.Contains(ext))
                {
                    return Results.BadRequest(new { error = $"File type '{ext}' is not allowed for security reasons." });
                }
            }

            if (!TryParseOptionalGuid(form["noteId"].ToString(), out var noteId) ||
                !TryParseOptionalGuid(form["universityCourseId"].ToString(), out var courseId) ||
                !TryParseOptionalGuid(form["universityActivityItemId"].ToString(), out var itemId) ||
                !TryParseOptionalGuid(form["calendarEventId"].ToString(), out var eventId))
            {
                return Results.BadRequest(new { error = "Invalid owner identifier." });
            }

            DateTimeOffset? occurrenceStartAt = null;
            var occurrenceRaw = form["calendarOccurrenceStartAt"].ToString().Trim();
            if (!string.IsNullOrEmpty(occurrenceRaw))
            {
                if (!DateTimeOffset.TryParse(
                    occurrenceRaw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsedOcc))
                {
                    return Results.BadRequest(new { error = "Invalid calendarOccurrenceStartAt timestamp." });
                }

                occurrenceStartAt = parsedOcc.ToUniversalTime();
            }

            Guid? resolvedNoteId = null;
            Guid? resolvedCourseId = null;
            Guid? resolvedItemId = null;
            Guid? resolvedEventId = null;
            DateTimeOffset? resolvedOccurrence = null;

            if (kind == AttachmentKinds.InlineImage)
            {
                if (noteId is null || courseId is not null || itemId is not null || eventId is not null)
                {
                    return Results.BadRequest(new { error = "Inline images must belong exclusively to a note." });
                }

                var noteExists = await db.Notes.AnyAsync(
                    n => n.Id == noteId.Value && n.UserId == userId.Value && n.DeletedAt == null,
                    ct);
                if (!noteExists)
                {
                    return Results.NotFound(new { error = "Target note not found." });
                }

                resolvedNoteId = noteId.Value;
            }
            else
            {
                var ownerCount =
                    (noteId.HasValue ? 1 : 0) +
                    (courseId.HasValue ? 1 : 0) +
                    (itemId.HasValue ? 1 : 0) +
                    (eventId.HasValue ? 1 : 0);

                if (ownerCount != 1)
                {
                    return Results.BadRequest(new
                    {
                        error = "Artifacts must specify exactly one owner: noteId, universityCourseId, universityActivityItemId, or calendarEventId.",
                    });
                }

                if (noteId.HasValue)
                {
                    var noteExists = await db.Notes.AnyAsync(
                        n => n.Id == noteId.Value && n.UserId == userId.Value && n.DeletedAt == null,
                        ct);
                    if (!noteExists) return Results.NotFound(new { error = "Target note not found." });
                    resolvedNoteId = noteId.Value;
                }
                else if (courseId.HasValue)
                {
                    var courseExists = await db.UniversityCourses.AnyAsync(
                        c => c.Id == courseId.Value && c.UserId == userId.Value,
                        ct);
                    if (!courseExists) return Results.NotFound(new { error = "Target course not found." });
                    resolvedCourseId = courseId.Value;
                }
                else if (itemId.HasValue)
                {
                    var item = await db.UniversityActivityItems
                        .AsNoTracking()
                        .FirstOrDefaultAsync(i => i.Id == itemId.Value && i.UserId == userId.Value, ct);
                    if (item is null) return Results.NotFound(new { error = "Target university item not found." });
                    resolvedItemId = item.Id;
                    resolvedCourseId = item.CourseId;
                }
                else if (eventId.HasValue)
                {
                    var evt = await db.CalendarEvents
                        .AsNoTracking()
                        .Include(e => e.Calendar)
                        .FirstOrDefaultAsync(e => e.Id == eventId.Value && e.UserId == userId.Value, ct);
                    if (evt is null) return Results.NotFound(new { error = "Target calendar event not found." });

                    var isUniEvent = string.Equals(evt.Calendar.Module, "university", StringComparison.OrdinalIgnoreCase)
                        || await db.UniversityActivityItems.AnyAsync(
                            i => i.UserId == userId.Value && i.CalendarEventId == evt.Id,
                            ct);
                    if (isUniEvent)
                    {
                        return Results.BadRequest(new
                        {
                            error = "University calendar events must attach artifacts via universityActivityItemId.",
                        });
                    }

                    if (evt.RecurrenceMasterId.HasValue)
                    {
                        resolvedEventId = evt.RecurrenceMasterId.Value;
                        resolvedOccurrence = (occurrenceStartAt ?? evt.RecurrenceOriginalStartAt ?? evt.StartAt).ToUniversalTime();
                    }
                    else
                    {
                        resolvedEventId = evt.Id;
                        resolvedOccurrence = occurrenceStartAt;
                    }
                }
            }

            await using var uploadBuffer = new MemoryStream();
            await using (var sourceStream = file.OpenReadStream())
            {
                var buffer = new byte[81_920];
                long totalRead = 0;
                int bytesRead;
                while ((bytesRead = await sourceStream.ReadAsync(buffer, ct)) > 0)
                {
                    totalRead += bytesRead;
                    if (totalRead > maxBytes)
                    {
                        return Results.Json(
                            new { error = $"File exceeds maximum size of {maxBytes / (1024 * 1024)} MB." },
                            statusCode: StatusCodes.Status413PayloadTooLarge);
                    }

                    await uploadBuffer.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                }
            }

            uploadBuffer.Position = 0;
            var sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(uploadBuffer, ct));
            uploadBuffer.Position = 0;

            var attachmentId = Guid.NewGuid();
            var storageKey = $"{userId.Value:N}/{attachmentId:N}";
            await storage.SaveAsync(storageKey, uploadBuffer, ct);

            var entity = new Attachment
            {
                Id = attachmentId,
                UserId = userId.Value,
                Kind = kind,
                FileName = sanitizedName,
                ContentType = contentType,
                SizeBytes = uploadBuffer.Length,
                Sha256 = sha256,
                StorageKey = storageKey,
                NoteId = resolvedNoteId,
                UniversityCourseId = resolvedCourseId,
                UniversityActivityItemId = resolvedItemId,
                CalendarEventId = resolvedEventId,
                CalendarOccurrenceStartAt = resolvedOccurrence,
                AllowAgentRead = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            db.Attachments.Add(entity);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/attachments/{entity.Id}", ToDto(entity));
        })
        .DisableAntiforgery()
        .RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapGet("/", async (
            Guid? noteId,
            Guid? universityCourseId,
            Guid? universityActivityItemId,
            Guid? calendarEventId,
            DateTimeOffset? occurrenceStartAt,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var ownerCount =
                (noteId.HasValue ? 1 : 0) +
                (universityCourseId.HasValue ? 1 : 0) +
                (universityActivityItemId.HasValue ? 1 : 0) +
                (calendarEventId.HasValue ? 1 : 0);

            if (ownerCount != 1)
            {
                return Results.BadRequest(new
                {
                    error = "Specify exactly one filter: noteId, universityCourseId, universityActivityItemId, or calendarEventId.",
                });
            }

            var isAgent = IsPatPrincipal(principal);
            if (isAgent)
            {
                var requiredScope = noteId.HasValue
                    ? "notes:read"
                    : calendarEventId.HasValue
                        ? "calendar:read"
                        : "university:read";
                if (!HasScope(principal, requiredScope))
                {
                    return Results.Forbid();
                }
            }

            var query = db.Attachments
                .AsNoTracking()
                .Where(a => a.UserId == userId.Value && a.DeletedAt == null && a.Kind == AttachmentKinds.Artifact);

            if (noteId.HasValue)
            {
                query = query.Where(a => a.NoteId == noteId.Value);
            }
            else if (universityCourseId.HasValue)
            {
                query = query.Where(a => a.UniversityCourseId == universityCourseId.Value && a.UniversityActivityItemId == null);
            }
            else if (universityActivityItemId.HasValue)
            {
                query = query.Where(a => a.UniversityActivityItemId == universityActivityItemId.Value);
            }
            else if (calendarEventId.HasValue)
            {
                var targetEventId = calendarEventId.Value;
                var targetOccurrence = occurrenceStartAt?.ToUniversalTime();
                var evt = await db.CalendarEvents
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == targetEventId && e.UserId == userId.Value, ct);
                if (evt is not null && evt.RecurrenceMasterId.HasValue)
                {
                    targetEventId = evt.RecurrenceMasterId.Value;
                    targetOccurrence ??= (evt.RecurrenceOriginalStartAt ?? evt.StartAt).ToUniversalTime();
                }

                query = targetOccurrence.HasValue
                    ? query.Where(a => a.CalendarEventId == targetEventId && a.CalendarOccurrenceStartAt == targetOccurrence.Value)
                    : query.Where(a => a.CalendarEventId == targetEventId && a.CalendarOccurrenceStartAt == null);
            }

            var items = await query.ToListAsync(ct);

            if (isAgent)
            {
                var allowed = new List<Attachment>(items.Count);
                foreach (var item in items)
                {
                    if (await IsAttachmentAgentReadableAsync(db, userId.Value, item, ct))
                    {
                        allowed.Add(item);
                    }
                }

                items = allowed;
            }

            var ordered = items
                .OrderBy(a => a.CreatedAt)
                .Select(ToDto)
                .ToList();

            return Results.Ok(ordered);
        }).RequireAuthorization(AuthConstants.HumanOrAgentPolicy);

        group.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext httpContext,
            ClaimsPrincipal principal,
            AppDbContext db,
            IAttachmentStorage storage,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var attachment = await db.Attachments
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId.Value && a.DeletedAt == null, ct);
            if (attachment is null) return Results.NotFound();

            if (IsPatPrincipal(principal))
            {
                var requiredScope = GetRequiredAgentScope(attachment);
                if (!HasScope(principal, requiredScope))
                {
                    return Results.Forbid();
                }

                if (!await IsAttachmentAgentReadableAsync(db, userId.Value, attachment, ct))
                {
                    return Results.NotFound();
                }
            }

            var stream = await storage.OpenReadAsync(attachment.StorageKey, ct);
            if (stream is null) return Results.NotFound();

            httpContext.Response.Headers.XContentTypeOptions = "nosniff";

            if (attachment.Kind == AttachmentKinds.InlineImage)
            {
                httpContext.Response.Headers.CacheControl = "private, max-age=3600";
                httpContext.Response.Headers.ContentDisposition = "inline";
                return Results.Stream(stream, contentType: attachment.ContentType);
            }

            return Results.File(stream, contentType: attachment.ContentType, fileDownloadName: attachment.FileName);
        }).RequireAuthorization(AuthConstants.HumanOrAgentPolicy);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            AppDbContext db,
            IAttachmentStorage storage,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var attachment = await db.Attachments
                .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId.Value && a.DeletedAt == null, ct);
            if (attachment is null) return Results.NotFound();

            attachment.DeletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            try
            {
                await storage.DeleteAsync(attachment.StorageKey, ct);
            }
            catch (IOException)
            {
                // Best-effort disk cleanup
            }

            return Results.NoContent();
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);
    }

    public static AttachmentDto ToDto(Attachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return new AttachmentDto(
            attachment.Id,
            attachment.Kind,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            $"/api/attachments/{attachment.Id}",
            attachment.CreatedAt);
    }

    public static string SanitizeFileName(string? rawFileName)
    {
        if (string.IsNullOrWhiteSpace(rawFileName))
        {
            return "attachment";
        }

        var parts = rawFileName.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        var baseName = parts.Length > 0 ? parts[^1] : rawFileName;

        var sb = new StringBuilder(baseName.Length);
        foreach (var ch in baseName)
        {
            if (char.IsControl(ch) || ch is '"' or ';' or '<' or '>' or '|' or '*' or '?')
            {
                continue;
            }

            sb.Append(ch);
        }

        var cleaned = sb.ToString().Trim().Trim('.');
        if (string.IsNullOrEmpty(cleaned))
        {
            return "attachment";
        }

        return cleaned.Length > 255 ? cleaned[..255] : cleaned;
    }

    private static bool IsPatPrincipal(ClaimsPrincipal principal) =>
        string.Equals(
            principal.FindFirstValue(AuthConstants.AuthTypeClaim),
            AuthConstants.PatAuthType,
            StringComparison.Ordinal);

    private static bool HasScope(ClaimsPrincipal principal, string scope) =>
        principal.FindAll(AuthConstants.ScopeClaim)
            .Any(c => string.Equals(c.Value, scope, StringComparison.Ordinal));

    private static string GetRequiredAgentScope(Attachment attachment)
    {
        if (attachment.NoteId.HasValue) return "notes:read";
        if (attachment.CalendarEventId.HasValue) return "calendar:read";
        return "university:read";
    }

    private static async Task<bool> IsAttachmentAgentReadableAsync(
        AppDbContext db,
        Guid userId,
        Attachment attachment,
        CancellationToken ct)
    {
        if (!attachment.AllowAgentRead)
        {
            return false;
        }

        if (attachment.NoteId.HasValue)
        {
            var note = await db.Notes
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == attachment.NoteId.Value && n.UserId == userId && n.DeletedAt == null, ct);
            if (note is null || !note.AllowAgentRead)
            {
                return false;
            }

            if (note.FolderId.HasValue)
            {
                return await IsFolderChainAgentReadableAsync(db, userId, note.FolderId.Value, ct);
            }

            return true;
        }

        if (attachment.UniversityActivityItemId.HasValue)
        {
            var linkedNote = await db.Notes
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    n => n.UserId == userId
                        && n.UniversityActivityItemId == attachment.UniversityActivityItemId.Value
                        && n.DeletedAt == null,
                    ct);
            if (linkedNote is not null && !linkedNote.AllowAgentRead)
            {
                return false;
            }
        }
        else if (attachment.CalendarEventId.HasValue)
        {
            var linkedNote = await db.Notes
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    n => n.UserId == userId
                        && n.CalendarEventId == attachment.CalendarEventId.Value
                        && n.CalendarOccurrenceStartAt == attachment.CalendarOccurrenceStartAt
                        && n.DeletedAt == null,
                    ct);
            if (linkedNote is not null && !linkedNote.AllowAgentRead)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> IsFolderChainAgentReadableAsync(
        AppDbContext db,
        Guid userId,
        Guid folderId,
        CancellationToken ct)
    {
        var folders = await db.NoteFolders
            .AsNoTracking()
            .Where(f => f.UserId == userId && f.DeletedAt == null)
            .ToDictionaryAsync(f => f.Id, ct);

        Guid? current = folderId;
        var guard = 0;
        while (current.HasValue && guard++ < 64)
        {
            if (!folders.TryGetValue(current.Value, out var folder))
            {
                return false;
            }

            if (!folder.AllowAgentRead)
            {
                return false;
            }

            current = folder.ParentFolderId;
        }

        return true;
    }

    private static bool TryParseOptionalGuid(string raw, out Guid? value)
    {
        var trimmed = raw.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            value = null;
            return true;
        }

        if (Guid.TryParse(trimmed, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }
}
