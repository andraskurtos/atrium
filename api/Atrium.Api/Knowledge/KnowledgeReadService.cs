using System.Globalization;
using System.Security.Claims;
using Atrium.Api.Attachments;
using Atrium.Api.Auth;
using Atrium.Api.Calendar;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Atrium.Api.Days;
using Atrium.Api.Endpoints;
using Atrium.Api.Notes;
using Atrium.Api.University;
using Microsoft.EntityFrameworkCore;

namespace Atrium.Api.Knowledge;

public sealed class KnowledgeReadService(
    AppDbContext db,
    ArtifactTextService textService)
{
    public const int MaxEventRangeDays = 90;
    public const int DefaultArtifactMaxChunks = 10;
    public const int MaxArtifactChunksPerPage = 20;
    public const int DefaultSearchLimit = 10;
    public const int MaxSearchLimit = 25;
    private const int MaxSearchQueryLength = 300;
    private const int SnippetMaxLength = 240;

    private bool IsPostgres =>
        string.Equals(db.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal);

    public async Task<KnowledgeResult<KnowledgeDayResponse>> GetDayAsync(
        ClaimsPrincipal principal,
        bool enforceAgentPrivacy,
        string? date,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return KnowledgeResult.Unauthorized<KnowledgeDayResponse>();

        var enforceScopes = ShouldEnforceAgentRules(principal, enforceAgentPrivacy);
        if (enforceScopes && !HasScope(principal, "tasks:read"))
        {
            return KnowledgeResult.Forbidden<KnowledgeDayResponse>("Missing required scope: tasks:read");
        }

        DateOnly parsedDate;
        if (string.IsNullOrWhiteSpace(date))
        {
            var userTz = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId.Value)
                .Select(u => u.Timezone)
                .FirstOrDefaultAsync(ct);
            var tz = ResolveTimeZone(userTz);
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            parsedDate = DateOnly.FromDateTime(localNow);
        }
        else if (!DateOnly.TryParseExact(
            date.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out parsedDate))
        {
            return KnowledgeResult.BadRequest<KnowledgeDayResponse>("date must be a valid yyyy-MM-dd date.");
        }

        var items = (await db.DailyCheckItems
            .AsNoTracking()
            .Where(i => i.UserId == userId.Value && i.Date == parsedDate)
            .OrderBy(i => i.SortOrder)
            .ToListAsync(ct))
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.CreatedAt)
            .Select(i => new DailyCheckItemDto(
                i.Id,
                FormatDate(i.Date),
                i.Title,
                i.IsCompleted,
                i.CompletedAt,
                i.DueTime,
                i.SortOrder,
                i.CreatedAt,
                i.UpdatedAt))
            .ToList();

        var linkedNote = await db.Notes
            .AsNoTracking()
            .FirstOrDefaultAsync(
                n => n.UserId == userId.Value && n.DailyDate == parsedDate && n.DeletedAt == null,
                ct);

        DailyNoteDto? noteDto = null;
        var noteHidden = false;
        var canReadNotes = !enforceScopes || HasScope(principal, "notes:read");

        if (linkedNote is not null)
        {
            if (canReadNotes && (!enforceScopes || linkedNote.AllowAgentRead))
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
                noteHidden = true;
            }
        }
        else
        {
            var legacyNote = await db.DailyNotes
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.UserId == userId.Value && n.Date == parsedDate, ct);
            if (legacyNote is not null)
            {
                if (canReadNotes)
                {
                    noteDto = new DailyNoteDto(
                        legacyNote.Id,
                        FormatDate(legacyNote.Date),
                        legacyNote.Content,
                        legacyNote.CreatedAt,
                        legacyNote.UpdatedAt);
                }
                else
                {
                    noteHidden = true;
                }
            }
        }

        return KnowledgeResult.Ok<KnowledgeDayResponse>(new KnowledgeDayResponse(
            FormatDate(parsedDate),
            items,
            noteDto,
            noteHidden));
    }

    public async Task<KnowledgeResult<KnowledgeEventsResponse>> ListEventsAsync(
        ClaimsPrincipal principal,
        bool enforceAgentPrivacy,
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? calendarId,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return KnowledgeResult.Unauthorized<KnowledgeEventsResponse>();

        var enforceScopes = ShouldEnforceAgentRules(principal, enforceAgentPrivacy);
        if (enforceScopes && !HasScope(principal, "calendar:read"))
        {
            return KnowledgeResult.Forbidden<KnowledgeEventsResponse>("Missing required scope: calendar:read");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var resolvedFrom = (from ?? new DateTimeOffset(nowUtc.UtcDateTime.Date, TimeSpan.Zero)).ToUniversalTime();
        var resolvedTo = (to ?? resolvedFrom.AddDays(7)).ToUniversalTime();

        if (resolvedTo < resolvedFrom)
        {
            return KnowledgeResult.BadRequest<KnowledgeEventsResponse>("'to' must be greater than or equal to 'from'.");
        }

        var clamped = false;
        if (resolvedTo - resolvedFrom > TimeSpan.FromDays(MaxEventRangeDays))
        {
            resolvedTo = resolvedFrom.AddDays(MaxEventRangeDays);
            clamped = true;
        }

        IQueryable<CalendarEvent> baseQuery = db.CalendarEvents
            .AsNoTracking()
            .Include(e => e.Calendar)
            .Where(e => e.UserId == userId.Value);

        if (calendarId.HasValue)
        {
            baseQuery = baseQuery.Where(e => e.CalendarId == calendarId.Value);
        }

        var nonMasters = await baseQuery
            .Include(e => e.RecurrenceMaster)
            .Where(e =>
                !e.IsRecurrenceExceptionDeleted
                && (e.RecurrenceMasterId != null || e.RecurrenceRule == null)
                && (e.EndAt > resolvedFrom || (e.StartAt == e.EndAt && e.StartAt >= resolvedFrom))
                && e.StartAt < resolvedTo)
            .ToListAsync(ct);

        var masters = await baseQuery
            .Where(e => e.RecurrenceMasterId == null && e.RecurrenceRule != null && e.StartAt < resolvedTo)
            .ToListAsync(ct);

        var suppressedByMaster = new Dictionary<Guid, HashSet<DateTimeOffset>>();
        if (masters.Count > 0)
        {
            var masterIds = masters.Select(m => m.Id).ToList();
            var overrides = await db.CalendarEvents
                .AsNoTracking()
                .Where(e =>
                    e.UserId == userId.Value
                    && e.RecurrenceMasterId != null
                    && masterIds.Contains(e.RecurrenceMasterId.Value)
                    && e.RecurrenceOriginalStartAt != null)
                .Select(e => new { MasterId = e.RecurrenceMasterId!.Value, OriginalStart = e.RecurrenceOriginalStartAt!.Value })
                .ToListAsync(ct);

            foreach (var ov in overrides)
            {
                if (!suppressedByMaster.TryGetValue(ov.MasterId, out var set))
                {
                    set = [];
                    suppressedByMaster[ov.MasterId] = set;
                }

                set.Add(ov.OriginalStart.ToUniversalTime());
            }
        }

        var rawResults = new List<CalendarEventDto>(nonMasters.Count + masters.Count);
        foreach (var item in nonMasters)
        {
            rawResults.Add(ToEventDto(item, item.Calendar));
        }

        foreach (var master in masters)
        {
            var masterStartUtc = master.StartAt.ToUniversalTime();
            suppressedByMaster.TryGetValue(master.Id, out var suppressedStarts);

            if (!RRuleExpander.TryParse(master.RecurrenceRule, out _, out _))
            {
                var masterEndUtc = master.EndAt.ToUniversalTime();
                var overlaps = masterEndUtc > resolvedFrom
                    || (masterStartUtc == masterEndUtc && masterStartUtc >= resolvedFrom);
                if (overlaps && masterStartUtc < resolvedTo
                    && (suppressedStarts is null || !suppressedStarts.Contains(masterStartUtc)))
                {
                    rawResults.Add(ToEventDto(master, master.Calendar));
                }

                continue;
            }

            var expanded = RRuleExpander.ExpandOccurrences(
                masterStartUtc,
                master.EndAt.ToUniversalTime(),
                master.IsAllDay,
                master.Timezone ?? master.Calendar.Timezone,
                master.RecurrenceRule!,
                resolvedFrom,
                resolvedTo);

            foreach (var (occStart, occEnd) in expanded)
            {
                if (suppressedStarts is not null && suppressedStarts.Contains(occStart))
                {
                    continue;
                }

                rawResults.Add(ToExpandedOccurrenceDto(master, master.Calendar, occStart, occEnd));
            }
        }

        var canReadUni = !enforceScopes || HasScope(principal, "university:read");
        if (canReadUni && rawResults.Count > 0)
        {
            var eventIds = rawResults.Select(r => r.Id).Distinct().ToList();
            var uniLinks = await db.UniversityActivityItems
                .AsNoTracking()
                .Where(i => i.UserId == userId.Value && i.CalendarEventId != null && eventIds.Contains(i.CalendarEventId.Value))
                .Select(i => new { EventId = i.CalendarEventId!.Value, i.Id, i.CourseId })
                .ToListAsync(ct);
            var linkMap = uniLinks.ToDictionary(x => x.EventId);
            rawResults = rawResults
                .Select(r => linkMap.TryGetValue(r.Id, out var link)
                    ? r with { UniversityActivityItemId = link.Id }
                    : r)
                .ToList();
        }

        var ordered = rawResults
            .OrderBy(e => e.StartAt)
            .ThenBy(e => e.EndAt)
            .ThenBy(e => e.Title, StringComparer.Ordinal)
            .ToList();

        return KnowledgeResult.Ok<KnowledgeEventsResponse>(new KnowledgeEventsResponse(
            resolvedFrom,
            resolvedTo,
            clamped,
            ordered));
    }

    public async Task<KnowledgeResult<IReadOnlyList<KnowledgeCourseSummaryDto>>> ListCoursesAsync(
        ClaimsPrincipal principal,
        bool enforceAgentPrivacy,
        string? semester,
        string? status,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return KnowledgeResult.Unauthorized<IReadOnlyList<KnowledgeCourseSummaryDto>>();

        var enforceScopes = ShouldEnforceAgentRules(principal, enforceAgentPrivacy);
        if (enforceScopes && !HasScope(principal, "university:read"))
        {
            return KnowledgeResult.Forbidden<IReadOnlyList<KnowledgeCourseSummaryDto>>("Missing required scope: university:read");
        }

        var courses = await db.UniversityCourses
            .AsNoTracking()
            .Where(c => c.UserId == userId.Value)
            .Include(c => c.Activities)
                .ThenInclude(a => a.Items)
            .ToListAsync(ct);

        IEnumerable<UniversityCourse> filtered = courses;
        if (!string.IsNullOrWhiteSpace(semester))
        {
            var trimmed = semester.Trim();
            if (!string.Equals(trimmed, "all", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(c => string.Equals(c.Semester, trimmed, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var trimmed = status.Trim();
            if (!string.Equals(trimmed, "all", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(c => string.Equals(c.Status, trimmed, StringComparison.OrdinalIgnoreCase));
            }
        }

        var canReadNotes = !enforceScopes || HasScope(principal, "notes:read");
        var uniNotes = await db.Notes
            .AsNoTracking()
            .Where(n => n.UserId == userId.Value && n.DeletedAt == null && n.UniversityCourseId != null)
            .Select(n => new { n.UniversityCourseId, n.UniversityActivityItemId, n.AllowAgentRead })
            .ToListAsync(ct);

        var hiddenItemIds = uniNotes
            .Where(n => n.UniversityActivityItemId.HasValue && !n.AllowAgentRead)
            .Select(n => n.UniversityActivityItemId!.Value)
            .ToHashSet();

        var courseNoteCounts = canReadNotes
            ? uniNotes
                .Where(n => !enforceScopes || n.AllowAgentRead)
                .GroupBy(n => n.UniversityCourseId!.Value)
                .ToDictionary(g => g.Key, g => g.Count())
            : new Dictionary<Guid, int>();

        var uniArtifacts = await db.Attachments
            .AsNoTracking()
            .Where(a => a.UserId == userId.Value
                && a.DeletedAt == null
                && a.Kind == AttachmentKinds.Artifact
                && a.UniversityCourseId != null)
            .Select(a => new { a.UniversityCourseId, a.UniversityActivityItemId, a.AllowAgentRead })
            .ToListAsync(ct);

        var courseArtifactCounts = uniArtifacts
            .Where(a => !enforceScopes || (a.AllowAgentRead && (!a.UniversityActivityItemId.HasValue || !hiddenItemIds.Contains(a.UniversityActivityItemId.Value))))
            .GroupBy(a => a.UniversityCourseId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var summaries = filtered
            .OrderBy(c => c.Status == "active" ? 0 : (c.Status == "completed" ? 1 : 2))
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.CreatedAt)
            .Select(c => new KnowledgeCourseSummaryDto(
                c.Id,
                c.Name,
                c.Code,
                c.Semester,
                c.Instructor,
                c.Credits,
                c.Status,
                courseNoteCounts.GetValueOrDefault(c.Id, 0),
                courseArtifactCounts.GetValueOrDefault(c.Id, 0),
                c.Activities
                    .OrderBy(a => a.SortOrder)
                    .ThenBy(a => a.CreatedAt)
                    .Select(a => new KnowledgeCourseActivitySummaryDto(
                        a.Id,
                        a.ActivityType,
                        string.IsNullOrWhiteSpace(a.Title)
                            ? UniversityConstants.GetDefaultActivityLabel(a.ActivityType)
                            : a.Title.Trim(),
                        a.Items.Count,
                        UniversityAttendanceCalculator.Calculate(a)))
                    .ToList()))
            .ToList();

        return KnowledgeResult.Ok<IReadOnlyList<KnowledgeCourseSummaryDto>>(summaries);
    }

    public async Task<KnowledgeResult<KnowledgeCourseDetailDto>> GetCourseAsync(
        ClaimsPrincipal principal,
        bool enforceAgentPrivacy,
        Guid courseId,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return KnowledgeResult.Unauthorized<KnowledgeCourseDetailDto>();

        var enforceScopes = ShouldEnforceAgentRules(principal, enforceAgentPrivacy);
        if (enforceScopes && !HasScope(principal, "university:read"))
        {
            return KnowledgeResult.Forbidden<KnowledgeCourseDetailDto>("Missing required scope: university:read");
        }

        var course = await db.UniversityCourses
            .AsNoTracking()
            .Where(c => c.UserId == userId.Value && c.Id == courseId)
            .Include(c => c.Activities)
                .ThenInclude(a => a.Items)
                    .ThenInclude(i => i.CalendarEvent)
            .FirstOrDefaultAsync(ct);

        if (course is null)
        {
            return KnowledgeResult.NotFound<KnowledgeCourseDetailDto>("University course not found.");
        }

        var canReadNotes = !enforceScopes || HasScope(principal, "notes:read");
        var courseNotesRaw = await db.Notes
            .AsNoTracking()
            .Include(n => n.Tags)
            .Where(n => n.UserId == userId.Value && n.DeletedAt == null && n.UniversityCourseId == courseId)
            .ToListAsync(ct);

        var hiddenItemIds = courseNotesRaw
            .Where(n => n.UniversityActivityItemId.HasValue && !n.AllowAgentRead)
            .Select(n => n.UniversityActivityItemId!.Value)
            .ToHashSet();

        var itemNoteIds = new Dictionary<Guid, Guid>();
        if (canReadNotes)
        {
            foreach (var n in courseNotesRaw.Where(n => n.UniversityActivityItemId.HasValue && (!enforceScopes || n.AllowAgentRead)))
            {
                itemNoteIds[n.UniversityActivityItemId!.Value] = n.Id;
            }
        }

        var courseArtifactsRaw = await db.Attachments
            .AsNoTracking()
            .Where(a => a.UserId == userId.Value
                && a.DeletedAt == null
                && a.Kind == AttachmentKinds.Artifact
                && a.UniversityCourseId == courseId)
            .ToListAsync(ct);

        var visibleCourseArtifacts = courseArtifactsRaw
            .Where(a => !a.UniversityActivityItemId.HasValue && (!enforceScopes || a.AllowAgentRead))
            .OrderBy(a => a.CreatedAt)
            .Select(AttachmentEndpoints.ToDto)
            .ToList();

        var itemArtifactCounts = courseArtifactsRaw
            .Where(a => a.UniversityActivityItemId.HasValue
                && (!enforceScopes || (a.AllowAgentRead && !hiddenItemIds.Contains(a.UniversityActivityItemId.Value))))
            .GroupBy(a => a.UniversityActivityItemId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var courseLabel = BuildCourseLinkLabel(course);
        var visibleCourseNotes = canReadNotes
            ? courseNotesRaw
                .Where(n => !n.UniversityActivityItemId.HasValue && (!enforceScopes || n.AllowAgentRead))
                .OrderByDescending(n => n.UpdatedAt)
                .Select(n => ToNoteSummaryDto(n, n.AllowAgentRead, null, courseLabel))
                .ToList()
            : [];

        var activityDtos = course.Activities
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.CreatedAt)
            .Select(a =>
            {
                var itemDtos = a.Items
                    .OrderBy(i => i.CalendarEvent is null ? 1 : 0)
                    .ThenBy(i => i.CalendarEvent?.StartAt ?? DateTimeOffset.MaxValue)
                    .ThenBy(i => i.SequenceNumber ?? int.MaxValue)
                    .ThenBy(i => i.CreatedAt)
                    .Select(i => new UniversityActivityItemDto(
                        i.Id,
                        i.ActivityId,
                        i.CourseId,
                        i.CalendarEventId,
                        i.Title,
                        i.Theme,
                        i.Notes,
                        i.SequenceNumber,
                        i.AttendanceStatus,
                        i.CalendarEvent?.StartAt.ToUniversalTime(),
                        i.CalendarEvent?.EndAt.ToUniversalTime(),
                        i.CalendarEvent?.IsAllDay ?? false,
                        i.CalendarEvent?.Location,
                        i.MetadataJson,
                        i.CreatedAt,
                        i.UpdatedAt,
                        itemNoteIds.TryGetValue(i.Id, out var noteId) ? noteId : null,
                        itemArtifactCounts.GetValueOrDefault(i.Id, 0)))
                    .ToList();

                return new UniversityCourseActivityDto(
                    a.Id,
                    a.CourseId,
                    a.ActivityType,
                    a.Title,
                    a.Description,
                    a.DefaultLocation,
                    a.MinAttendanceCount,
                    a.MinAttendancePercent,
                    a.PlannedTotalCount,
                    a.SortOrder,
                    a.MetadataJson,
                    UniversityAttendanceCalculator.Calculate(a),
                    itemDtos,
                    a.CreatedAt,
                    a.UpdatedAt);
            })
            .ToList();

        var courseDto = new UniversityCourseDto(
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
            activityDtos,
            course.CreatedAt,
            course.UpdatedAt,
            visibleCourseNotes.Count,
            visibleCourseArtifacts.Count);

        return KnowledgeResult.Ok<KnowledgeCourseDetailDto>(new KnowledgeCourseDetailDto(
            courseDto,
            visibleCourseNotes,
            visibleCourseArtifacts));
    }

    public async Task<KnowledgeResult<NotesTreeResponse>> ListNotesTreeAsync(
        ClaimsPrincipal principal,
        bool enforceAgentPrivacy,
        bool includeUniversity,
        bool includeEvents,
        bool includeDaily,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return KnowledgeResult.Unauthorized<NotesTreeResponse>();

        var enforceScopes = ShouldEnforceAgentRules(principal, enforceAgentPrivacy);
        if (enforceScopes && !HasScope(principal, "notes:read"))
        {
            return KnowledgeResult.Forbidden<NotesTreeResponse>("Missing required scope: notes:read");
        }

        var activeFolders = await db.NoteFolders
            .AsNoTracking()
            .Where(f => f.UserId == userId.Value && f.DeletedAt == null)
            .ToListAsync(ct);

        var effectiveFolderAccess = ComputeEffectiveFolderAccess(activeFolders.ToDictionary(f => f.Id));

        var activeNotes = await db.Notes
            .AsNoTracking()
            .Include(n => n.Tags)
            .Where(n => n.UserId == userId.Value && n.DeletedAt == null)
            .ToListAsync(ct);

        var visibleFolders = activeFolders
            .Where(f => !enforceScopes || effectiveFolderAccess.GetValueOrDefault(f.Id, true))
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(f => new NoteFolderDto(
                f.Id,
                f.ParentFolderId,
                f.Name,
                f.AllowAgentRead,
                effectiveFolderAccess.GetValueOrDefault(f.Id, true),
                f.SortOrder,
                f.CreatedAt,
                f.UpdatedAt))
            .ToList();

        var canReadUni = !enforceScopes || HasScope(principal, "university:read");
        var canReadCal = !enforceScopes || HasScope(principal, "calendar:read");

        var generalNotes = activeNotes
            .Where(IsGeneralNote)
            .Select(n => (Note: n, EffectiveAgent: IsNoteAgentReadable(n, effectiveFolderAccess)))
            .Where(x => !enforceScopes || x.EffectiveAgent)
            .OrderByDescending(x => x.Note.UpdatedAt)
            .Select(x => ToNoteSummaryDto(x.Note, x.EffectiveAgent))
            .ToList();

        var allVisibleForTags = activeNotes
            .Where(n =>
                IsGeneralNote(n)
                || (includeDaily && IsDailyNote(n))
                || (includeUniversity && IsUniversityNote(n) && canReadUni)
                || (includeEvents && IsEventNote(n) && canReadCal))
            .Select(n => (Note: n, EffectiveAgent: IsNoteAgentReadable(n, effectiveFolderAccess)))
            .Where(x => !enforceScopes || x.EffectiveAgent)
            .ToList();

        var tagCounts = allVisibleForTags
            .SelectMany(x => x.Note.Tags.Select(t => t.Tag))
            .GroupBy(t => t, StringComparer.Ordinal)
            .Select(g => new NoteTagCountDto(g.Key, g.Count()))
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Tag, StringComparer.Ordinal)
            .ToList();

        IReadOnlyList<NoteSummaryDto> dailyNotes = includeDaily
            ? activeNotes
                .Where(IsDailyNote)
                .Select(n => (Note: n, EffectiveAgent: IsNoteAgentReadable(n, effectiveFolderAccess)))
                .Where(x => !enforceScopes || x.EffectiveAgent)
                .OrderByDescending(x => x.Note.DailyDate)
                .Select(x => ToNoteSummaryDto(x.Note, x.EffectiveAgent, null, FormatDate(x.Note.DailyDate!.Value)))
                .ToList()
            : [];

        var universityTree = includeUniversity && canReadUni
            ? await BuildUniversityTreeAsync(userId.Value, enforceScopes, activeNotes, effectiveFolderAccess, ct)
            : [];

        var eventsTree = includeEvents && canReadCal
            ? await BuildEventsTreeAsync(userId.Value, enforceScopes, activeNotes, effectiveFolderAccess, ct)
            : [];

        return KnowledgeResult.Ok<NotesTreeResponse>(new NotesTreeResponse(
            visibleFolders,
            generalNotes,
            tagCounts,
            universityTree,
            eventsTree,
            dailyNotes));
    }

    public async Task<KnowledgeResult<KnowledgeNoteDetailDto>> ReadNoteAsync(
        ClaimsPrincipal principal,
        bool enforceAgentPrivacy,
        Guid noteId,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return KnowledgeResult.Unauthorized<KnowledgeNoteDetailDto>();

        var enforceScopes = ShouldEnforceAgentRules(principal, enforceAgentPrivacy);
        if (enforceScopes && !HasScope(principal, "notes:read"))
        {
            return KnowledgeResult.Forbidden<KnowledgeNoteDetailDto>("Missing required scope: notes:read");
        }

        var note = await db.Notes
            .AsNoTracking()
            .Include(n => n.Tags)
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId.Value && n.DeletedAt == null, ct);
        if (note is null)
        {
            return KnowledgeResult.NotFound<KnowledgeNoteDetailDto>("Note not found.");
        }

        var activeFolders = await db.NoteFolders
            .AsNoTracking()
            .Where(f => f.UserId == userId.Value && f.DeletedAt == null)
            .ToListAsync(ct);
        var effectiveFolderAccess = ComputeEffectiveFolderAccess(activeFolders.ToDictionary(f => f.Id));
        var effectiveAgent = IsNoteAgentReadable(note, effectiveFolderAccess);

        if (enforceScopes && !effectiveAgent)
        {
            return KnowledgeResult.NotFound<KnowledgeNoteDetailDto>("Note not found.");
        }

        if (enforceScopes && IsUniversityNote(note) && !HasScope(principal, "university:read"))
        {
            return KnowledgeResult.Forbidden<KnowledgeNoteDetailDto>("Missing required scope: university:read");
        }

        if (enforceScopes && IsEventNote(note) && !HasScope(principal, "calendar:read"))
        {
            return KnowledgeResult.Forbidden<KnowledgeNoteDetailDto>("Missing required scope: calendar:read");
        }

        var labels = await BuildLinkLabelsAsync(userId.Value, [note], ct);
        var noteDetail = new NoteDetailDto(
            note.Id,
            note.FolderId,
            note.Title,
            note.Content,
            note.Tags.Select(t => t.Tag).OrderBy(t => t, StringComparer.Ordinal).ToList(),
            note.ContentHash,
            note.Version,
            note.AllowAgentRead,
            effectiveAgent,
            note.CreatedAt,
            note.UpdatedAt,
            note.UniversityCourseId,
            note.UniversityActivityItemId,
            note.CalendarEventId,
            note.CalendarOccurrenceStartAt,
            GetLinkKind(note),
            labels.GetValueOrDefault(note.Id),
            note.DailyDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var artifactsRaw = await db.Attachments
            .AsNoTracking()
            .Where(a => a.UserId == userId.Value
                && a.DeletedAt == null
                && a.Kind == AttachmentKinds.Artifact
                && a.NoteId == note.Id)
            .ToListAsync(ct);

        var artifacts = artifactsRaw
            .Where(a => !enforceScopes || a.AllowAgentRead)
            .OrderBy(a => a.CreatedAt)
            .Select(AttachmentEndpoints.ToDto)
            .ToList();

        return KnowledgeResult.Ok<KnowledgeNoteDetailDto>(new KnowledgeNoteDetailDto(noteDetail, artifacts));
    }

    public async Task<KnowledgeResult<IReadOnlyList<AttachmentDto>>> ListArtifactsAsync(
        ClaimsPrincipal principal,
        bool enforceAgentPrivacy,
        Guid? noteId,
        Guid? universityCourseId,
        Guid? universityActivityItemId,
        Guid? calendarEventId,
        DateTimeOffset? occurrenceStartAt,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return KnowledgeResult.Unauthorized<IReadOnlyList<AttachmentDto>>();

        var ownerCount =
            (noteId.HasValue ? 1 : 0) +
            (universityCourseId.HasValue ? 1 : 0) +
            (universityActivityItemId.HasValue ? 1 : 0) +
            (calendarEventId.HasValue ? 1 : 0);

        if (ownerCount != 1)
        {
            return KnowledgeResult.BadRequest<IReadOnlyList<AttachmentDto>>(
                "Specify exactly one owner: noteId, universityCourseId, universityActivityItemId, or calendarEventId.");
        }

        var enforceScopes = ShouldEnforceAgentRules(principal, enforceAgentPrivacy);
        if (enforceScopes)
        {
            var requiredScope = noteId.HasValue
                ? "notes:read"
                : calendarEventId.HasValue
                    ? "calendar:read"
                    : "university:read";
            if (!HasScope(principal, requiredScope))
            {
                return KnowledgeResult.Forbidden<IReadOnlyList<AttachmentDto>>($"Missing required scope: {requiredScope}");
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

        if (enforceScopes)
        {
            var privacyContext = await LoadPrivacyContextAsync(userId.Value, ct);
            items = items
                .Where(a => IsAttachmentAgentReadable(a, privacyContext))
                .ToList();
        }

        var ordered = items
            .OrderBy(a => a.CreatedAt)
            .Select(AttachmentEndpoints.ToDto)
            .ToList();

        return KnowledgeResult.Ok<IReadOnlyList<AttachmentDto>>(ordered);
    }

    public async Task<KnowledgeResult<ArtifactTextResponse>> ReadArtifactTextAsync(
        ClaimsPrincipal principal,
        bool enforceAgentPrivacy,
        Guid attachmentId,
        int startChunk,
        int maxChunks,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return KnowledgeResult.Unauthorized<ArtifactTextResponse>();

        var attachment = await db.Attachments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.Id == attachmentId
                    && a.UserId == userId.Value
                    && a.DeletedAt == null
                    && a.Kind == AttachmentKinds.Artifact,
                ct);

        if (attachment is null)
        {
            return KnowledgeResult.NotFound<ArtifactTextResponse>("Artifact not found.");
        }

        var enforceScopes = ShouldEnforceAgentRules(principal, enforceAgentPrivacy);
        if (enforceScopes)
        {
            var privacyContext = await LoadPrivacyContextAsync(userId.Value, ct);
            if (!IsAttachmentAgentReadable(attachment, privacyContext))
            {
                return KnowledgeResult.NotFound<ArtifactTextResponse>("Artifact not found.");
            }

            var requiredScope = GetRequiredAgentScope(attachment);
            if (!HasScope(principal, requiredScope))
            {
                return KnowledgeResult.Forbidden<ArtifactTextResponse>($"Missing required scope: {requiredScope}");
            }
        }

        if (string.Equals(attachment.TextExtractionStatus, AttachmentExtractionStatuses.Pending, StringComparison.Ordinal))
        {
            await textService.EnsureExtractedAsync(attachment.Id, userId.Value, ct);
            attachment = await db.Attachments
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == attachmentId && a.UserId == userId.Value && a.DeletedAt == null, ct);
            if (attachment is null)
            {
                return KnowledgeResult.NotFound<ArtifactTextResponse>("Artifact not found.");
            }
        }

        var clampedStart = Math.Max(0, startChunk);
        var clampedMax = Math.Clamp(maxChunks <= 0 ? DefaultArtifactMaxChunks : maxChunks, 1, MaxArtifactChunksPerPage);

        var totalChunks = await db.AttachmentTextChunks
            .AsNoTracking()
            .CountAsync(c => c.AttachmentId == attachment.Id && c.UserId == userId.Value, ct);

        var chunks = await db.AttachmentTextChunks
            .AsNoTracking()
            .Where(c => c.AttachmentId == attachment.Id && c.UserId == userId.Value && c.ChunkIndex >= clampedStart)
            .OrderBy(c => c.ChunkIndex)
            .Take(clampedMax)
            .Select(c => new ArtifactTextChunkDto(c.ChunkIndex, c.PageLabel, c.Content))
            .ToListAsync(ct);

        var hasMore = clampedStart + chunks.Count < totalChunks;

        return KnowledgeResult.Ok<ArtifactTextResponse>(new ArtifactTextResponse(
            attachment.Id,
            attachment.FileName,
            attachment.ContentType,
            attachment.TextExtractionStatus,
            attachment.ExtractedPageCount,
            totalChunks,
            clampedStart,
            hasMore,
            chunks));
    }

    public async Task<KnowledgeResult<KnowledgeSearchResponse>> SearchKnowledgeAsync(
        ClaimsPrincipal principal,
        bool enforceAgentPrivacy,
        string? query,
        string? scope,
        Guid? courseId,
        int limit,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return KnowledgeResult.Unauthorized<KnowledgeSearchResponse>();

        var trimmedQuery = (query ?? string.Empty).Trim();
        if (trimmedQuery.Length == 0)
        {
            return KnowledgeResult.BadRequest<KnowledgeSearchResponse>("Search query is required.");
        }

        if (trimmedQuery.Length > MaxSearchQueryLength)
        {
            trimmedQuery = trimmedQuery[..MaxSearchQueryLength];
        }

        var normalizedScope = string.IsNullOrWhiteSpace(scope)
            ? "all"
            : scope.Trim().ToLowerInvariant();
        if (normalizedScope is not ("all" or "notes" or "artifacts"))
        {
            return KnowledgeResult.BadRequest<KnowledgeSearchResponse>("scope must be 'all', 'notes', or 'artifacts'.");
        }

        var clampedLimit = Math.Clamp(limit <= 0 ? DefaultSearchLimit : limit, 1, MaxSearchLimit);

        var enforceScopes = ShouldEnforceAgentRules(principal, enforceAgentPrivacy);
        var canReadNotes = !enforceScopes || HasScope(principal, "notes:read");
        var canReadUni = !enforceScopes || HasScope(principal, "university:read");
        var canReadCal = !enforceScopes || HasScope(principal, "calendar:read");

        if (enforceScopes && !canReadNotes && !canReadUni && !canReadCal)
        {
            return KnowledgeResult.Forbidden<KnowledgeSearchResponse>(
                "Missing required scope: at least one of notes:read, university:read, or calendar:read is required.");
        }

        var privacyContext = enforceScopes
            ? await LoadPrivacyContextAsync(userId.Value, ct)
            : null;

        var queryTokens = TokenizeQuery(trimmedQuery);
        var scoredHits = new List<(double Score, DateTimeOffset Recency, KnowledgeSearchHitDto Hit)>();

        if (normalizedScope is "all" or "notes" && canReadNotes)
        {
            var noteQuery = db.Notes
                .AsNoTracking()
                .Where(n => n.UserId == userId.Value && n.DeletedAt == null);

            if (courseId.HasValue)
            {
                noteQuery = noteQuery.Where(n => n.UniversityCourseId == courseId.Value);
            }

            if (privacyContext is not null)
            {
                var allowedFolderIds = privacyContext.EffectiveFolderAccess
                    .Where(kvp => kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToList();
                noteQuery = noteQuery.Where(n =>
                    n.AllowAgentRead
                    && (n.FolderId == null || allowedFolderIds.Contains(n.FolderId.Value)));
            }

            if (!canReadUni)
            {
                noteQuery = noteQuery.Where(n => n.UniversityCourseId == null && n.UniversityActivityItemId == null);
            }

            if (!canReadCal)
            {
                noteQuery = noteQuery.Where(n => n.CalendarEventId == null);
            }

            List<Note> candidateNotes;
            if (IsPostgres)
            {
                var noteFetchLimit = Math.Max(clampedLimit * 4, 50);
                candidateNotes = await noteQuery
                    .Where(n => EF.Functions.ToTsVector("english", n.Title + " " + n.Content)
                        .Matches(EF.Functions.WebSearchToTsQuery("english", trimmedQuery)))
                    .OrderByDescending(n => EF.Functions.ToTsVector("english", n.Title + " " + n.Content)
                        .Rank(EF.Functions.WebSearchToTsQuery("english", trimmedQuery)))
                    .ThenByDescending(n => n.UpdatedAt)
                    .Take(noteFetchLimit)
                    .ToListAsync(ct);
            }
            else
            {
                var allNotes = await noteQuery.ToListAsync(ct);
                candidateNotes = allNotes
                    .Where(n => MatchesFallbackQuery(n.Title, n.Content, trimmedQuery, queryTokens))
                    .ToList();
            }

            var allowedNotes = candidateNotes
                .Where(n =>
                    IsGeneralNote(n)
                    || IsDailyNote(n)
                    || (IsUniversityNote(n) && canReadUni)
                    || (IsEventNote(n) && canReadCal))
                .Where(n => privacyContext is null || IsNoteAgentReadable(n, privacyContext.EffectiveFolderAccess))
                .ToList();

            var labels = await BuildLinkLabelsAsync(userId.Value, allowedNotes, ct);
            foreach (var note in allowedNotes)
            {
                var parsed = NoteFrontmatter.Parse(note.Content);
                var score = ScoreTextMatch(note.Title, parsed.Body, trimmedQuery, queryTokens);
                var snippet = BuildCenteredSnippet(parsed.Body, trimmedQuery, queryTokens);
                scoredHits.Add((
                    score,
                    note.UpdatedAt,
                    new KnowledgeSearchHitDto(
                        "note",
                        note.Id,
                        note.Title,
                        labels.GetValueOrDefault(note.Id),
                        null,
                        null,
                        snippet)));
            }
        }

        if (normalizedScope is "all" or "artifacts")
        {
            var chunkQuery = db.AttachmentTextChunks
                .AsNoTracking()
                .Include(c => c.Attachment)
                .Where(c => c.UserId == userId.Value
                    && c.Attachment.DeletedAt == null
                    && c.Attachment.Kind == AttachmentKinds.Artifact);

            if (courseId.HasValue)
            {
                chunkQuery = chunkQuery.Where(c => c.Attachment.UniversityCourseId == courseId.Value);
            }

            if (privacyContext is not null)
            {
                chunkQuery = chunkQuery.Where(c => c.Attachment.AllowAgentRead);

                if (privacyContext.HiddenUniItemIds.Count > 0)
                {
                    var hiddenItems = privacyContext.HiddenUniItemIds.ToList();
                    chunkQuery = chunkQuery.Where(c =>
                        c.Attachment.UniversityActivityItemId == null
                        || !hiddenItems.Contains(c.Attachment.UniversityActivityItemId.Value));
                }

                var readableNoteIds = privacyContext.NotesById.Values
                    .Where(n => IsNoteAgentReadable(n, privacyContext.EffectiveFolderAccess))
                    .Select(n => n.Id)
                    .ToList();
                chunkQuery = chunkQuery.Where(c =>
                    c.Attachment.NoteId == null
                    || readableNoteIds.Contains(c.Attachment.NoteId.Value));
            }

            if (!canReadNotes)
            {
                chunkQuery = chunkQuery.Where(c => c.Attachment.NoteId == null);
            }

            if (!canReadCal)
            {
                chunkQuery = chunkQuery.Where(c => c.Attachment.CalendarEventId == null);
            }

            if (!canReadUni)
            {
                chunkQuery = chunkQuery.Where(c => c.Attachment.NoteId != null || c.Attachment.CalendarEventId != null);
            }

            List<AttachmentTextChunk> candidateChunks;
            if (IsPostgres)
            {
                var chunkFetchLimit = Math.Max(clampedLimit * 10, 100);
                candidateChunks = await chunkQuery
                    .Where(c => EF.Functions.ToTsVector("english", c.Content)
                        .Matches(EF.Functions.WebSearchToTsQuery("english", trimmedQuery)))
                    .OrderByDescending(c => EF.Functions.ToTsVector("english", c.Content)
                        .Rank(EF.Functions.WebSearchToTsQuery("english", trimmedQuery)))
                    .ThenBy(c => c.ChunkIndex)
                    .Take(chunkFetchLimit)
                    .ToListAsync(ct);
            }
            else
            {
                var allChunks = await chunkQuery.ToListAsync(ct);
                candidateChunks = allChunks
                    .Where(c => MatchesFallbackQuery(c.Attachment.FileName, c.Content, trimmedQuery, queryTokens))
                    .ToList();
            }

            var allowedChunks = candidateChunks
                .Where(c => HasArtifactScope(c.Attachment, canReadNotes, canReadUni, canReadCal))
                .Where(c => privacyContext is null || IsAttachmentAgentReadable(c.Attachment, privacyContext))
                .ToList();

            // Keep best-matching chunk per attachment (up to 2 chunks per attachment so one long PDF doesn't dominate)
            var attachmentLabels = await BuildAttachmentLinkLabelsAsync(
                userId.Value,
                allowedChunks.Select(c => c.Attachment).DistinctBy(a => a.Id).ToList(),
                ct);

            var perAttachmentCount = new Dictionary<Guid, int>();
            var rankedChunks = allowedChunks
                .Select(c => (
                    Chunk: c,
                    Score: ScoreTextMatch(c.Attachment.FileName, c.Content, trimmedQuery, queryTokens)))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Chunk.ChunkIndex);

            foreach (var (chunk, score) in rankedChunks)
            {
                var count = perAttachmentCount.GetValueOrDefault(chunk.AttachmentId, 0);
                if (count >= 2)
                {
                    continue;
                }

                perAttachmentCount[chunk.AttachmentId] = count + 1;
                var snippet = BuildCenteredSnippet(chunk.Content, trimmedQuery, queryTokens);
                scoredHits.Add((
                    score,
                    chunk.Attachment.CreatedAt,
                    new KnowledgeSearchHitDto(
                        "artifact",
                        chunk.AttachmentId,
                        chunk.Attachment.FileName,
                        attachmentLabels.GetValueOrDefault(chunk.AttachmentId),
                        chunk.PageLabel,
                        chunk.ChunkIndex,
                        snippet)));
            }
        }

        var topHits = scoredHits
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Recency)
            .Take(clampedLimit)
            .Select(x => x.Hit)
            .ToList();

        return KnowledgeResult.Ok<KnowledgeSearchResponse>(new KnowledgeSearchResponse(
            trimmedQuery,
            normalizedScope,
            topHits.Count,
            topHits));
    }

    // -------------------------------------------------------------------------
    // Privacy, Scope & Helper Methods
    // -------------------------------------------------------------------------

    private sealed record PrivacyContext(
        Dictionary<Guid, bool> EffectiveFolderAccess,
        Dictionary<Guid, Note> NotesById,
        HashSet<Guid> HiddenUniItemIds,
        HashSet<(Guid EventId, DateTimeOffset? OccurrenceStartAt)> HiddenEventTargets);

    private async Task<PrivacyContext> LoadPrivacyContextAsync(Guid userId, CancellationToken ct)
    {
        var folders = await db.NoteFolders
            .AsNoTracking()
            .Where(f => f.UserId == userId && f.DeletedAt == null)
            .ToListAsync(ct);
        var effectiveFolderAccess = ComputeEffectiveFolderAccess(folders.ToDictionary(f => f.Id));

        var notes = await db.Notes
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.DeletedAt == null)
            .ToListAsync(ct);

        var notesById = notes.ToDictionary(n => n.Id);
        var hiddenUniItemIds = notes
            .Where(n => n.UniversityActivityItemId.HasValue && !IsNoteAgentReadable(n, effectiveFolderAccess))
            .Select(n => n.UniversityActivityItemId!.Value)
            .ToHashSet();

        var hiddenEventTargets = notes
            .Where(n => n.CalendarEventId.HasValue && !IsNoteAgentReadable(n, effectiveFolderAccess))
            .Select(n => (n.CalendarEventId!.Value, n.CalendarOccurrenceStartAt?.ToUniversalTime()))
            .ToHashSet();

        return new PrivacyContext(effectiveFolderAccess, notesById, hiddenUniItemIds, hiddenEventTargets);
    }

    private static bool IsAttachmentAgentReadable(Attachment attachment, PrivacyContext ctx)
    {
        if (!attachment.AllowAgentRead)
        {
            return false;
        }

        if (attachment.NoteId.HasValue)
        {
            return ctx.NotesById.TryGetValue(attachment.NoteId.Value, out var note)
                && IsNoteAgentReadable(note, ctx.EffectiveFolderAccess);
        }

        if (attachment.UniversityActivityItemId.HasValue)
        {
            return !ctx.HiddenUniItemIds.Contains(attachment.UniversityActivityItemId.Value);
        }

        if (attachment.CalendarEventId.HasValue)
        {
            var key = (attachment.CalendarEventId.Value, attachment.CalendarOccurrenceStartAt?.ToUniversalTime());
            return !ctx.HiddenEventTargets.Contains(key);
        }

        return true;
    }

    private static bool ShouldEnforceAgentRules(ClaimsPrincipal principal, bool enforceAgentPrivacy) =>
        enforceAgentPrivacy
        || string.Equals(
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

    private static bool HasArtifactScope(Attachment attachment, bool canReadNotes, bool canReadUni, bool canReadCal)
    {
        if (attachment.NoteId.HasValue) return canReadNotes;
        if (attachment.CalendarEventId.HasValue) return canReadCal;
        return canReadUni;
    }

    private static Dictionary<Guid, bool> ComputeEffectiveFolderAccess(Dictionary<Guid, NoteFolder> folderMap)
    {
        var result = new Dictionary<Guid, bool>(folderMap.Count);
        foreach (var folder in folderMap.Values)
        {
            var effective = true;
            Guid? currentId = folder.Id;
            var guard = 0;
            while (currentId.HasValue && guard++ < 64)
            {
                if (!folderMap.TryGetValue(currentId.Value, out var current))
                {
                    effective = false;
                    break;
                }

                if (!current.AllowAgentRead)
                {
                    effective = false;
                    break;
                }

                currentId = current.ParentFolderId;
            }

            if (currentId.HasValue)
            {
                effective = false;
            }

            result[folder.Id] = effective;
        }

        return result;
    }

    private static bool IsNoteAgentReadable(Note note, Dictionary<Guid, bool> effectiveFolderAccess) =>
        note.AllowAgentRead
        && (!note.FolderId.HasValue || effectiveFolderAccess.GetValueOrDefault(note.FolderId.Value, false));

    private static bool IsGeneralNote(Note n) =>
        !n.UniversityCourseId.HasValue
        && !n.UniversityActivityItemId.HasValue
        && !n.CalendarEventId.HasValue
        && !n.DailyDate.HasValue;

    private static bool IsDailyNote(Note n) => n.DailyDate.HasValue;

    private static bool IsUniversityNote(Note n) =>
        n.UniversityCourseId.HasValue || n.UniversityActivityItemId.HasValue;

    private static bool IsEventNote(Note n) => n.CalendarEventId.HasValue;

    private static string GetLinkKind(Note n)
    {
        if (n.DailyDate.HasValue) return NoteLinkKinds.Daily;
        if (n.UniversityActivityItemId.HasValue) return NoteLinkKinds.UniversityItem;
        if (n.UniversityCourseId.HasValue) return NoteLinkKinds.UniversityCourse;
        if (n.CalendarEventId.HasValue) return NoteLinkKinds.CalendarEvent;
        return NoteLinkKinds.General;
    }

    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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

    private static NoteSummaryDto ToNoteSummaryDto(
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
            linkLabel,
            n.DailyDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private async Task<Dictionary<Guid, string>> BuildLinkLabelsAsync(
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

        var courses = courseIds.Count > 0
            ? await db.UniversityCourses.AsNoTracking().Where(c => c.UserId == userId && courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct)
            : new Dictionary<Guid, UniversityCourse>();

        var items = itemIds.Count > 0
            ? await db.UniversityActivityItems.AsNoTracking().Include(i => i.Course).Include(i => i.Activity).Where(i => i.UserId == userId && itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct)
            : new Dictionary<Guid, UniversityActivityItem>();

        var events = eventIds.Count > 0
            ? await db.CalendarEvents.AsNoTracking().Where(e => e.UserId == userId && eventIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct)
            : new Dictionary<Guid, CalendarEvent>();

        foreach (var note in notes)
        {
            if (note.DailyDate.HasValue)
            {
                labels[note.Id] = FormatDate(note.DailyDate.Value);
            }
            else if (note.UniversityActivityItemId.HasValue && items.TryGetValue(note.UniversityActivityItemId.Value, out var item))
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

    private async Task<Dictionary<Guid, string>> BuildAttachmentLinkLabelsAsync(
        Guid userId,
        IReadOnlyList<Attachment> attachments,
        CancellationToken ct)
    {
        var labels = new Dictionary<Guid, string>();
        var noteIds = attachments.Where(a => a.NoteId.HasValue).Select(a => a.NoteId!.Value).Distinct().ToList();
        var courseIds = attachments.Where(a => a.UniversityCourseId.HasValue && !a.UniversityActivityItemId.HasValue).Select(a => a.UniversityCourseId!.Value).Distinct().ToList();
        var itemIds = attachments.Where(a => a.UniversityActivityItemId.HasValue).Select(a => a.UniversityActivityItemId!.Value).Distinct().ToList();
        var eventIds = attachments.Where(a => a.CalendarEventId.HasValue).Select(a => a.CalendarEventId!.Value).Distinct().ToList();

        var notes = noteIds.Count > 0
            ? await db.Notes.AsNoTracking().Where(n => n.UserId == userId && noteIds.Contains(n.Id)).ToDictionaryAsync(n => n.Id, ct)
            : new Dictionary<Guid, Note>();
        var courses = courseIds.Count > 0
            ? await db.UniversityCourses.AsNoTracking().Where(c => c.UserId == userId && courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct)
            : new Dictionary<Guid, UniversityCourse>();
        var items = itemIds.Count > 0
            ? await db.UniversityActivityItems.AsNoTracking().Include(i => i.Course).Include(i => i.Activity).Where(i => i.UserId == userId && itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct)
            : new Dictionary<Guid, UniversityActivityItem>();
        var events = eventIds.Count > 0
            ? await db.CalendarEvents.AsNoTracking().Where(e => e.UserId == userId && eventIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct)
            : new Dictionary<Guid, CalendarEvent>();

        foreach (var att in attachments)
        {
            if (att.NoteId.HasValue && notes.TryGetValue(att.NoteId.Value, out var note))
            {
                labels[att.Id] = $"Note: {note.Title}";
            }
            else if (att.UniversityActivityItemId.HasValue && items.TryGetValue(att.UniversityActivityItemId.Value, out var item))
            {
                labels[att.Id] = BuildItemLinkLabel(item.Course, item.Activity, item);
            }
            else if (att.UniversityCourseId.HasValue && courses.TryGetValue(att.UniversityCourseId.Value, out var course))
            {
                labels[att.Id] = BuildCourseLinkLabel(course);
            }
            else if (att.CalendarEventId.HasValue && events.TryGetValue(att.CalendarEventId.Value, out var evt))
            {
                var isRec = !string.IsNullOrWhiteSpace(evt.RecurrenceRule) || evt.RecurrenceMasterId.HasValue;
                labels[att.Id] = BuildEventLinkLabel(evt.Title, att.CalendarOccurrenceStartAt, isRec);
            }
        }

        return labels;
    }

    private async Task<IReadOnlyList<UniversitySemesterNotesTreeDto>> BuildUniversityTreeAsync(
        Guid userId,
        bool enforcePrivacy,
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

        if (courses.Count == 0) return [];

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
            .Where(x => !enforcePrivacy || x.EffectiveAgent)
            .GroupBy(x => x.Note.UniversityCourseId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var itemNoteLookup = activeNotes
            .Where(n => n.UniversityActivityItemId.HasValue)
            .GroupBy(n => n.UniversityActivityItemId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(n => n.UpdatedAt).ToList());

        var courseArtifactsLookup = uniArtifacts
            .Where(a => !a.UniversityActivityItemId.HasValue && a.UniversityCourseId.HasValue && (!enforcePrivacy || a.AllowAgentRead))
            .GroupBy(a => a.UniversityCourseId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<AttachmentDto>)g.OrderBy(a => a.CreatedAt).Select(AttachmentEndpoints.ToDto).ToList());

        var itemArtifactsLookup = uniArtifacts
            .Where(a => a.UniversityActivityItemId.HasValue && (!enforcePrivacy || a.AllowAgentRead))
            .GroupBy(a => a.UniversityActivityItemId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.CreatedAt).ToList());

        return courses
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
                                .Select(x => ToNoteSummaryDto(x.Note, x.EffectiveAgent, null, courseLabel))
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
                                        var itemNotes = new List<NoteSummaryDto>();
                                        var itemHidden = false;
                                        if (itemNoteLookup.TryGetValue(i.Id, out var linkedNotes))
                                        {
                                            var label = BuildItemLinkLabel(c, a, i);
                                            foreach (var linkedNote in linkedNotes)
                                            {
                                                var eff = IsNoteAgentReadable(linkedNote, effectiveFolderAccess);
                                                if (!eff) itemHidden = true;
                                                if (!enforcePrivacy || eff)
                                                {
                                                    itemNotes.Add(ToNoteSummaryDto(linkedNote, eff, null, label));
                                                }
                                            }
                                        }

                                        IReadOnlyList<AttachmentDto> iArtifacts =
                                            enforcePrivacy && itemHidden
                                                ? []
                                                : (itemArtifactsLookup.TryGetValue(i.Id, out var rawArts)
                                                    ? rawArts.Select(AttachmentEndpoints.ToDto).ToList()
                                                    : []);

                                        return new UniversityItemNotesTreeDto(i.Id, i.Title, i.SequenceNumber, itemNotes.FirstOrDefault(), iArtifacts, itemNotes);
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
    }

    private async Task<IReadOnlyList<EventNotesGroupDto>> BuildEventsTreeAsync(
        Guid userId,
        bool enforcePrivacy,
        IReadOnlyList<Note> activeNotes,
        Dictionary<Guid, bool> effectiveFolderAccess,
        CancellationToken ct)
    {
        var eventNotes = activeNotes.Where(n => n.CalendarEventId.HasValue).ToList();
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
        if (eventIds.Count == 0) return [];

        var uniLinkedSet = (await db.UniversityActivityItems
            .AsNoTracking()
            .Where(i => i.UserId == userId && i.CalendarEventId != null && eventIds.Contains(i.CalendarEventId.Value))
            .Select(i => i.CalendarEventId!.Value)
            .ToListAsync(ct)).ToHashSet();

        var events = await db.CalendarEvents
            .AsNoTracking()
            .Include(e => e.Calendar)
            .Where(e => e.UserId == userId && eventIds.Contains(e.Id))
            .ToListAsync(ct);

        var validEvents = events
            .Where(e => !string.Equals(e.Calendar.Module, UniversityConstants.CalendarModuleKey, StringComparison.OrdinalIgnoreCase)
                && !uniLinkedSet.Contains(e.Id))
            .ToDictionary(e => e.Id);

        var groups = new List<EventNotesGroupDto>();
        foreach (var evt in validEvents.Values.OrderByDescending(e => e.StartAt))
        {
            var isRecurring = !string.IsNullOrWhiteSpace(evt.RecurrenceRule);
            var notesForEvent = eventNotes.Where(n => n.CalendarEventId == evt.Id).ToList();
            var artifactsForEvent = eventArtifacts.Where(a => a.CalendarEventId == evt.Id).ToList();

            var sharedNoteRaw = notesForEvent.FirstOrDefault(n => !n.CalendarOccurrenceStartAt.HasValue);
            var sharedHidden = sharedNoteRaw is not null && !IsNoteAgentReadable(sharedNoteRaw, effectiveFolderAccess);
            NoteSummaryDto? sharedNoteDto = null;
            if (sharedNoteRaw is not null)
            {
                var eff = IsNoteAgentReadable(sharedNoteRaw, effectiveFolderAccess);
                if (!enforcePrivacy || eff)
                {
                    sharedNoteDto = ToNoteSummaryDto(sharedNoteRaw, eff, null, BuildEventLinkLabel(evt.Title, null, isRecurring));
                }
            }

            var sharedArtifacts = (enforcePrivacy && sharedHidden)
                ? new List<AttachmentDto>()
                : artifactsForEvent
                    .Where(a => !a.CalendarOccurrenceStartAt.HasValue && (!enforcePrivacy || a.AllowAgentRead))
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
                var occHidden = occNoteRaw is not null && !IsNoteAgentReadable(occNoteRaw, effectiveFolderAccess);
                NoteSummaryDto? occNoteDto = null;
                if (occNoteRaw is not null)
                {
                    var eff = IsNoteAgentReadable(occNoteRaw, effectiveFolderAccess);
                    if (!enforcePrivacy || eff)
                    {
                        occNoteDto = ToNoteSummaryDto(occNoteRaw, eff, null, BuildEventLinkLabel(evt.Title, occStart, isRecurring));
                    }
                }

                var occArtifacts = (enforcePrivacy && occHidden)
                    ? new List<AttachmentDto>()
                    : artifactsForEvent
                        .Where(a => a.CalendarOccurrenceStartAt.HasValue
                            && a.CalendarOccurrenceStartAt.Value.ToUniversalTime() == occStart
                            && (!enforcePrivacy || a.AllowAgentRead))
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

    private static CalendarEventDto ToEventDto(CalendarEvent e, UserCalendar calendar) => new(
        e.Id,
        e.CalendarId,
        calendar.Name,
        calendar.Color,
        e.Title,
        e.Description,
        e.Location,
        e.StartAt.ToUniversalTime(),
        e.EndAt.ToUniversalTime(),
        e.IsAllDay,
        e.Timezone ?? calendar.Timezone,
        e.Status,
        e.ExternalUid,
        e.RecurrenceRule ?? e.RecurrenceMaster?.RecurrenceRule,
        e.Source,
        e.CreatedAt,
        e.UpdatedAt,
        e.RecurrenceMasterId,
        e.RecurrenceOriginalStartAt?.ToUniversalTime(),
        e.RecurrenceMasterId.HasValue || !string.IsNullOrWhiteSpace(e.RecurrenceRule),
        e.RecurrenceMasterId.HasValue);

    private static CalendarEventDto ToExpandedOccurrenceDto(
        CalendarEvent master,
        UserCalendar calendar,
        DateTimeOffset occStart,
        DateTimeOffset occEnd) => new(
            master.Id,
            master.CalendarId,
            calendar.Name,
            calendar.Color,
            master.Title,
            master.Description,
            master.Location,
            occStart.ToUniversalTime(),
            occEnd.ToUniversalTime(),
            master.IsAllDay,
            master.Timezone ?? calendar.Timezone,
            master.Status,
            master.ExternalUid,
            master.RecurrenceRule,
            master.Source,
            master.CreatedAt,
            master.UpdatedAt,
            master.Id,
            occStart.ToUniversalTime(),
            true,
            false);

    private static TimeZoneInfo ResolveTimeZone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone) || timezone.Equals("UTC", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static List<string> TokenizeQuery(string query) =>
        query
            .Split([' ', '\t', '\r', '\n', ',', ';', '"', '\''], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

    private static bool MatchesFallbackQuery(string title, string body, string fullQuery, List<string> tokens)
    {
        if (title.Contains(fullQuery, StringComparison.OrdinalIgnoreCase)
            || body.Contains(fullQuery, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (tokens.Count <= 1)
        {
            return false;
        }

        return tokens.All(t =>
            title.Contains(t, StringComparison.OrdinalIgnoreCase)
            || body.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    private static double ScoreTextMatch(string title, string body, string fullQuery, List<string> tokens)
    {
        var score = 0.0;
        if (title.Contains(fullQuery, StringComparison.OrdinalIgnoreCase)) score += 6.0;
        if (body.Contains(fullQuery, StringComparison.OrdinalIgnoreCase)) score += 4.0;

        foreach (var token in tokens)
        {
            if (title.Contains(token, StringComparison.OrdinalIgnoreCase)) score += 2.0;
            if (body.Contains(token, StringComparison.OrdinalIgnoreCase)) score += 1.0;
        }

        return score;
    }

    private static string BuildCenteredSnippet(string text, string fullQuery, List<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= SnippetMaxLength)
        {
            return normalized;
        }

        var matchIdx = normalized.IndexOf(fullQuery, StringComparison.OrdinalIgnoreCase);
        if (matchIdx < 0)
        {
            foreach (var token in tokens)
            {
                matchIdx = normalized.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                if (matchIdx >= 0) break;
            }
        }

        if (matchIdx < 0)
        {
            return normalized[..SnippetMaxLength].TrimEnd() + "…";
        }

        var start = Math.Max(0, matchIdx - 80);
        var length = Math.Min(SnippetMaxLength, normalized.Length - start);
        var slice = normalized.Substring(start, length).Trim();
        var prefix = start > 0 ? "…" : string.Empty;
        var suffix = start + length < normalized.Length ? "…" : string.Empty;
        return $"{prefix}{slice}{suffix}";
    }
}
