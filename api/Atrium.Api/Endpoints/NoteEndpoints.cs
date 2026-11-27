using System.Security.Claims;
using Atrium.Api.Auth;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Atrium.Api.Notes;
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

            var visibleNotes = activeNotes
                .Select(n => (Note: n, EffectiveAgent: IsNoteAgentReadable(n, effectiveFolderAccess)))
                .Where(x => !isPat || x.EffectiveAgent)
                .OrderByDescending(x => x.Note.UpdatedAt)
                .Select(x => ToSummaryDto(x.Note, x.EffectiveAgent))
                .ToList();

            var tagCounts = visibleNotes
                .SelectMany(n => n.Tags)
                .GroupBy(t => t, StringComparer.Ordinal)
                .Select(g => new NoteTagCountDto(g.Key, g.Count()))
                .OrderByDescending(t => t.Count)
                .ThenBy(t => t.Tag, StringComparer.Ordinal)
                .ToList();

            return Results.Ok(new NotesTreeResponse(visibleFolders, visibleNotes, tagCounts));
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

            var results = candidates
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
                    normalizedSearchIn == "content" ? searchTerm : null))
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

            return Results.Ok(ToDetailDto(note, effectiveAgent));
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

            var activeFolders = await db.NoteFolders
                .AsNoTracking()
                .Where(f => f.UserId == userId.Value && f.DeletedAt == null)
                .ToListAsync(ct);
            var folderMap = activeFolders.ToDictionary(f => f.Id);

            if (request.FolderId.HasValue && !folderMap.ContainsKey(request.FolderId.Value))
            {
                return Results.NotFound(new { error = "Folder not found." });
            }

            var now = DateTimeOffset.UtcNow;
            var note = new Note
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                FolderId = request.FolderId,
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

            return Results.Created($"/api/notes/{note.Id}", ToDetailDto(note, effectiveAgent));
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
            return Results.Ok(ToDetailDto(note, effectiveAgent));
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

    private static NoteSummaryDto ToSummaryDto(
        Note n,
        bool effectiveAgent,
        string? highlightTerm = null)
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
            n.UpdatedAt);
    }

    private static NoteDetailDto ToDetailDto(Note n, bool effectiveAgent) => new(
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
        n.UpdatedAt);
}

