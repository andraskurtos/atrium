using System.Globalization;
using System.Security.Claims;
using Atrium.Api.Auth;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Atrium.Api.Days;
using Atrium.Api.Notes;
using Microsoft.EntityFrameworkCore;

namespace Atrium.Api.Endpoints;

public static class DayEndpoints
{
    private const int MaxTitleLength = 280;
    private const int MaxNoteContentLength = 10_000;

    public static void MapDayEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/days");

        group.MapGet("/{date}", async (
            string date,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var accessError = CheckDayReadAccess(principal, out var userId);
            if (accessError is not null) return accessError;

            if (!TryParseDate(date, out var parsedDate))
            {
                return Results.BadRequest(new { error = "Date must be a valid yyyy-MM-dd date." });
            }

            var items = (await db.DailyCheckItems
                .AsNoTracking()
                .Where(i => i.UserId == userId && i.Date == parsedDate)
                .OrderBy(i => i.SortOrder)
                .ToListAsync(ct))
                .OrderBy(i => i.SortOrder)
                .ThenBy(i => i.CreatedAt)
                .ToList();

            var linkedNote = await db.Notes
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    n => n.UserId == userId && n.DailyDate == parsedDate && n.DeletedAt == null,
                    ct);

            DailyNoteDto? noteDto;
            if (linkedNote is not null)
            {
                noteDto = new DailyNoteDto(
                    linkedNote.Id,
                    FormatDate(parsedDate),
                    linkedNote.Content,
                    linkedNote.CreatedAt,
                    linkedNote.UpdatedAt);
            }
            else
            {
                var legacyNote = await db.DailyNotes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(n => n.UserId == userId && n.Date == parsedDate, ct);
                noteDto = legacyNote is null ? null : ToNoteDto(legacyNote);
            }

            var response = new DailyTrackerResponse(
                FormatDate(parsedDate),
                items.Select(ToCheckItemDto).ToList(),
                noteDto);

            return Results.Ok(response);
        }).RequireAuthorization(AuthConstants.HumanOrAgentPolicy);

        group.MapPost("/{date}/checklist", async (
            string date,
            CreateDailyCheckItemRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            if (!TryParseDate(date, out var parsedDate))
            {
                return Results.BadRequest(new { error = "Date must be a valid yyyy-MM-dd date." });
            }

            var trimmedTitle = (request.Title ?? string.Empty).Trim();
            if (trimmedTitle.Length == 0 || trimmedTitle.Length > MaxTitleLength)
            {
                return Results.BadRequest(new { error = $"Checklist item title must be 1–{MaxTitleLength} characters." });
            }

            if (!TryNormalizeDueTime(request.DueTime, out var normalizedDueTime))
            {
                return Results.BadRequest(new { error = "Due time must be in 24-hour HH:mm format." });
            }

            var maxSortOrder = await db.DailyCheckItems
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
                SortOrder = (maxSortOrder ?? -1) + 1,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.DailyCheckItems.Add(item);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/days/{FormatDate(parsedDate)}/checklist/{item.Id}", ToCheckItemDto(item));
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapPut("/checklist/{id:guid}", async (
            Guid id,
            UpdateDailyCheckItemRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var item = await db.DailyCheckItems
                .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId.Value, ct);
            if (item is null) return Results.NotFound();

            var now = DateTimeOffset.UtcNow;

            if (request.Title is not null)
            {
                var trimmedTitle = request.Title.Trim();
                if (trimmedTitle.Length == 0 || trimmedTitle.Length > MaxTitleLength)
                {
                    return Results.BadRequest(new { error = $"Checklist item title must be 1–{MaxTitleLength} characters." });
                }

                item.Title = trimmedTitle;
            }

            if (request.IsCompleted.HasValue)
            {
                if (request.IsCompleted.Value && !item.IsCompleted)
                {
                    item.CompletedAt = now;
                }
                else if (!request.IsCompleted.Value)
                {
                    item.CompletedAt = null;
                }

                item.IsCompleted = request.IsCompleted.Value;
            }

            if (request.ClearDueTime)
            {
                item.DueTime = null;
            }
            else if (request.DueTime is not null)
            {
                if (!TryNormalizeDueTime(request.DueTime, out var normalizedDueTime))
                {
                    return Results.BadRequest(new { error = "Due time must be in 24-hour HH:mm format." });
                }

                item.DueTime = normalizedDueTime;
            }

            if (request.Date is not null)
            {
                if (!TryParseDate(request.Date, out var targetDate))
                {
                    return Results.BadRequest(new { error = "Date must be a valid yyyy-MM-dd date." });
                }

                if (targetDate != item.Date)
                {
                    var targetMaxSort = await db.DailyCheckItems
                        .Where(i => i.UserId == userId.Value && i.Date == targetDate)
                        .MaxAsync(i => (int?)i.SortOrder, ct);

                    item.Date = targetDate;
                    item.SortOrder = (targetMaxSort ?? -1) + 1;
                }
            }

            item.UpdatedAt = now;
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToCheckItemDto(item));
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapDelete("/checklist/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var item = await db.DailyCheckItems
                .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId.Value, ct);
            if (item is null) return Results.NotFound();

            db.DailyCheckItems.Remove(item);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapPut("/{date}/note", async (
            string date,
            UpsertDailyNoteRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            if (!TryParseDate(date, out var parsedDate))
            {
                return Results.BadRequest(new { error = "Date must be a valid yyyy-MM-dd date." });
            }

            var content = request.Content ?? string.Empty;
            if (content.Length > MaxNoteContentLength)
            {
                return Results.BadRequest(new { error = $"Daily note must be {MaxNoteContentLength} characters or fewer." });
            }

            var formattedDate = FormatDate(parsedDate);
            var existingNote = await db.Notes
                .Include(n => n.Tags)
                .FirstOrDefaultAsync(
                    n => n.UserId == userId.Value && n.DailyDate == parsedDate && n.DeletedAt == null,
                    ct);
            var legacyNote = await db.DailyNotes
                .FirstOrDefaultAsync(n => n.UserId == userId.Value && n.Date == parsedDate, ct);

            if (string.IsNullOrWhiteSpace(content))
            {
                var nowClear = DateTimeOffset.UtcNow;
                if (existingNote is not null)
                {
                    existingNote.DeletedAt = nowClear;
                    existingNote.UpdatedAt = nowClear;
                    if (existingNote.Tags.Count > 0)
                    {
                        db.NoteTags.RemoveRange(existingNote.Tags);
                    }
                }

                if (legacyNote is not null)
                {
                    db.DailyNotes.Remove(legacyNote);
                }

                if (existingNote is not null || legacyNote is not null)
                {
                    await db.SaveChangesAsync(ct);
                }

                return Results.Ok(new UpsertDailyNoteResponse(formattedDate, null));
            }

            var now = DateTimeOffset.UtcNow;
            var parsedFm = NoteFrontmatter.Parse(content);

            if (legacyNote is not null)
            {
                db.DailyNotes.Remove(legacyNote);
            }

            if (existingNote is not null)
            {
                existingNote.Content = content;
                existingNote.ContentHash = NoteFrontmatter.ComputeContentHash(content);
                existingNote.Version += 1;
                existingNote.UpdatedAt = now;
                SyncNoteTags(db, existingNote, userId.Value, parsedFm.Tags);
                await db.SaveChangesAsync(ct);
                return Results.Ok(new UpsertDailyNoteResponse(
                    formattedDate,
                    new DailyNoteDto(existingNote.Id, formattedDate, existingNote.Content, existingNote.CreatedAt, existingNote.UpdatedAt)));
            }

            var title = !string.IsNullOrWhiteSpace(parsedFm.Title)
                ? parsedFm.Title.Trim()
                : parsedDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
            if (title.Length > 200) title = title[..200];

            var created = new Note
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                DailyDate = parsedDate,
                Title = title,
                Content = content,
                ContentHash = NoteFrontmatter.ComputeContentHash(content),
                Version = 1,
                AllowAgentRead = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            SyncNoteTags(db, created, userId.Value, parsedFm.Tags);

            db.Notes.Add(created);
            try
            {
                await db.SaveChangesAsync(ct);
                return Results.Ok(new UpsertDailyNoteResponse(
                    formattedDate,
                    new DailyNoteDto(created.Id, formattedDate, created.Content, created.CreatedAt, created.UpdatedAt)));
            }
            catch (DbUpdateException)
            {
                db.Entry(created).State = EntityState.Detached;
                var raced = await db.Notes
                    .Include(n => n.Tags)
                    .FirstOrDefaultAsync(
                        n => n.UserId == userId.Value && n.DailyDate == parsedDate && n.DeletedAt == null,
                        ct);
                if (raced is null)
                {
                    throw;
                }

                raced.Content = content;
                raced.ContentHash = NoteFrontmatter.ComputeContentHash(content);
                raced.Version += 1;
                raced.UpdatedAt = DateTimeOffset.UtcNow;
                SyncNoteTags(db, raced, userId.Value, parsedFm.Tags);
                await db.SaveChangesAsync(ct);
                return Results.Ok(new UpsertDailyNoteResponse(
                    formattedDate,
                    new DailyNoteDto(raced.Id, formattedDate, raced.Content, raced.CreatedAt, raced.UpdatedAt)));
            }
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);
    }

    private static IResult? CheckDayReadAccess(ClaimsPrincipal principal, out Guid userId)
    {
        userId = Guid.Empty;
        var resolvedUserId = principal.GetUserId();
        if (resolvedUserId is null)
        {
            return Results.Unauthorized();
        }

        userId = resolvedUserId.Value;

        var authType = principal.FindFirstValue(AuthConstants.AuthTypeClaim);
        if (string.Equals(authType, AuthConstants.PatAuthType, StringComparison.Ordinal))
        {
            var hasScope = principal.FindAll(AuthConstants.ScopeClaim)
                .Any(c => string.Equals(c.Value, "tasks:read", StringComparison.Ordinal));
            if (!hasScope)
            {
                return Results.Forbid();
            }
        }

        return null;
    }

    private static bool TryParseDate(string raw, out DateOnly parsed) =>
        DateOnly.TryParseExact(
            raw?.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out parsed);

    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryNormalizeDueTime(string? raw, out string? normalized)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            normalized = null;
            return true;
        }

        var trimmed = raw.Trim();
        if (!TimeOnly.TryParseExact(trimmed, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            normalized = null;
            return false;
        }

        normalized = parsed.ToString("HH:mm", CultureInfo.InvariantCulture);
        return true;
    }

    private static DailyCheckItemDto ToCheckItemDto(DailyCheckItem item) => new(
        item.Id,
        FormatDate(item.Date),
        item.Title,
        item.IsCompleted,
        item.CompletedAt,
        item.DueTime,
        item.SortOrder,
        item.CreatedAt,
        item.UpdatedAt);

    private static void SyncNoteTags(
        AppDbContext db,
        Note note,
        Guid userId,
        IReadOnlyList<string> desiredTags)
    {
        var desiredSet = new HashSet<string>(desiredTags, StringComparer.Ordinal);
        var toRemove = note.Tags.Where(t => !desiredSet.Contains(t.Tag)).ToList();
        foreach (var oldTag in toRemove)
        {
            note.Tags.Remove(oldTag);
            db.NoteTags.Remove(oldTag);
        }

        var existingSet = note.Tags.Select(t => t.Tag).ToHashSet(StringComparer.Ordinal);
        foreach (var tag in desiredTags)
        {
            if (!existingSet.Contains(tag))
            {
                note.Tags.Add(new NoteTag
                {
                    NoteId = note.Id,
                    UserId = userId,
                    Tag = tag,
                });
            }
        }
    }

    private static DailyNoteDto ToNoteDto(DailyNote note) => new(
        note.Id,
        FormatDate(note.Date),
        note.Content,
        note.CreatedAt,
        note.UpdatedAt);
}
