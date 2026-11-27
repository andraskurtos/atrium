using System.Globalization;
using System.Security.Claims;
using Atrium.Api.Attachments;
using Atrium.Api.Auth;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Atrium.Api.Notes;
using Atrium.Api.University;
using Microsoft.EntityFrameworkCore;

namespace Atrium.Api.Endpoints;

public static class NoteEndpoints
{
    private const int MaxFolderNameLength = 120;
    private const int MaxNoteTitleLength = 200;
    private const int MaxNoteContentLength = 500_000;

    public static void MapNoteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/notes");

        group.MapGet("/tree", async (
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var accessError = CheckNotesReadAccess(principal, out var userId, out var isPat);
            if (accessError is not null) return accessError;

            var activeFolders = await db.NoteFolders
                .AsNoTracking()
                .Where(f => f.UserId == userId && f.DeletedAt == null)
                .ToListAsync(ct);

            var folderMap = activeFolders.ToDictionary(f => f.Id);
            var effectiveFolderAccess = ComputeEffectiveFolderAccess(folderMap);

            var activeNotes = await db.Notes
                .AsNoTracking()
                .Include(n => n.Tags)
                .Where(n => n.UserId == userId && n.DeletedAt == null)
                .ToListAsync(ct);

            var visibleFolders = activeFolders
                .Where(f => !isPat || effectiveFolderAccess.GetValueOrDefault(f.Id, true))
                .OrderBy(f => f.SortOrder)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Select(f => ToFolderDto(f, effectiveFolderAccess.GetValueOrDefault(f.Id, true)))
                .ToList();

            var canReadUni = !isPat || HasScope(principal, "university:read");
            var canReadCal = !isPat || HasScope(principal, "calendar:read");

            var generalNotes = activeNotes
                .Where(IsGeneralNote)
                .Select(n => (Note: n, EffectiveAgent: IsNoteAgentReadable(n, effectiveFolderAccess)))
                .Where(x => !isPat || x.EffectiveAgent)
                .OrderByDescending(x => x.Note.UpdatedAt)
                .Select(x => ToSummaryDto(x.Note, x.EffectiveAgent))
                .ToList();

            var allVisibleNotesForTags = activeNotes
                .Where(n =>
                    IsGeneralNote(n)
                    || (IsUniversityNote(n) && canReadUni)
                    || (IsEventNote(n) && canReadCal))
                .Select(n => (Note: n, EffectiveAgent: IsNoteAgentReadable(n, effectiveFolderAccess)))
                .Where(x => !isPat || x.EffectiveAgent)
                .ToList();

            var tagCounts = allVisibleNotesForTags
                .SelectMany(x => x.Note.Tags.Select(t => t.Tag))
                .GroupBy(t => t, StringComparer.Ordinal)
                .Select(g => new NoteTagCountDto(g.Key, g.Count()))
                .OrderByDescending(t => t.Count)
                .ThenBy(t => t.Tag, StringComparer.Ordinal)
                .ToList();

            var universityTree = canReadUni
                ? await BuildUniversityTreeAsync(db, userId, isPat, activeNotes, effectiveFolderAccess, ct)
                : [];

            var eventsTree = canReadCal
                ? await BuildEventsTreeAsync(db, userId, isPat, activeNotes, effectiveFolderAccess, ct)
                : [];

            return Results.Ok(new NotesTreeResponse(
                visibleFolders,
                generalNotes,
                tagCounts,
                universityTree,
                eventsTree));
        }).RequireAuthorization(AuthConstants.HumanOrAgentPolicy);

        group.MapGet("/", async (
            string? q,
            string? searchIn,
            string? tag,
            string? folderId,
            bool? includeSubfolders,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var accessError = CheckNotesReadAccess(principal, out var userId, out var isPat);
            if (accessError is not null) return accessError;

            var activeFolders = await db.NoteFolders
                .AsNoTracking()
                .Where(f => f.UserId == userId && f.DeletedAt == null)
                .ToListAsync(ct);

            var folderMap = activeFolders.ToDictionary(f => f.Id);
            var effectiveFolderAccess = ComputeEffectiveFolderAccess(folderMap);

            HashSet<Guid>? allowedFolderFilter = null;
            var filterRootOnly = false;
            if (!string.IsNullOrWhiteSpace(folderId))
            {
                if (string.Equals(folderId.Trim(), "root", StringComparison.OrdinalIgnoreCase))
                {
                    filterRootOnly = true;
                }
                else if (Guid.TryParse(folderId.Trim(), out var parsedFolderId))
                {
                    if (!folderMap.ContainsKey(parsedFolderId)
                        || (isPat && !effectiveFolderAccess.GetValueOrDefault(parsedFolderId, false)))
                    {
                        return Results.Ok(Array.Empty<NoteSummaryDto>());
                    }

                    if (includeSubfolders ?? true)
                    {
                        allowedFolderFilter = CollectSubtreeFolderIds(
                            parsedFolderId,
                            activeFolders,
                            isPat ? effectiveFolderAccess : null);
                    }
                    else
                    {
                        allowedFolderFilter = [parsedFolderId];
                    }
                }
                else
                {
                    return Results.BadRequest(new { error = "folderId must be a valid GUID or 'root'." });
                }
            }

            string? normalizedTag = null;
            if (!string.IsNullOrWhiteSpace(tag))
            {
                normalizedTag = NoteFrontmatter.NormalizeTag(tag);
                if (normalizedTag is null)
                {
                    return Results.Ok(Array.Empty<NoteSummaryDto>());
                }
            }

            var query = db.Notes
                .AsNoTracking()
                .Include(n => n.Tags)
                .Where(n => n.UserId == userId && n.DeletedAt == null);

            if (filterRootOnly)
            {
                query = query.Where(n => n.FolderId == null);
            }
            else if (allowedFolderFilter is not null)
            {
                query = query.Where(n => n.FolderId != null && allowedFolderFilter.Contains(n.FolderId.Value));
            }

            if (normalizedTag is not null)
            {
                query = query.Where(n => n.Tags.Any(t => t.Tag == normalizedTag));
            }

            var candidates = await query.ToListAsync(ct);
            var searchTerm = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
            var normalizedSearchIn = string.IsNullOrWhiteSpace(searchIn)
                ? "all"
                : searchIn.Trim().ToLowerInvariant();
            if (normalizedSearchIn is not ("all" or "titles" or "content" or "tags"))
            {
                return Results.BadRequest(new { error = "searchIn must be 'all', 'titles', 'content', or 'tags'." });
            }

            var tagSearchTerm = searchTerm?.TrimStart('#');

            var canReadUni = !isPat || HasScope(principal, "university:read");
            var canReadCal = !isPat || HasScope(principal, "calendar:read");

            var filteredCandidates = candidates
                .Where(n =>
                    IsGeneralNote(n)
                    || (IsUniversityNote(n) && canReadUni)
                    || (IsEventNote(n) && canReadCal))
                .ToList();

            var linkLabels = await BuildLinkLabelsAsync(db, userId, filteredCandidates, ct);

            var results = filteredCandidates
                .Select(n => (
                    Note: n,
                    EffectiveAgent: IsNoteAgentReadable(n, effectiveFolderAccess),
                    Parsed: NoteFrontmatter.Parse(n.Content)))
                .Where(x => !isPat || x.EffectiveAgent)
                .Where(x => searchTerm is null || normalizedSearchIn switch
                {
                    "titles" => x.Note.Title.Contains(searchTerm, StringComparison.OrdinalIgnoreCase),
                    "content" => x.Parsed.Body.Contains(searchTerm, StringComparison.OrdinalIgnoreCase),
                    "tags" => !string.IsNullOrEmpty(tagSearchTerm)
                        && x.Note.Tags.Any(t => t.Tag.Contains(tagSearchTerm, StringComparison.OrdinalIgnoreCase)),
                    _ => x.Note.Title.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                        || x.Note.Content.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                        || x.Note.Tags.Any(t => t.Tag.Contains(tagSearchTerm ?? searchTerm, StringComparison.OrdinalIgnoreCase)),
                })
                .OrderByDescending(x => x.Note.UpdatedAt)
                .Select(x => ToSummaryDto(
                    x.Note,
                    x.EffectiveAgent,
                    normalizedSearchIn == "content" ? searchTerm : null,
                    linkLabels.GetValueOrDefault(x.Note.Id)))
                .ToList();

            return Results.Ok(results);
        }).RequireAuthorization(AuthConstants.HumanOrAgentPolicy);

        group.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var accessError = CheckNotesReadAccess(principal, out var userId, out var isPat);
            if (accessError is not null) return accessError;

            var note = await db.Notes
                .AsNoTracking()
                .Include(n => n.Tags)
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId && n.DeletedAt == null, ct);
            if (note is null) return Results.NotFound();

            var activeFolders = await db.NoteFolders
                .AsNoTracking()
                .Where(f => f.UserId == userId && f.DeletedAt == null)
                .ToListAsync(ct);
            var effectiveFolderAccess = ComputeEffectiveFolderAccess(activeFolders.ToDictionary(f => f.Id));
            var effectiveAgent = IsNoteAgentReadable(note, effectiveFolderAccess);

            if (isPat && !effectiveAgent)
            {
                return Results.NotFound();
            }

            if (isPat && IsUniversityNote(note) && !HasScope(principal, "university:read"))
            {
                return Results.Forbid();
            }

            if (isPat && IsEventNote(note) && !HasScope(principal, "calendar:read"))
            {
                return Results.Forbid();
            }

            var labels = await BuildLinkLabelsAsync(db, userId, [note], ct);
            return Results.Ok(ToDetailDto(note, effectiveAgent, labels.GetValueOrDefault(note.Id)));
        }).RequireAuthorization(AuthConstants.HumanOrAgentPolicy);

        group.MapPost("/folders", async (
            CreateNoteFolderRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var trimmedName = (request.Name ?? string.Empty).Trim();
            if (trimmedName.Length == 0 || trimmedName.Length > MaxFolderNameLength)
            {
                return Results.BadRequest(new { error = $"Folder name must be 1–{MaxFolderNameLength} characters." });
            }

            var activeFolders = await db.NoteFolders
                .Where(f => f.UserId == userId.Value && f.DeletedAt == null)
                .ToListAsync(ct);
            var folderMap = activeFolders.ToDictionary(f => f.Id);

            if (request.ParentFolderId.HasValue && !folderMap.ContainsKey(request.ParentFolderId.Value))
            {
                return Results.NotFound(new { error = "Parent folder not found." });
            }

            var maxSort = activeFolders
                .Where(f => f.ParentFolderId == request.ParentFolderId)
                .Select(f => (int?)f.SortOrder)
                .Max();

            var now = DateTimeOffset.UtcNow;
            var folder = new NoteFolder
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                ParentFolderId = request.ParentFolderId,
                Name = trimmedName,
                AllowAgentRead = request.AllowAgentRead,
                SortOrder = (maxSort ?? -1) + 1,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.NoteFolders.Add(folder);
            await db.SaveChangesAsync(ct);

            folderMap[folder.Id] = folder;
            var effective = ComputeEffectiveFolderAccess(folderMap).GetValueOrDefault(folder.Id, folder.AllowAgentRead);

            return Results.Created($"/api/notes/folders/{folder.Id}", ToFolderDto(folder, effective));
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapPut("/folders/{id:guid}", async (
            Guid id,
            UpdateNoteFolderRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var activeFolders = await db.NoteFolders
                .Where(f => f.UserId == userId.Value && f.DeletedAt == null)
                .ToListAsync(ct);
            var folderMap = activeFolders.ToDictionary(f => f.Id);

            if (!folderMap.TryGetValue(id, out var folder))
            {
                return Results.NotFound();
            }

            if (request.Name is not null)
            {
                var trimmedName = request.Name.Trim();
                if (trimmedName.Length == 0 || trimmedName.Length > MaxFolderNameLength)
                {
                    return Results.BadRequest(new { error = $"Folder name must be 1–{MaxFolderNameLength} characters." });
                }

                folder.Name = trimmedName;
            }

            if (request.MoveToRoot)
            {
                folder.ParentFolderId = null;
            }
            else if (request.ParentFolderId.HasValue)
            {
                var targetParentId = request.ParentFolderId.Value;
                if (targetParentId == folder.Id)
                {
                    return Results.BadRequest(new { error = "A folder cannot be moved into itself." });
                }

                if (!folderMap.ContainsKey(targetParentId))
                {
                    return Results.NotFound(new { error = "Target parent folder not found." });
                }

                if (IsDescendantFolder(folder.Id, targetParentId, folderMap))
                {
                    return Results.BadRequest(new { error = "A folder cannot be moved into one of its subfolders." });
                }

                folder.ParentFolderId = targetParentId;
            }

            if (request.AllowAgentRead.HasValue)
            {
                folder.AllowAgentRead = request.AllowAgentRead.Value;
            }

            folder.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            var effective = ComputeEffectiveFolderAccess(folderMap).GetValueOrDefault(folder.Id, folder.AllowAgentRead);
            return Results.Ok(ToFolderDto(folder, effective));
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapDelete("/folders/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var activeFolders = await db.NoteFolders
                .Where(f => f.UserId == userId.Value && f.DeletedAt == null)
                .ToListAsync(ct);

            if (activeFolders.All(f => f.Id != id))
            {
                return Results.NotFound();
            }

            var subtreeIds = CollectSubtreeFolderIds(id, activeFolders, effectiveAccessFilter: null);
            var now = DateTimeOffset.UtcNow;

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            foreach (var folder in activeFolders.Where(f => subtreeIds.Contains(f.Id)))
            {
                folder.DeletedAt = now;
                folder.UpdatedAt = now;
            }

            var subtreeNotes = await db.Notes
                .Include(n => n.Tags)
                .Where(n => n.UserId == userId.Value && n.DeletedAt == null && n.FolderId != null && subtreeIds.Contains(n.FolderId.Value))
                .ToListAsync(ct);

            foreach (var note in subtreeNotes)
            {
                note.DeletedAt = now;
                note.UpdatedAt = now;
                if (note.Tags.Count > 0)
                {
                    db.NoteTags.RemoveRange(note.Tags);
                }
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.NoContent();
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapPost("/", async (
            CreateNoteRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var content = request.Content ?? string.Empty;
            if (content.Length > MaxNoteContentLength)
            {
                return Results.BadRequest(new { error = $"Note content must be {MaxNoteContentLength} characters or fewer." });
            }

            var parsed = NoteFrontmatter.Parse(content);
            var title = !string.IsNullOrWhiteSpace(request.Title)
                ? request.Title.Trim()
                : (!string.IsNullOrWhiteSpace(parsed.Title) ? parsed.Title.Trim() : "Untitled");

            if (title.Length == 0 || title.Length > MaxNoteTitleLength)
            {
                return Results.BadRequest(new { error = $"Note title must be 1–{MaxNoteTitleLength} characters." });
            }

            if (request.FolderId.HasValue && request.UniversityCourseId.HasValue)
            {
                return Results.BadRequest(new { error = "A note cannot belong to both a folder and a university course." });
            }

            var activeFolders = await db.NoteFolders
                .AsNoTracking()
                .Where(f => f.UserId == userId.Value && f.DeletedAt == null)
                .ToListAsync(ct);
            var folderMap = activeFolders.ToDictionary(f => f.Id);

            if (request.FolderId.HasValue && !folderMap.ContainsKey(request.FolderId.Value))
            {
                return Results.NotFound(new { error = "Folder not found." });
            }

            string? linkLabel = null;
            if (request.UniversityCourseId.HasValue)
            {
                var course = await db.UniversityCourses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == request.UniversityCourseId.Value && c.UserId == userId.Value, ct);
                if (course is null)
                {
                    return Results.NotFound(new { error = "University course not found." });
                }

                linkLabel = BuildCourseLinkLabel(course);
            }

            var now = DateTimeOffset.UtcNow;
            var note = new Note
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                FolderId = request.FolderId,
                UniversityCourseId = request.UniversityCourseId,
                Title = title,
                Content = content,
                ContentHash = NoteFrontmatter.ComputeContentHash(content),
                Version = 1,
                AllowAgentRead = request.AllowAgentRead,
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

            var effectiveFolderAccess = ComputeEffectiveFolderAccess(folderMap);
            var effectiveAgent = IsNoteAgentReadable(note, effectiveFolderAccess);

            return Results.Created($"/api/notes/{note.Id}", ToDetailDto(note, effectiveAgent, linkLabel));
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapPost("/linked", async (
            OpenLinkedNoteRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var kind = (request.Kind ?? string.Empty).Trim().ToLowerInvariant();
            if (kind == NoteLinkKinds.UniversityItem)
            {
                if (!request.UniversityActivityItemId.HasValue)
                {
                    return Results.BadRequest(new { error = "universityActivityItemId is required." });
                }

                var item = await db.UniversityActivityItems
                    .AsNoTracking()
                    .Include(i => i.Course)
                    .Include(i => i.Activity)
                    .FirstOrDefaultAsync(i => i.Id == request.UniversityActivityItemId.Value && i.UserId == userId.Value, ct);
                if (item is null)
                {
                    return Results.NotFound(new { error = "University activity item not found." });
                }

                var existing = await db.Notes
                    .AsNoTracking()
                    .Include(n => n.Tags)
                    .FirstOrDefaultAsync(
                        n => n.UserId == userId.Value
                            && n.UniversityActivityItemId == item.Id
                            && n.DeletedAt == null,
                        ct);

                var label = BuildItemLinkLabel(item.Course, item.Activity, item);
                if (existing is not null)
                {
                    return Results.Ok(ToDetailDto(existing, existing.AllowAgentRead, label));
                }

                var now = DateTimeOffset.UtcNow;
                var initialContent = !string.IsNullOrWhiteSpace(item.Notes)
                    ? item.Notes.Trim()
                    : string.Empty;
                var noteTitle = string.IsNullOrWhiteSpace(item.Title) ? "Untitled" : item.Title.Trim();
                if (noteTitle.Length > MaxNoteTitleLength) noteTitle = noteTitle[..MaxNoteTitleLength];

                var created = new Note
                {
                    Id = Guid.NewGuid(),
                    UserId = userId.Value,
                    UniversityCourseId = item.CourseId,
                    UniversityActivityItemId = item.Id,
                    Title = noteTitle,
                    Content = initialContent,
                    ContentHash = NoteFrontmatter.ComputeContentHash(initialContent),
                    Version = 1,
                    AllowAgentRead = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                db.Notes.Add(created);
                try
                {
                    await db.SaveChangesAsync(ct);
                    return Results.Created($"/api/notes/{created.Id}", ToDetailDto(created, created.AllowAgentRead, label));
                }
                catch (DbUpdateException)
                {
                    db.Entry(created).State = EntityState.Detached;
                    var raced = await db.Notes
                        .AsNoTracking()
                        .Include(n => n.Tags)
                        .FirstOrDefaultAsync(
                            n => n.UserId == userId.Value
                                && n.UniversityActivityItemId == item.Id
                                && n.DeletedAt == null,
                            ct);
                    if (raced is not null)
                    {
                        return Results.Ok(ToDetailDto(raced, raced.AllowAgentRead, label));
                    }

                    throw;
                }
            }

            if (kind == NoteLinkKinds.CalendarEvent)
            {
                if (!request.CalendarEventId.HasValue)
                {
                    return Results.BadRequest(new { error = "calendarEventId is required." });
                }

                var evt = await db.CalendarEvents
                    .AsNoTracking()
                    .Include(e => e.Calendar)
                    .Include(e => e.RecurrenceMaster)
                    .FirstOrDefaultAsync(e => e.Id == request.CalendarEventId.Value && e.UserId == userId.Value, ct);
                if (evt is null)
                {
                    return Results.NotFound(new { error = "Calendar event not found." });
                }

                var isUniEvent = string.Equals(evt.Calendar.Module, UniversityConstants.CalendarModuleKey, StringComparison.OrdinalIgnoreCase)
                    || await db.UniversityActivityItems.AnyAsync(
                        i => i.UserId == userId.Value && i.CalendarEventId == evt.Id,
                        ct);
                if (isUniEvent)
                {
                    return Results.BadRequest(new
                    {
                        error = "University calendar events use university_item notes instead of calendar_event notes.",
                    });
                }

                Guid masterEventId;
                DateTimeOffset? normalizedOccurrence;
                string baseTitle;
                bool isRecurring;

                if (evt.RecurrenceMasterId.HasValue)
                {
                    masterEventId = evt.RecurrenceMasterId.Value;
                    normalizedOccurrence = (request.OccurrenceStartAt ?? evt.RecurrenceOriginalStartAt ?? evt.StartAt).ToUniversalTime();
                    baseTitle = evt.RecurrenceMaster?.Title ?? evt.Title;
                    isRecurring = true;
                }
                else
                {
                    masterEventId = evt.Id;
                    isRecurring = !string.IsNullOrWhiteSpace(evt.RecurrenceRule);
                    normalizedOccurrence = isRecurring
                        ? request.OccurrenceStartAt?.ToUniversalTime()
                        : null;
                    baseTitle = evt.Title;
                }

                var existing = await db.Notes
                    .AsNoTracking()
                    .Include(n => n.Tags)
                    .FirstOrDefaultAsync(
                        n => n.UserId == userId.Value
                            && n.CalendarEventId == masterEventId
                            && n.CalendarOccurrenceStartAt == normalizedOccurrence
                            && n.DeletedAt == null,
                        ct);

                var label = BuildEventLinkLabel(baseTitle, normalizedOccurrence, isRecurring);
                if (existing is not null)
                {
                    return Results.Ok(ToDetailDto(existing, existing.AllowAgentRead, label));
                }

                var title = normalizedOccurrence.HasValue
                    ? $"{baseTitle} — {normalizedOccurrence.Value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}"
                    : (isRecurring ? $"{baseTitle} (Shared)" : baseTitle);
                if (title.Length > MaxNoteTitleLength) title = title[..MaxNoteTitleLength];

                var now = DateTimeOffset.UtcNow;
                var created = new Note
                {
                    Id = Guid.NewGuid(),
                    UserId = userId.Value,
                    CalendarEventId = masterEventId,
                    CalendarOccurrenceStartAt = normalizedOccurrence,
                    Title = title,
                    Content = string.Empty,
                    ContentHash = NoteFrontmatter.ComputeContentHash(string.Empty),
                    Version = 1,
                    AllowAgentRead = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                db.Notes.Add(created);
                try
                {
                    await db.SaveChangesAsync(ct);
                    return Results.Created($"/api/notes/{created.Id}", ToDetailDto(created, created.AllowAgentRead, label));
                }
                catch (DbUpdateException)
                {
                    db.Entry(created).State = EntityState.Detached;
                    var raced = await db.Notes
                        .AsNoTracking()
                        .Include(n => n.Tags)
                        .FirstOrDefaultAsync(
                            n => n.UserId == userId.Value
                                && n.CalendarEventId == masterEventId
                                && n.CalendarOccurrenceStartAt == normalizedOccurrence
                                && n.DeletedAt == null,
                            ct);
                    if (raced is not null)
                    {
                        return Results.Ok(ToDetailDto(raced, raced.AllowAgentRead, label));
                    }

                    throw;
                }
            }

            return Results.BadRequest(new { error = "kind must be 'university_item' or 'calendar_event'." });
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateNoteRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var note = await db.Notes
                .Include(n => n.Tags)
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId.Value && n.DeletedAt == null, ct);
            if (note is null) return Results.NotFound();

            var activeFolders = await db.NoteFolders
                .AsNoTracking()
                .Where(f => f.UserId == userId.Value && f.DeletedAt == null)
                .ToListAsync(ct);
            var folderMap = activeFolders.ToDictionary(f => f.Id);
            var effectiveFolderAccess = ComputeEffectiveFolderAccess(folderMap);

            if (request.ExpectedVersion.HasValue && request.ExpectedVersion.Value != note.Version)
            {
                return Results.Conflict(new NoteConflictResponse(
                    "Note was modified by another session.",
                    ToDetailDto(note, IsNoteAgentReadable(note, effectiveFolderAccess))));
            }

            if (request.Content is not null && request.Content.Length > MaxNoteContentLength)
            {
                return Results.BadRequest(new { error = $"Note content must be {MaxNoteContentLength} characters or fewer." });
            }

            if (request.Title is not null)
            {
                var trimmedTitle = request.Title.Trim();
                if (trimmedTitle.Length == 0 || trimmedTitle.Length > MaxNoteTitleLength)
                {
                    return Results.BadRequest(new { error = $"Note title must be 1–{MaxNoteTitleLength} characters." });
                }

                note.Title = trimmedTitle;
            }

            if (request.MoveToRoot)
            {
                note.FolderId = null;
            }
            else if (request.FolderId.HasValue)
            {
                if (!IsGeneralNote(note))
                {
                    return Results.BadRequest(new { error = "Linked university or event notes cannot be moved into general folders." });
                }

                if (!folderMap.ContainsKey(request.FolderId.Value))
                {
                    return Results.NotFound(new { error = "Target folder not found." });
                }

                note.FolderId = request.FolderId.Value;
            }

            if (request.AllowAgentRead.HasValue)
            {
                note.AllowAgentRead = request.AllowAgentRead.Value;
            }

            if (request.Content is not null)
            {
                note.Content = request.Content;
                note.ContentHash = NoteFrontmatter.ComputeContentHash(request.Content);

                var parsed = NoteFrontmatter.Parse(request.Content);
                if (request.Title is null && !string.IsNullOrWhiteSpace(parsed.Title))
                {
                    var fmTitle = parsed.Title.Trim();
                    if (fmTitle.Length <= MaxNoteTitleLength)
                    {
                        note.Title = fmTitle;
                    }
                }

                var desiredTags = new HashSet<string>(parsed.Tags, StringComparer.Ordinal);
                var toRemove = note.Tags.Where(t => !desiredTags.Contains(t.Tag)).ToList();
                foreach (var oldTag in toRemove)
                {
                    note.Tags.Remove(oldTag);
                    db.NoteTags.Remove(oldTag);
                }

                var existingTagNames = note.Tags.Select(t => t.Tag).ToHashSet(StringComparer.Ordinal);
                foreach (var newTag in desiredTags)
                {
                    if (!existingTagNames.Contains(newTag))
                    {
                        note.Tags.Add(new NoteTag
                        {
                            NoteId = note.Id,
                            UserId = userId.Value,
                            Tag = newTag,
                        });
                    }
                }
            }

            note.Version += 1;
            note.UpdatedAt = DateTimeOffset.UtcNow;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                var latest = await db.Notes
                    .AsNoTracking()
                    .Include(n => n.Tags)
                    .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId.Value && n.DeletedAt == null, ct);
                if (latest is null)
                {
                    return Results.NotFound();
                }

                return Results.Conflict(new NoteConflictResponse(
                    "Note was modified by another session.",
                    ToDetailDto(latest, IsNoteAgentReadable(latest, effectiveFolderAccess))));
            }

            var effectiveAgent = IsNoteAgentReadable(note, effectiveFolderAccess);
            var labels = await BuildLinkLabelsAsync(db, userId.Value, [note], ct);
            return Results.Ok(ToDetailDto(note, effectiveAgent, labels.GetValueOrDefault(note.Id)));
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var note = await db.Notes
                .Include(n => n.Tags)
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId.Value && n.DeletedAt == null, ct);
            if (note is null) return Results.NotFound();

            var now = DateTimeOffset.UtcNow;
            note.DeletedAt = now;
            note.UpdatedAt = now;
            if (note.Tags.Count > 0)
            {
                db.NoteTags.RemoveRange(note.Tags);
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);
    }

    private static IResult? CheckNotesReadAccess(
        ClaimsPrincipal principal,
        out Guid userId,
        out bool isPat)
    {
        userId = Guid.Empty;
        isPat = false;

        var resolvedUserId = principal.GetUserId();
        if (resolvedUserId is null)
        {
            return Results.Unauthorized();
        }

        userId = resolvedUserId.Value;

        var authType = principal.FindFirstValue(AuthConstants.AuthTypeClaim);
        if (string.Equals(authType, AuthConstants.PatAuthType, StringComparison.Ordinal))
        {
            isPat = true;
            var hasScope = principal.FindAll(AuthConstants.ScopeClaim)
                .Any(c => string.Equals(c.Value, "notes:read", StringComparison.Ordinal));
            if (!hasScope)
            {
                return Results.Forbid();
            }
        }

        return null;
    }

    private static Dictionary<Guid, bool> ComputeEffectiveFolderAccess(
        Dictionary<Guid, NoteFolder> folderMap)
    {
        var result = new Dictionary<Guid, bool>(folderMap.Count);
        foreach (var id in folderMap.Keys)
        {
            result[id] = ResolveFolderAccess(id, folderMap, result, []);
        }

        return result;
    }

    private static bool ResolveFolderAccess(
        Guid folderId,
        Dictionary<Guid, NoteFolder> folderMap,
        Dictionary<Guid, bool> cache,
        HashSet<Guid> visiting)
    {
        if (cache.TryGetValue(folderId, out var cached))
        {
            return cached;
        }

        if (!folderMap.TryGetValue(folderId, out var folder) || !visiting.Add(folderId))
        {
            return false;
        }

        var allowed = folder.AllowAgentRead
            && (!folder.ParentFolderId.HasValue
                || ResolveFolderAccess(folder.ParentFolderId.Value, folderMap, cache, visiting));
        visiting.Remove(folderId);
        cache[folderId] = allowed;
        return allowed;
    }

    private static bool IsNoteAgentReadable(
        Note note,
        Dictionary<Guid, bool> effectiveFolderAccess)
    {
        if (!note.AllowAgentRead)
        {
            return false;
        }

        if (note.FolderId.HasValue)
        {
            return effectiveFolderAccess.GetValueOrDefault(note.FolderId.Value, false);
        }

        return true;
    }

    private static bool IsDescendantFolder(
        Guid candidateAncestorId,
        Guid targetFolderId,
        Dictionary<Guid, NoteFolder> folderMap)
    {
        var visited = new HashSet<Guid>();
        Guid? cursor = targetFolderId;
        while (cursor.HasValue && visited.Add(cursor.Value))
        {
            if (cursor.Value == candidateAncestorId)
            {
                return true;
            }

            cursor = folderMap.TryGetValue(cursor.Value, out var f) ? f.ParentFolderId : null;
        }

        return false;
    }

    private static HashSet<Guid> CollectSubtreeFolderIds(
        Guid rootFolderId,
        List<NoteFolder> activeFolders,
        Dictionary<Guid, bool>? effectiveAccessFilter)
    {
        var childrenByParent = activeFolders
            .Where(f => f.ParentFolderId.HasValue)
            .GroupBy(f => f.ParentFolderId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(f => f.Id).ToList());

        var result = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(rootFolderId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (effectiveAccessFilter is not null && !effectiveAccessFilter.GetValueOrDefault(current, false))
            {
                continue;
            }

            if (!result.Add(current))
            {
                continue;
            }

            if (childrenByParent.TryGetValue(current, out var kids))
            {
                foreach (var kid in kids)
                {
                    queue.Enqueue(kid);
                }
            }
        }

        return result;
    }

    private static NoteFolderDto ToFolderDto(NoteFolder f, bool effectiveAgent) => new(
        f.Id,
        f.ParentFolderId,
        f.Name,
        f.AllowAgentRead,
        effectiveAgent,
        f.SortOrder,
        f.CreatedAt,
        f.UpdatedAt);

    private static bool HasScope(ClaimsPrincipal principal, string scope) =>
        principal.FindAll(AuthConstants.ScopeClaim)
            .Any(c => string.Equals(c.Value, scope, StringComparison.Ordinal));

    private static bool IsGeneralNote(Note n) =>
        !n.UniversityCourseId.HasValue && !n.UniversityActivityItemId.HasValue && !n.CalendarEventId.HasValue;

    private static bool IsUniversityNote(Note n) =>
        n.UniversityCourseId.HasValue || n.UniversityActivityItemId.HasValue;

    private static bool IsEventNote(Note n) =>
        n.CalendarEventId.HasValue;

    private static string GetLinkKind(Note n)
    {
        if (n.UniversityActivityItemId.HasValue) return NoteLinkKinds.UniversityItem;
        if (n.UniversityCourseId.HasValue) return NoteLinkKinds.UniversityCourse;
        if (n.CalendarEventId.HasValue) return NoteLinkKinds.CalendarEvent;
        return NoteLinkKinds.General;
    }

    private static string BuildCourseLinkLabel(UniversityCourse course)
    {
        var sem = string.IsNullOrWhiteSpace(course.Semester) ? "Uncategorized" : course.Semester.Trim();
        return $"{sem} / {course.Name}";
    }

    private static string BuildItemLinkLabel(
        UniversityCourse course,
        UniversityCourseActivity activity,
        UniversityActivityItem item)
    {
        var sem = string.IsNullOrWhiteSpace(course.Semester) ? "Uncategorized" : course.Semester.Trim();
        var actLabel = string.IsNullOrWhiteSpace(activity.Title)
            ? UniversityConstants.GetDefaultActivityLabel(activity.ActivityType)
            : activity.Title.Trim();
        return $"{sem} / {course.Name} / {actLabel} / {item.Title}";
    }

    private static string BuildEventLinkLabel(
        string eventTitle,
        DateTimeOffset? occurrenceStartAt,
        bool isRecurring)
    {
        if (occurrenceStartAt.HasValue)
        {
            return $"{eventTitle} · {occurrenceStartAt.Value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}";
        }

        return isRecurring ? $"{eventTitle} · Shared" : eventTitle;
    }

    private static async Task<Dictionary<Guid, string>> BuildLinkLabelsAsync(
        AppDbContext db,
        Guid userId,
        IReadOnlyList<Note> notes,
        CancellationToken ct)
    {
        var labels = new Dictionary<Guid, string>();
        var courseIds = notes
            .Where(n => n.UniversityCourseId.HasValue && !n.UniversityActivityItemId.HasValue)
            .Select(n => n.UniversityCourseId!.Value)
            .Distinct()
            .ToList();
        var itemIds = notes
            .Where(n => n.UniversityActivityItemId.HasValue)
            .Select(n => n.UniversityActivityItemId!.Value)
            .Distinct()
            .ToList();
        var eventIds = notes
            .Where(n => n.CalendarEventId.HasValue)
            .Select(n => n.CalendarEventId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, UniversityCourse> courses = [];
        if (courseIds.Count > 0)
        {
            courses = await db.UniversityCourses
                .AsNoTracking()
                .Where(c => c.UserId == userId && courseIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct);
        }

        Dictionary<Guid, UniversityActivityItem> items = [];
        if (itemIds.Count > 0)
        {
            items = await db.UniversityActivityItems
                .AsNoTracking()
                .Include(i => i.Course)
                .Include(i => i.Activity)
                .Where(i => i.UserId == userId && itemIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, ct);
        }

        Dictionary<Guid, CalendarEvent> events = [];
        if (eventIds.Count > 0)
        {
            events = await db.CalendarEvents
                .AsNoTracking()
                .Where(e => e.UserId == userId && eventIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, ct);
        }

        foreach (var note in notes)
        {
            if (note.UniversityActivityItemId.HasValue && items.TryGetValue(note.UniversityActivityItemId.Value, out var item))
            {
                labels[note.Id] = BuildItemLinkLabel(item.Course, item.Activity, item);
            }
            else if (note.UniversityCourseId.HasValue && courses.TryGetValue(note.UniversityCourseId.Value, out var course))
            {
                labels[note.Id] = BuildCourseLinkLabel(course);
            }
            else if (note.CalendarEventId.HasValue && events.TryGetValue(note.CalendarEventId.Value, out var evt))
            {
                var isRec = !string.IsNullOrWhiteSpace(evt.RecurrenceRule) || evt.RecurrenceMasterId.HasValue;
                labels[note.Id] = BuildEventLinkLabel(evt.Title, note.CalendarOccurrenceStartAt, isRec);
            }
        }

        return labels;
    }

    private static async Task<IReadOnlyList<UniversitySemesterNotesTreeDto>> BuildUniversityTreeAsync(
        AppDbContext db,
        Guid userId,
        bool isPat,
        IReadOnlyList<Note> activeNotes,
        Dictionary<Guid, bool> effectiveFolderAccess,
        CancellationToken ct)
    {
        var courses = await db.UniversityCourses
            .AsNoTracking()
            .Include(c => c.Activities)
                .ThenInclude(a => a.Items)
            .Where(c => c.UserId == userId)
            .ToListAsync(ct);

        if (courses.Count == 0)
        {
            return [];
        }

        var uniArtifacts = await db.Attachments
            .AsNoTracking()
            .Where(a => a.UserId == userId
                && a.DeletedAt == null
                && a.Kind == AttachmentKinds.Artifact
                && a.UniversityCourseId != null)
            .ToListAsync(ct);

        var courseNotesLookup = activeNotes
            .Where(n => n.UniversityCourseId.HasValue && !n.UniversityActivityItemId.HasValue)
            .Select(n => (Note: n, EffectiveAgent: IsNoteAgentReadable(n, effectiveFolderAccess)))
            .Where(x => !isPat || x.EffectiveAgent)
            .GroupBy(x => x.Note.UniversityCourseId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var itemNoteLookup = activeNotes
            .Where(n => n.UniversityActivityItemId.HasValue)
            .ToDictionary(n => n.UniversityActivityItemId!.Value);

        var courseArtifactsLookup = uniArtifacts
            .Where(a => !a.UniversityActivityItemId.HasValue && a.UniversityCourseId.HasValue && (!isPat || a.AllowAgentRead))
            .GroupBy(a => a.UniversityCourseId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<AttachmentDto>)g.OrderBy(a => a.CreatedAt).Select(AttachmentEndpoints.ToDto).ToList());

        var itemArtifactsLookup = uniArtifacts
            .Where(a => a.UniversityActivityItemId.HasValue && (!isPat || a.AllowAgentRead))
            .GroupBy(a => a.UniversityActivityItemId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.CreatedAt).ToList());

        var semesterGroups = courses
            .GroupBy(c => string.IsNullOrWhiteSpace(c.Semester) ? "Uncategorized" : c.Semester.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(semGroup =>
            {
                var courseDtos = semGroup
                    .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(c =>
                    {
                        var courseLabel = BuildCourseLinkLabel(c);
                        var freeformNotes = courseNotesLookup.TryGetValue(c.Id, out var cNotes)
                            ? cNotes
                                .OrderByDescending(x => x.Note.UpdatedAt)
                                .Select(x => ToSummaryDto(x.Note, x.EffectiveAgent, null, courseLabel))
                                .ToList()
                            : [];

                        var cArtifacts = courseArtifactsLookup.GetValueOrDefault(c.Id, []);

                        var activityDtos = c.Activities
                            .OrderBy(a => a.SortOrder)
                            .ThenBy(a => a.CreatedAt)
                            .Select(a =>
                            {
                                var actTitle = string.IsNullOrWhiteSpace(a.Title)
                                    ? UniversityConstants.GetDefaultActivityLabel(a.ActivityType)
                                    : a.Title.Trim();

                                var itemDtos = a.Items
                                    .OrderBy(i => i.SequenceNumber ?? int.MaxValue)
                                    .ThenBy(i => i.CreatedAt)
                                    .Select(i =>
                                    {
                                        NoteSummaryDto? noteDto = null;
                                        var itemHiddenFromAgent = false;
                                        if (itemNoteLookup.TryGetValue(i.Id, out var linkedNote))
                                        {
                                            var eff = IsNoteAgentReadable(linkedNote, effectiveFolderAccess);
                                            if (!eff) itemHiddenFromAgent = true;
                                            if (!isPat || eff)
                                            {
                                                noteDto = ToSummaryDto(
                                                    linkedNote,
                                                    eff,
                                                    null,
                                                    BuildItemLinkLabel(c, a, i));
                                            }
                                        }

                                        IReadOnlyList<AttachmentDto> iArtifacts =
                                            isPat && itemHiddenFromAgent
                                                ? []
                                                : (itemArtifactsLookup.TryGetValue(i.Id, out var rawArts)
                                                    ? rawArts.Select(AttachmentEndpoints.ToDto).ToList()
                                                    : []);

                                        return new UniversityItemNotesTreeDto(
                                            i.Id,
                                            i.Title,
                                            i.SequenceNumber,
                                            noteDto,
                                            iArtifacts);
                                    })
                                    .ToList();

                                return new UniversityActivityNotesTreeDto(a.Id, a.ActivityType, actTitle, itemDtos);
                            })
                            .ToList();

                        return new UniversityCourseNotesTreeDto(
                            c.Id,
                            c.Name,
                            c.Code,
                            c.Color,
                            semGroup.Key,
                            freeformNotes,
                            cArtifacts,
                            activityDtos);
                    })
                    .ToList();

                return new UniversitySemesterNotesTreeDto(semGroup.Key, courseDtos);
            })
            .ToList();

        return semesterGroups;
    }

    private static async Task<IReadOnlyList<EventNotesGroupDto>> BuildEventsTreeAsync(
        AppDbContext db,
        Guid userId,
        bool isPat,
        IReadOnlyList<Note> activeNotes,
        Dictionary<Guid, bool> effectiveFolderAccess,
        CancellationToken ct)
    {
        var eventNotes = activeNotes
            .Where(n => n.CalendarEventId.HasValue)
            .ToList();

        var eventArtifacts = await db.Attachments
            .AsNoTracking()
            .Where(a => a.UserId == userId
                && a.DeletedAt == null
                && a.Kind == AttachmentKinds.Artifact
                && a.CalendarEventId != null)
            .ToListAsync(ct);

        var eventIds = eventNotes
            .Select(n => n.CalendarEventId!.Value)
            .Concat(eventArtifacts.Select(a => a.CalendarEventId!.Value))
            .Distinct()
            .ToList();

        if (eventIds.Count == 0)
        {
            return [];
        }

        var uniLinkedEventIds = await db.UniversityActivityItems
            .AsNoTracking()
            .Where(i => i.UserId == userId && i.CalendarEventId != null && eventIds.Contains(i.CalendarEventId.Value))
            .Select(i => i.CalendarEventId!.Value)
            .ToListAsync(ct);
        var uniLinkedSet = uniLinkedEventIds.ToHashSet();

        var events = await db.CalendarEvents
            .AsNoTracking()
            .Include(e => e.Calendar)
            .Where(e => e.UserId == userId && eventIds.Contains(e.Id))
            .ToListAsync(ct);

        var validEvents = events
            .Where(e =>
                !string.Equals(e.Calendar.Module, UniversityConstants.CalendarModuleKey, StringComparison.OrdinalIgnoreCase)
                && !uniLinkedSet.Contains(e.Id))
            .ToDictionary(e => e.Id);

        var groups = new List<EventNotesGroupDto>();
        foreach (var evt in validEvents.Values.OrderByDescending(e => e.StartAt))
        {
            var isRecurring = !string.IsNullOrWhiteSpace(evt.RecurrenceRule);
            var notesForEvent = eventNotes.Where(n => n.CalendarEventId == evt.Id).ToList();
            var artifactsForEvent = eventArtifacts.Where(a => a.CalendarEventId == evt.Id).ToList();

            var sharedNoteRaw = notesForEvent.FirstOrDefault(n => !n.CalendarOccurrenceStartAt.HasValue);
            var sharedHiddenFromAgent = sharedNoteRaw is not null && !IsNoteAgentReadable(sharedNoteRaw, effectiveFolderAccess);
            NoteSummaryDto? sharedNoteDto = null;
            if (sharedNoteRaw is not null)
            {
                var eff = IsNoteAgentReadable(sharedNoteRaw, effectiveFolderAccess);
                if (!isPat || eff)
                {
                    sharedNoteDto = ToSummaryDto(
                        sharedNoteRaw,
                        eff,
                        null,
                        BuildEventLinkLabel(evt.Title, null, isRecurring));
                }
            }

            var sharedArtifacts = (isPat && sharedHiddenFromAgent)
                ? new List<AttachmentDto>()
                : artifactsForEvent
                    .Where(a => !a.CalendarOccurrenceStartAt.HasValue && (!isPat || a.AllowAgentRead))
                    .OrderBy(a => a.CreatedAt)
                    .Select(AttachmentEndpoints.ToDto)
                    .ToList();

            var occurrenceStarts = notesForEvent
                .Where(n => n.CalendarOccurrenceStartAt.HasValue)
                .Select(n => n.CalendarOccurrenceStartAt!.Value.ToUniversalTime())
                .Concat(artifactsForEvent
                    .Where(a => a.CalendarOccurrenceStartAt.HasValue)
                    .Select(a => a.CalendarOccurrenceStartAt!.Value.ToUniversalTime()))
                .Distinct()
                .OrderByDescending(d => d)
                .ToList();

            var occurrences = new List<EventOccurrenceNotesDto>();
            foreach (var occStart in occurrenceStarts)
            {
                var occNoteRaw = notesForEvent.FirstOrDefault(
                    n => n.CalendarOccurrenceStartAt.HasValue
                        && n.CalendarOccurrenceStartAt.Value.ToUniversalTime() == occStart);
                var occHiddenFromAgent = occNoteRaw is not null && !IsNoteAgentReadable(occNoteRaw, effectiveFolderAccess);
                NoteSummaryDto? occNoteDto = null;
                if (occNoteRaw is not null)
                {
                    var eff = IsNoteAgentReadable(occNoteRaw, effectiveFolderAccess);
                    if (!isPat || eff)
                    {
                        occNoteDto = ToSummaryDto(
                            occNoteRaw,
                            eff,
                            null,
                            BuildEventLinkLabel(evt.Title, occStart, isRecurring));
                    }
                }

                var occArtifacts = (isPat && occHiddenFromAgent)
                    ? new List<AttachmentDto>()
                    : artifactsForEvent
                        .Where(a => a.CalendarOccurrenceStartAt.HasValue
                            && a.CalendarOccurrenceStartAt.Value.ToUniversalTime() == occStart
                            && (!isPat || a.AllowAgentRead))
                        .OrderBy(a => a.CreatedAt)
                        .Select(AttachmentEndpoints.ToDto)
                        .ToList();

                if (occNoteDto is not null || occArtifacts.Count > 0)
                {
                    occurrences.Add(new EventOccurrenceNotesDto(occStart, occNoteDto, occArtifacts));
                }
            }

            if (sharedNoteDto is not null || sharedArtifacts.Count > 0 || occurrences.Count > 0)
            {
                groups.Add(new EventNotesGroupDto(
                    evt.Id,
                    evt.Title,
                    evt.StartAt.ToUniversalTime(),
                    isRecurring,
                    sharedNoteDto,
                    sharedArtifacts,
                    occurrences));
            }
        }

        return groups;
    }

    private static NoteSummaryDto ToSummaryDto(
        Note n,
        bool effectiveAgent,
        string? highlightTerm = null,
        string? linkLabel = null)
    {
        var parsed = NoteFrontmatter.Parse(n.Content);
        return new NoteSummaryDto(
            n.Id,
            n.FolderId,
            n.Title,
            n.Tags.Select(t => t.Tag).OrderBy(t => t, StringComparer.Ordinal).ToList(),
            NoteFrontmatter.BuildExcerpt(parsed.Body, highlightTerm: highlightTerm),
            n.AllowAgentRead,
            effectiveAgent,
            n.Version,
            n.CreatedAt,
            n.UpdatedAt,
            n.UniversityCourseId,
            n.UniversityActivityItemId,
            n.CalendarEventId,
            n.CalendarOccurrenceStartAt,
            GetLinkKind(n),
            linkLabel);
    }

    private static NoteDetailDto ToDetailDto(Note n, bool effectiveAgent, string? linkLabel = null) => new(
        n.Id,
        n.FolderId,
        n.Title,
        n.Content,
        n.Tags.Select(t => t.Tag).OrderBy(t => t, StringComparer.Ordinal).ToList(),
        n.ContentHash,
        n.Version,
        n.AllowAgentRead,
        effectiveAgent,
        n.CreatedAt,
        n.UpdatedAt,
        n.UniversityCourseId,
        n.UniversityActivityItemId,
        n.CalendarEventId,
        n.CalendarOccurrenceStartAt,
        GetLinkKind(n),
        linkLabel);
}

