using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Atrium.Api.Auth;
using Atrium.Api.Calendar;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atrium.Api.Endpoints;

public static partial class CalendarEndpoints
{
    private const string DefaultCalendarName = "Personal";
    private const string DefaultCalendarColor = "#c25438";
    private const long MaxIcsUploadBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> ValidEventStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "confirmed",
        "tentative",
        "cancelled",
    };

    [GeneratedRegex(@"^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorRegex();

    public static void MapCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        var calendarsGroup = app.MapGroup("/api/calendars");
        var eventsGroup = app.MapGroup("/api/events");

        calendarsGroup.MapGet("/", async (ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var accessError = CheckCalendarReadAccess(principal, out var userId);
            if (accessError is not null) return accessError;

            var calendars = await EnsureUserCalendarsAsync(db, userId, ct);
            var ordered = calendars
                .OrderByDescending(c => c.IsDefault)
                .ThenBy(c => c.CreatedAt)
                .Select(ToCalendarDto)
                .ToList();

            return Results.Ok(ordered);
        }).RequireAuthorization(AuthConstants.HumanOrAgentPolicy);

        calendarsGroup.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var accessError = CheckCalendarReadAccess(principal, out var userId);
            if (accessError is not null) return accessError;

            var calendar = await db.Calendars
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);

            return calendar is null ? Results.NotFound() : Results.Ok(ToCalendarDto(calendar));
        }).RequireAuthorization(AuthConstants.HumanOrAgentPolicy);

        calendarsGroup.MapPost("/", async (
            CreateCalendarRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId.Value && u.IsActive, ct);
            if (user is null) return Results.Unauthorized();

            var name = (request.Name ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 80)
            {
                return Results.BadRequest(new { error = "Calendar name must be 1–80 characters." });
            }

            var description = (request.Description ?? string.Empty).Trim();
            if (description.Length > 280)
            {
                return Results.BadRequest(new { error = "Calendar description must be 280 characters or fewer." });
            }

            var color = string.IsNullOrWhiteSpace(request.Color) ? DefaultCalendarColor : request.Color.Trim();
            if (!HexColorRegex().IsMatch(color))
            {
                return Results.BadRequest(new { error = "Calendar color must be a 6-digit hex code like #c25438." });
            }

            var timezone = string.IsNullOrWhiteSpace(request.Timezone) ? user.Timezone : request.Timezone.Trim();
            if (timezone.Length > 64)
            {
                return Results.BadRequest(new { error = "Timezone must be 64 characters or fewer." });
            }

            var existingCalendars = await db.Calendars
                .Where(c => c.UserId == user.Id)
                .ToListAsync(ct);

            var shouldBeDefault = request.IsDefault || existingCalendars.Count == 0;
            var now = DateTimeOffset.UtcNow;

            if (shouldBeDefault)
            {
                foreach (var existing in existingCalendars)
                {
                    if (existing.IsDefault)
                    {
                        existing.IsDefault = false;
                        existing.UpdatedAt = now;
                    }
                }
            }

            var calendar = new UserCalendar
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Name = name,
                Description = description,
                Color = color.ToLowerInvariant(),
                Timezone = timezone,
                IsDefault = shouldBeDefault,
                IsVisible = request.IsVisible,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.Calendars.Add(calendar);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/calendars/{calendar.Id}", ToCalendarDto(calendar));
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        calendarsGroup.MapPut("/{id:guid}", async (
            Guid id,
            UpdateCalendarRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var calendar = await db.Calendars
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId.Value, ct);
            if (calendar is null) return Results.NotFound();

            var name = (request.Name ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 80)
            {
                return Results.BadRequest(new { error = "Calendar name must be 1–80 characters." });
            }

            var description = request.Description is null ? calendar.Description : request.Description.Trim();
            if (description.Length > 280)
            {
                return Results.BadRequest(new { error = "Calendar description must be 280 characters or fewer." });
            }

            var color = string.IsNullOrWhiteSpace(request.Color) ? calendar.Color : request.Color.Trim();
            if (!HexColorRegex().IsMatch(color))
            {
                return Results.BadRequest(new { error = "Calendar color must be a 6-digit hex code like #c25438." });
            }

            var timezone = string.IsNullOrWhiteSpace(request.Timezone) ? calendar.Timezone : request.Timezone.Trim();
            if (timezone.Length > 64)
            {
                return Results.BadRequest(new { error = "Timezone must be 64 characters or fewer." });
            }

            var now = DateTimeOffset.UtcNow;
            if (request.IsDefault == true && !calendar.IsDefault)
            {
                var others = await db.Calendars
                    .Where(c => c.UserId == userId.Value && c.Id != calendar.Id && c.IsDefault)
                    .ToListAsync(ct);
                foreach (var other in others)
                {
                    other.IsDefault = false;
                    other.UpdatedAt = now;
                }

                calendar.IsDefault = true;
            }

            calendar.Name = name;
            calendar.Description = description;
            calendar.Color = color.ToLowerInvariant();
            calendar.Timezone = timezone;
            if (request.IsVisible.HasValue)
            {
                calendar.IsVisible = request.IsVisible.Value;
            }

            calendar.UpdatedAt = now;

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToCalendarDto(calendar));
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        calendarsGroup.MapDelete("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var userCalendars = await db.Calendars
                .Where(c => c.UserId == userId.Value)
                .ToListAsync(ct);

            var target = userCalendars.FirstOrDefault(c => c.Id == id);
            if (target is null) return Results.NotFound();

            if (userCalendars.Count <= 1)
            {
                return Results.BadRequest(new { error = "You must keep at least one calendar." });
            }

            if (target.IsDefault)
            {
                var replacement = userCalendars
                    .Where(c => c.Id != target.Id)
                    .OrderBy(c => c.CreatedAt)
                    .First();
                replacement.IsDefault = true;
                replacement.UpdatedAt = DateTimeOffset.UtcNow;
            }

            db.Calendars.Remove(target);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        calendarsGroup.MapPost("/{id:guid}/import/ics", async (
            Guid id,
            HttpRequest httpRequest,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var calendar = await db.Calendars
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId.Value, ct);
            if (calendar is null) return Results.NotFound();

            var (icsText, _, _, _, readError) = await ReadIcsPayloadAsync(httpRequest, ct);
            if (readError is not null) return readError;

            ParsedIcsCalendar parsed;
            try
            {
                parsed = IcsParser.Parse(icsText!, calendar.Timezone);
            }
            catch (FormatException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var response = await UpsertParsedEventsAsync(db, calendar, parsed, ct);
            return Results.Ok(response);
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        calendarsGroup.MapPost("/import/ics", async (
            HttpRequest httpRequest,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId.Value && u.IsActive, ct);
            if (user is null) return Results.Unauthorized();

            var (icsText, fileName, explicitName, explicitColor, readError) = await ReadIcsPayloadAsync(httpRequest, ct);
            if (readError is not null) return readError;

            ParsedIcsCalendar parsed;
            try
            {
                parsed = IcsParser.Parse(icsText!, user.Timezone);
            }
            catch (FormatException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var candidateName = !string.IsNullOrWhiteSpace(explicitName)
                ? explicitName.Trim()
                : !string.IsNullOrWhiteSpace(parsed.CalendarName)
                    ? parsed.CalendarName.Trim()
                    : DeriveCalendarNameFromFilename(fileName);

            if (candidateName.Length > 80)
            {
                candidateName = candidateName[..80];
            }

            var color = !string.IsNullOrWhiteSpace(explicitColor) && HexColorRegex().IsMatch(explicitColor.Trim())
                ? explicitColor.Trim().ToLowerInvariant()
                : "#3b6ea5";

            var description = (parsed.CalendarDescription ?? string.Empty).Trim();
            if (description.Length > 280)
            {
                description = description[..280];
            }

            var timezone = !string.IsNullOrWhiteSpace(parsed.Timezone) && parsed.Timezone.Length <= 64
                ? parsed.Timezone
                : user.Timezone;

            // Ensure existing user has a default Personal calendar before creating an imported one
            var existingCalendars = await EnsureUserCalendarsAsync(db, user.Id, ct);
            var now = DateTimeOffset.UtcNow;

            var calendar = new UserCalendar
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Name = candidateName,
                Description = description,
                Color = color,
                Timezone = timezone,
                IsDefault = existingCalendars.Count == 0,
                IsVisible = true,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.Calendars.Add(calendar);
            await db.SaveChangesAsync(ct);

            var response = await UpsertParsedEventsAsync(db, calendar, parsed, ct);
            return Results.Created($"/api/calendars/{calendar.Id}", response);
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        calendarsGroup.MapGet("/{id:guid}/export/ics", async (
            Guid id,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var accessError = CheckCalendarReadAccess(principal, out var userId);
            if (accessError is not null) return accessError;

            var calendar = await db.Calendars
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);
            if (calendar is null) return Results.NotFound();

            var rows = await db.CalendarEvents
                .AsNoTracking()
                .Where(e => e.CalendarId == calendar.Id && e.UserId == userId)
                .OrderBy(e => e.StartAt)
                .ThenBy(e => e.EndAt)
                .ToListAsync(ct);

            var masterIds = rows
                .Where(e => e.RecurrenceMasterId is null && !string.IsNullOrWhiteSpace(e.RecurrenceRule))
                .Select(e => e.Id)
                .ToList();

            var exDatesByMaster = new Dictionary<Guid, IReadOnlyList<DateTimeOffset>>();
            if (masterIds.Count > 0)
            {
                var deletedOverrides = await db.CalendarEvents
                    .AsNoTracking()
                    .Where(e =>
                        e.UserId == userId
                        && e.RecurrenceMasterId != null
                        && masterIds.Contains(e.RecurrenceMasterId.Value)
                        && e.IsRecurrenceExceptionDeleted
                        && e.RecurrenceOriginalStartAt != null)
                    .Select(e => new { MasterId = e.RecurrenceMasterId!.Value, OriginalStart = e.RecurrenceOriginalStartAt!.Value })
                    .ToListAsync(ct);

                foreach (var group in deletedOverrides.GroupBy(x => x.MasterId))
                {
                    exDatesByMaster[group.Key] = group.Select(x => x.OriginalStart.ToUniversalTime()).Order().ToList();
                }
            }

            var masterUidById = rows
                .Where(e => e.RecurrenceMasterId is null)
                .ToDictionary(
                    e => e.Id,
                    e => !string.IsNullOrWhiteSpace(e.ExternalUid) ? e.ExternalUid! : $"{e.Id:D}@atrium.local");

            var exportableDtos = rows
                .Where(e => !e.IsRecurrenceExceptionDeleted)
                .Select(e =>
                {
                    var dto = ToEventDto(e, calendar);
                    if (e.RecurrenceMasterId.HasValue && masterUidById.TryGetValue(e.RecurrenceMasterId.Value, out var masterUid))
                    {
                        dto = dto with { ExternalUid = masterUid };
                    }

                    return dto;
                })
                .ToList();

            var icsContent = IcsParser.Serialize(
                calendar.Name,
                calendar.Description,
                calendar.Timezone,
                exportableDtos,
                exDatesByMaster);
            var bytes = Encoding.UTF8.GetBytes(icsContent);
            var safeFileName = SanitizeFileName(calendar.Name) + ".ics";
            return Results.File(bytes, "text/calendar; charset=utf-8", safeFileName);
        }).RequireAuthorization(AuthConstants.HumanOrAgentPolicy);

        eventsGroup.MapGet("/", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            Guid[]? calendarId,
            bool? visibleOnly,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var accessError = CheckCalendarReadAccess(principal, out var userId);
            if (accessError is not null) return accessError;

            if (from.HasValue && to.HasValue && to.Value < from.Value)
            {
                return Results.BadRequest(new { error = "'to' must be greater than or equal to 'from'." });
            }

            IQueryable<CalendarEvent> baseQuery = db.CalendarEvents
                .AsNoTracking()
                .Include(e => e.Calendar)
                .Where(e => e.UserId == userId);

            if (calendarId is { Length: > 0 })
            {
                baseQuery = baseQuery.Where(e => calendarId.Contains(e.CalendarId));
            }

            if (visibleOnly == true)
            {
                baseQuery = baseQuery.Where(e => e.Calendar.IsVisible);
            }

            // 1. Standalone events and active (non-deleted) single-occurrence overrides
            var nonMasterQuery = baseQuery
                .Include(e => e.RecurrenceMaster)
                .Where(e =>
                    !e.IsRecurrenceExceptionDeleted
                    && (e.RecurrenceMasterId != null || e.RecurrenceRule == null));

            if (from.HasValue)
            {
                var fromUtc = from.Value.ToUniversalTime();
                nonMasterQuery = nonMasterQuery.Where(e => e.EndAt > fromUtc || (e.StartAt == e.EndAt && e.StartAt >= fromUtc));
            }

            if (to.HasValue)
            {
                var toUtc = to.Value.ToUniversalTime();
                nonMasterQuery = nonMasterQuery.Where(e => e.StartAt < toUtc);
            }

            var nonMasters = await nonMasterQuery.ToListAsync(ct);

            // 2. Recurring master events eligible for expansion
            var mastersQuery = baseQuery.Where(e => e.RecurrenceMasterId == null && e.RecurrenceRule != null);
            if (to.HasValue)
            {
                var toUtc = to.Value.ToUniversalTime();
                mastersQuery = mastersQuery.Where(e => e.StartAt < toUtc);
            }

            var masters = await mastersQuery.ToListAsync(ct);

            var suppressedByMaster = new Dictionary<Guid, HashSet<DateTimeOffset>>();
            if (masters.Count > 0)
            {
                var masterIds = masters.Select(m => m.Id).ToList();
                var overrides = await db.CalendarEvents
                    .AsNoTracking()
                    .Where(e =>
                        e.UserId == userId
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

            var results = new List<CalendarEventDto>(nonMasters.Count + masters.Count);
            foreach (var item in nonMasters)
            {
                results.Add(ToEventDto(item, item.Calendar));
            }

            var nowUtc = DateTimeOffset.UtcNow;
            foreach (var master in masters)
            {
                var masterStartUtc = master.StartAt.ToUniversalTime();
                var expandFrom = from?.ToUniversalTime() ?? masterStartUtc;
                var defaultTo = (nowUtc > masterStartUtc ? nowUtc : masterStartUtc).AddYears(1);
                var expandTo = to?.ToUniversalTime() ?? defaultTo;

                if (expandTo <= expandFrom)
                {
                    continue;
                }

                suppressedByMaster.TryGetValue(master.Id, out var suppressedStarts);

                if (!RRuleExpander.TryParse(master.RecurrenceRule, out _, out _))
                {
                    var masterEndUtc = master.EndAt.ToUniversalTime();
                    var overlapsWindow = masterEndUtc > expandFrom
                        || (masterStartUtc == masterEndUtc && masterStartUtc >= expandFrom);
                    if (overlapsWindow && masterStartUtc < expandTo
                        && (suppressedStarts is null || !suppressedStarts.Contains(masterStartUtc)))
                    {
                        results.Add(ToEventDto(master, master.Calendar));
                    }

                    continue;
                }

                var expanded = RRuleExpander.ExpandOccurrences(
                    masterStartUtc,
                    master.EndAt.ToUniversalTime(),
                    master.IsAllDay,
                    master.Timezone ?? master.Calendar.Timezone,
                    master.RecurrenceRule!,
                    expandFrom,
                    expandTo);

                foreach (var (occStart, occEnd) in expanded)
                {
                    if (suppressedStarts is not null && suppressedStarts.Contains(occStart))
                    {
                        continue;
                    }

                    results.Add(ToExpandedOccurrenceDto(master, master.Calendar, occStart, occEnd));
                }
            }

            var ordered = results
                .OrderBy(e => e.StartAt)
                .ThenBy(e => e.EndAt)
                .ThenBy(e => e.Title, StringComparer.Ordinal)
                .ToList();

            return Results.Ok(ordered);
        }).RequireAuthorization(AuthConstants.HumanOrAgentPolicy);

        eventsGroup.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var accessError = CheckCalendarReadAccess(principal, out var userId);
            if (accessError is not null) return accessError;

            var evt = await db.CalendarEvents
                .AsNoTracking()
                .Include(e => e.Calendar)
                .Include(e => e.RecurrenceMaster)
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId && !e.IsRecurrenceExceptionDeleted, ct);

            return evt is null ? Results.NotFound() : Results.Ok(ToEventDto(evt, evt.Calendar));
        }).RequireAuthorization(AuthConstants.HumanOrAgentPolicy);

        eventsGroup.MapPost("/", async (
            CreateEventRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var validationError = ValidateEventPayload(
                request.Title,
                request.StartAt,
                request.EndAt,
                request.Location,
                request.Timezone,
                request.Status,
                request.RecurrenceRule,
                out var normalizedTitle,
                out var normalizedStatus,
                out var normalizedRRule,
                request.Source);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            UserCalendar? targetCalendar;
            if (request.CalendarId.HasValue)
            {
                targetCalendar = await db.Calendars
                    .FirstOrDefaultAsync(c => c.Id == request.CalendarId.Value && c.UserId == userId.Value, ct);
                if (targetCalendar is null)
                {
                    return Results.NotFound(new { error = "Calendar not found." });
                }
            }
            else
            {
                var calendars = await EnsureUserCalendarsAsync(db, userId.Value, ct);
                targetCalendar = calendars.OrderByDescending(c => c.IsDefault).ThenBy(c => c.CreatedAt).First();
            }

            var now = DateTimeOffset.UtcNow;
            var evt = new CalendarEvent
            {
                Id = Guid.NewGuid(),
                CalendarId = targetCalendar.Id,
                UserId = userId.Value,
                Title = normalizedTitle,
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                Location = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim(),
                StartAt = request.StartAt.ToUniversalTime(),
                EndAt = request.EndAt.ToUniversalTime(),
                IsAllDay = request.IsAllDay,
                Timezone = string.IsNullOrWhiteSpace(request.Timezone) ? targetCalendar.Timezone : request.Timezone.Trim(),
                Status = normalizedStatus,
                RecurrenceRule = normalizedRRule,
                Source = string.IsNullOrWhiteSpace(request.Source) ? "Manual" : request.Source.Trim(),
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.CalendarEvents.Add(evt);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/events/{evt.Id}", ToEventDto(evt, targetCalendar));
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        eventsGroup.MapPut("/{id:guid}", async (
            Guid id,
            UpdateEventRequest request,
            string? scope,
            DateTimeOffset? occurrenceStartAt,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var evt = await db.CalendarEvents
                .Include(e => e.RecurrenceMaster)
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId.Value, ct);
            if (evt is null) return Results.NotFound();

            var effectiveScope = NormalizeRecurrenceScope(request.Scope ?? scope);
            if (effectiveScope is null)
            {
                return Results.BadRequest(new { error = "Scope must be 'single' or 'series'." });
            }

            var effectiveOccurrenceStart = (request.OccurrenceStartAt ?? occurrenceStartAt)?.ToUniversalTime();
            var rruleToValidate = effectiveScope == "single" ? null : request.RecurrenceRule;

            var validationError = ValidateEventPayload(
                request.Title,
                request.StartAt,
                request.EndAt,
                request.Location,
                request.Timezone,
                request.Status,
                rruleToValidate,
                out var normalizedTitle,
                out var normalizedStatus,
                out var normalizedRRule);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            var targetCalendar = await db.Calendars
                .FirstOrDefaultAsync(c => c.Id == request.CalendarId && c.UserId == userId.Value, ct);
            if (targetCalendar is null)
            {
                return Results.NotFound(new { error = "Target calendar not found." });
            }

            var now = DateTimeOffset.UtcNow;
            var newStartUtc = request.StartAt.ToUniversalTime();
            var newEndUtc = request.EndAt.ToUniversalTime();

            // Case 1: Editing a single occurrence of a recurring series
            if (effectiveScope == "single" && (evt.RecurrenceMasterId is not null || !string.IsNullOrWhiteSpace(evt.RecurrenceRule)))
            {
                if (evt.RecurrenceMasterId is not null)
                {
                    evt.CalendarId = targetCalendar.Id;
                    evt.Title = normalizedTitle;
                    evt.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                    evt.Location = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim();
                    evt.StartAt = newStartUtc;
                    evt.EndAt = newEndUtc;
                    evt.IsAllDay = request.IsAllDay;
                    evt.Timezone = string.IsNullOrWhiteSpace(request.Timezone) ? targetCalendar.Timezone : request.Timezone.Trim();
                    evt.Status = normalizedStatus;
                    evt.IsRecurrenceExceptionDeleted = false;
                    evt.UpdatedAt = now;

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(ToEventDto(evt, targetCalendar));
                }

                if (!effectiveOccurrenceStart.HasValue)
                {
                    return Results.BadRequest(new { error = "occurrenceStartAt is required when editing a single occurrence of a repeating series." });
                }

                var existingOverride = await db.CalendarEvents
                    .Include(e => e.RecurrenceMaster)
                    .FirstOrDefaultAsync(
                        e => e.UserId == userId.Value
                             && e.RecurrenceMasterId == evt.Id
                             && e.RecurrenceOriginalStartAt == effectiveOccurrenceStart.Value,
                        ct);

                if (existingOverride is not null)
                {
                    existingOverride.CalendarId = targetCalendar.Id;
                    existingOverride.Title = normalizedTitle;
                    existingOverride.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                    existingOverride.Location = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim();
                    existingOverride.StartAt = newStartUtc;
                    existingOverride.EndAt = newEndUtc;
                    existingOverride.IsAllDay = request.IsAllDay;
                    existingOverride.Timezone = string.IsNullOrWhiteSpace(request.Timezone) ? targetCalendar.Timezone : request.Timezone.Trim();
                    existingOverride.Status = normalizedStatus;
                    existingOverride.IsRecurrenceExceptionDeleted = false;
                    existingOverride.UpdatedAt = now;

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(ToEventDto(existingOverride, targetCalendar));
                }

                var createdOverride = new CalendarEvent
                {
                    Id = Guid.NewGuid(),
                    CalendarId = targetCalendar.Id,
                    UserId = userId.Value,
                    Title = normalizedTitle,
                    Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                    Location = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim(),
                    StartAt = newStartUtc,
                    EndAt = newEndUtc,
                    IsAllDay = request.IsAllDay,
                    Timezone = string.IsNullOrWhiteSpace(request.Timezone) ? targetCalendar.Timezone : request.Timezone.Trim(),
                    Status = normalizedStatus,
                    RecurrenceMasterId = evt.Id,
                    RecurrenceMaster = evt,
                    RecurrenceOriginalStartAt = effectiveOccurrenceStart.Value,
                    IsRecurrenceExceptionDeleted = false,
                    Source = evt.Source ?? "Manual",
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                db.CalendarEvents.Add(createdOverride);
                await db.SaveChangesAsync(ct);
                return Results.Ok(ToEventDto(createdOverride, targetCalendar));
            }

            // Case 2: Editing a standalone event OR editing an entire recurring series
            var master = evt.RecurrenceMasterId.HasValue
                ? await db.CalendarEvents.FirstOrDefaultAsync(
                    e => e.Id == evt.RecurrenceMasterId.Value && e.UserId == userId.Value,
                    ct)
                : evt;
            if (master is null) return Results.NotFound();

            var isSeries = !string.IsNullOrWhiteSpace(master.RecurrenceRule);
            var shiftedOverrides = new List<(CalendarEvent Child, DateTimeOffset NewOriginalStart)>();
            if (isSeries && (effectiveOccurrenceStart.HasValue || evt.RecurrenceMasterId.HasValue))
            {
                // When editing the whole series from an already-rescheduled override row,
                // the editor starts from evt.StartAt rather than RecurrenceOriginalStartAt.
                var anchorStartUtc = evt.RecurrenceMasterId.HasValue
                    ? evt.StartAt.ToUniversalTime()
                    : effectiveOccurrenceStart!.Value;
                var startDelta = newStartUtc - anchorStartUtc;
                var newDuration = newEndUtc - newStartUtc;
                master.StartAt = master.StartAt.ToUniversalTime() + startDelta;
                master.EndAt = master.StartAt + newDuration;

                if (startDelta != TimeSpan.Zero)
                {
                    var childOverrides = await db.CalendarEvents
                        .Where(e => e.UserId == userId.Value && e.RecurrenceMasterId == master.Id && e.RecurrenceOriginalStartAt != null)
                        .ToListAsync(ct);
                    foreach (var child in childOverrides)
                    {
                        shiftedOverrides.Add((child, child.RecurrenceOriginalStartAt!.Value.ToUniversalTime() + startDelta));
                    }
                }
            }
            else
            {
                master.StartAt = newStartUtc;
                master.EndAt = newEndUtc;
            }

            master.CalendarId = targetCalendar.Id;
            master.Title = normalizedTitle;
            master.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            master.Location = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim();
            master.IsAllDay = request.IsAllDay;
            master.Timezone = string.IsNullOrWhiteSpace(request.Timezone) ? targetCalendar.Timezone : request.Timezone.Trim();
            master.Status = normalizedStatus;
            master.RecurrenceRule = normalizedRRule;
            master.UpdatedAt = now;

            if (normalizedRRule is null)
            {
                var orphanedOverrides = await db.CalendarEvents
                    .Where(e => e.UserId == userId.Value && e.RecurrenceMasterId == master.Id)
                    .ToListAsync(ct);
                if (orphanedOverrides.Count > 0)
                {
                    db.CalendarEvents.RemoveRange(orphanedOverrides);
                }

                await db.SaveChangesAsync(ct);
            }
            else if (shiftedOverrides.Count > 0)
            {
                // Two-phase update avoids row-by-row unique-index collisions on
                // (recurrence_master_id, recurrence_original_start_at) when shifting adjacent overrides.
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                foreach (var (child, _) in shiftedOverrides)
                {
                    child.RecurrenceOriginalStartAt = null;
                    child.UpdatedAt = now;
                }

                await db.SaveChangesAsync(ct);

                foreach (var (child, newOriginalStart) in shiftedOverrides)
                {
                    child.RecurrenceOriginalStartAt = newOriginalStart;
                    child.UpdatedAt = now;
                }

                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            else
            {
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(ToEventDto(master, targetCalendar));
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        eventsGroup.MapDelete("/{id:guid}", async (
            Guid id,
            string? scope,
            DateTimeOffset? occurrenceStartAt,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var evt = await db.CalendarEvents
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId.Value, ct);
            if (evt is null) return Results.NotFound();

            var effectiveScope = NormalizeRecurrenceScope(scope);
            if (effectiveScope is null)
            {
                return Results.BadRequest(new { error = "Scope must be 'single' or 'series'." });
            }

            var now = DateTimeOffset.UtcNow;

            if (effectiveScope == "single" && (evt.RecurrenceMasterId is not null || !string.IsNullOrWhiteSpace(evt.RecurrenceRule)))
            {
                if (evt.RecurrenceMasterId is not null)
                {
                    evt.IsRecurrenceExceptionDeleted = true;
                    evt.UpdatedAt = now;
                    await db.SaveChangesAsync(ct);
                    return Results.NoContent();
                }

                if (!occurrenceStartAt.HasValue)
                {
                    return Results.BadRequest(new { error = "occurrenceStartAt is required when deleting a single occurrence of a repeating series." });
                }

                var occStartUtc = occurrenceStartAt.Value.ToUniversalTime();
                var existingOverride = await db.CalendarEvents.FirstOrDefaultAsync(
                    e => e.UserId == userId.Value
                         && e.RecurrenceMasterId == evt.Id
                         && e.RecurrenceOriginalStartAt == occStartUtc,
                    ct);

                if (existingOverride is not null)
                {
                    existingOverride.IsRecurrenceExceptionDeleted = true;
                    existingOverride.UpdatedAt = now;
                }
                else
                {
                    var duration = evt.EndAt >= evt.StartAt ? evt.EndAt - evt.StartAt : TimeSpan.Zero;
                    db.CalendarEvents.Add(new CalendarEvent
                    {
                        Id = Guid.NewGuid(),
                        CalendarId = evt.CalendarId,
                        UserId = userId.Value,
                        Title = evt.Title,
                        StartAt = occStartUtc,
                        EndAt = occStartUtc + duration,
                        IsAllDay = evt.IsAllDay,
                        Timezone = evt.Timezone,
                        Status = "cancelled",
                        RecurrenceMasterId = evt.Id,
                        RecurrenceOriginalStartAt = occStartUtc,
                        IsRecurrenceExceptionDeleted = true,
                        Source = evt.Source ?? "Manual",
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                }

                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            }

            // Whole series delete (or standalone event delete)
            if (evt.RecurrenceMasterId.HasValue)
            {
                var master = await db.CalendarEvents
                    .FirstOrDefaultAsync(e => e.Id == evt.RecurrenceMasterId.Value && e.UserId == userId.Value, ct);
                if (master is not null)
                {
                    db.CalendarEvents.Remove(master);
                    await db.SaveChangesAsync(ct);
                    return Results.NoContent();
                }
            }

            db.CalendarEvents.Remove(evt);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);
    }

    private static string? NormalizeRecurrenceScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope) || scope.Equals("series", StringComparison.OrdinalIgnoreCase))
        {
            return "series";
        }

        if (scope.Equals("single", StringComparison.OrdinalIgnoreCase))
        {
            return "single";
        }

        return null;
    }

    private static IResult? CheckCalendarReadAccess(ClaimsPrincipal principal, out Guid userId)
    {
        var parsedId = principal.GetUserId();
        if (parsedId is null)
        {
            userId = Guid.Empty;
            return Results.Unauthorized();
        }

        userId = parsedId.Value;

        var authType = principal.FindFirstValue(AuthConstants.AuthTypeClaim);
        if (string.Equals(authType, AuthConstants.PatAuthType, StringComparison.Ordinal))
        {
            var hasScope = principal.FindAll(AuthConstants.ScopeClaim)
                .Any(c => string.Equals(c.Value, "calendar:read", StringComparison.Ordinal));
            if (!hasScope)
            {
                return Results.Forbid();
            }
        }

        return null;
    }

    private static async Task<List<UserCalendar>> EnsureUserCalendarsAsync(
        AppDbContext db,
        Guid userId,
        CancellationToken ct)
    {
        var calendars = await db.Calendars
            .Where(c => c.UserId == userId)
            .ToListAsync(ct);

        if (calendars.Count > 0)
        {
            return calendars;
        }

        var userTimezone = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Timezone)
            .FirstOrDefaultAsync(ct) ?? "UTC";

        var now = DateTimeOffset.UtcNow;
        var personal = CreateDefaultCalendar(userId, userTimezone, now);

        db.Calendars.Add(personal);
        await db.SaveChangesAsync(ct);
        return [personal];
    }

    internal static UserCalendar CreateDefaultCalendar(Guid userId, string timezone, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Name = DefaultCalendarName,
        Description = "Default personal calendar",
        Color = DefaultCalendarColor,
        Timezone = string.IsNullOrWhiteSpace(timezone) ? "UTC" : timezone,
        IsDefault = true,
        IsVisible = true,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static async Task<(string? Content, string? FileName, string? Name, string? Color, IResult? Error)> ReadIcsPayloadAsync(
        HttpRequest request,
        CancellationToken ct)
    {
        string? explicitName = request.Query["name"].FirstOrDefault();
        string? explicitColor = request.Query["color"].FirstOrDefault();

        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? (form.Files.Count > 0 ? form.Files[0] : null);
            if (file is null || file.Length == 0)
            {
                return (null, null, null, null, Results.BadRequest(new { error = "Please upload a non-empty .ics calendar file." }));
            }

            if (file.Length > MaxIcsUploadBytes)
            {
                return (null, null, null, null, Results.BadRequest(new { error = "Calendar file must be 5 MB or smaller." }));
            }

            if (string.IsNullOrWhiteSpace(explicitName))
            {
                explicitName = form["name"].FirstOrDefault();
            }

            if (string.IsNullOrWhiteSpace(explicitColor))
            {
                explicitColor = form["color"].FirstOrDefault();
            }

            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = await reader.ReadToEndAsync(ct);
            return (text, file.FileName, explicitName, explicitColor, null);
        }

        if (request.ContentLength.HasValue && request.ContentLength.Value > MaxIcsUploadBytes)
        {
            return (null, null, null, null, Results.BadRequest(new { error = "Calendar file must be 5 MB or smaller." }));
        }

        using var bufferStream = new MemoryStream();
        var chunk = new byte[16 * 1024];
        var totalRead = 0;
        while (true)
        {
            var read = await request.Body.ReadAsync(chunk, ct);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
            if (totalRead > MaxIcsUploadBytes)
            {
                return (null, null, null, null, Results.BadRequest(new { error = "Calendar file must be 5 MB or smaller." }));
            }

            bufferStream.Write(chunk, 0, read);
        }

        bufferStream.Position = 0;
        using var bodyReader = new StreamReader(bufferStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var rawText = await bodyReader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return (null, null, null, null, Results.BadRequest(new { error = "Calendar file body is empty." }));
        }

        return (rawText, null, explicitName, explicitColor, null);
    }

    private static async Task<IcsImportResponse> UpsertParsedEventsAsync(
        AppDbContext db,
        UserCalendar calendar,
        ParsedIcsCalendar parsed,
        CancellationToken ct)
    {
        var masterItems = parsed.Events.Where(e => !e.RecurrenceId.HasValue).ToList();
        var overrideItems = parsed.Events.Where(e => e.RecurrenceId.HasValue).ToList();

        var uids = parsed.Events
            .Where(e => !string.IsNullOrWhiteSpace(e.Uid))
            .Select(e => e.Uid!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var existingByUid = uids.Count == 0
            ? new Dictionary<string, CalendarEvent>(StringComparer.Ordinal)
            : await db.CalendarEvents
                .Where(e => e.CalendarId == calendar.Id && e.RecurrenceMasterId == null && e.ExternalUid != null && uids.Contains(e.ExternalUid))
                .ToDictionaryAsync(e => e.ExternalUid!, StringComparer.Ordinal, ct);

        var importedCount = 0;
        var updatedCount = 0;
        var now = DateTimeOffset.UtcNow;
        var touched = new List<CalendarEvent>(parsed.Events.Count);
        var pendingExDates = new List<(CalendarEvent Master, IReadOnlyList<DateTimeOffset> ExDates)>();
        var warnings = new List<string>(parsed.Warnings);

        foreach (var item in masterItems)
        {
            var effectiveRRule = item.RecurrenceRule;
            if (!string.IsNullOrWhiteSpace(effectiveRRule)
                && !RRuleExpander.TryParse(effectiveRRule, out _, out var rruleError))
            {
                warnings.Add(
                    $"Event '{item.Title}' has an unsupported recurrence rule ({rruleError ?? effectiveRRule}) and was imported as a single event.");
                effectiveRRule = null;
            }

            CalendarEvent masterEntity;
            if (item.Uid is not null && existingByUid.TryGetValue(item.Uid, out var existing))
            {
                existing.Title = item.Title;
                existing.Description = item.Description;
                existing.Location = item.Location;
                existing.StartAt = item.StartAt;
                existing.EndAt = item.EndAt;
                existing.IsAllDay = item.IsAllDay;
                existing.Timezone = item.Timezone ?? calendar.Timezone;
                existing.Status = item.Status;
                existing.RecurrenceRule = effectiveRRule;
                existing.Source = "ICS Import";
                existing.UpdatedAt = now;
                updatedCount++;
                masterEntity = existing;
            }
            else
            {
                masterEntity = new CalendarEvent
                {
                    Id = Guid.NewGuid(),
                    CalendarId = calendar.Id,
                    UserId = calendar.UserId,
                    Title = item.Title,
                    Description = item.Description,
                    Location = item.Location,
                    StartAt = item.StartAt,
                    EndAt = item.EndAt,
                    IsAllDay = item.IsAllDay,
                    Timezone = item.Timezone ?? calendar.Timezone,
                    Status = item.Status,
                    ExternalUid = item.Uid,
                    RecurrenceRule = effectiveRRule,
                    Source = "ICS Import",
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.CalendarEvents.Add(masterEntity);
                if (item.Uid is not null)
                {
                    existingByUid[item.Uid] = masterEntity;
                }

                importedCount++;
            }

            touched.Add(masterEntity);
            if (item.ExceptionDates is { Count: > 0 })
            {
                pendingExDates.Add((masterEntity, item.ExceptionDates));
            }
        }

        // Persist masters first so FK targets exist for EXDATE tombstones & RECURRENCE-ID overrides
        await db.SaveChangesAsync(ct);

        var masterIdsWithExceptions = pendingExDates.Select(x => x.Master.Id)
            .Concat(overrideItems
                .Where(o => o.Uid is not null && existingByUid.ContainsKey(o.Uid))
                .Select(o => existingByUid[o.Uid!].Id))
            .Distinct()
            .ToList();

        var existingOverridesByKey = masterIdsWithExceptions.Count == 0
            ? new Dictionary<(Guid MasterId, DateTimeOffset OrigStart), CalendarEvent>()
            : (await db.CalendarEvents
                .Where(e =>
                    e.UserId == calendar.UserId
                    && e.RecurrenceMasterId != null
                    && masterIdsWithExceptions.Contains(e.RecurrenceMasterId.Value)
                    && e.RecurrenceOriginalStartAt != null)
                .ToListAsync(ct))
                .ToDictionary(e => (e.RecurrenceMasterId!.Value, e.RecurrenceOriginalStartAt!.Value.ToUniversalTime()));

        foreach (var (master, exDates) in pendingExDates)
        {
            var duration = master.EndAt >= master.StartAt ? master.EndAt - master.StartAt : TimeSpan.Zero;
            foreach (var exDate in exDates)
            {
                var exUtc = exDate.ToUniversalTime();
                var key = (master.Id, exUtc);
                if (existingOverridesByKey.TryGetValue(key, out var existingOv))
                {
                    existingOv.IsRecurrenceExceptionDeleted = true;
                    existingOv.UpdatedAt = now;
                }
                else
                {
                    var tombstone = new CalendarEvent
                    {
                        Id = Guid.NewGuid(),
                        CalendarId = calendar.Id,
                        UserId = calendar.UserId,
                        Title = master.Title,
                        StartAt = exUtc,
                        EndAt = exUtc + duration,
                        IsAllDay = master.IsAllDay,
                        Timezone = master.Timezone,
                        Status = "cancelled",
                        RecurrenceMasterId = master.Id,
                        RecurrenceOriginalStartAt = exUtc,
                        IsRecurrenceExceptionDeleted = true,
                        Source = "ICS Import",
                        CreatedAt = now,
                        UpdatedAt = now,
                    };
                    db.CalendarEvents.Add(tombstone);
                    existingOverridesByKey[key] = tombstone;
                }
            }
        }

        foreach (var item in overrideItems)
        {
            var recIdUtc = item.RecurrenceId!.Value.ToUniversalTime();
            if (item.Uid is not null && existingByUid.TryGetValue(item.Uid, out var master))
            {
                var key = (master.Id, recIdUtc);
                if (existingOverridesByKey.TryGetValue(key, out var existingOv))
                {
                    existingOv.Title = item.Title;
                    existingOv.Description = item.Description;
                    existingOv.Location = item.Location;
                    existingOv.StartAt = item.StartAt;
                    existingOv.EndAt = item.EndAt;
                    existingOv.IsAllDay = item.IsAllDay;
                    existingOv.Timezone = item.Timezone ?? calendar.Timezone;
                    existingOv.Status = item.Status;
                    existingOv.IsRecurrenceExceptionDeleted = false;
                    existingOv.Source = "ICS Import";
                    existingOv.UpdatedAt = now;
                    updatedCount++;
                    touched.Add(existingOv);
                }
                else
                {
                    var createdOv = new CalendarEvent
                    {
                        Id = Guid.NewGuid(),
                        CalendarId = calendar.Id,
                        UserId = calendar.UserId,
                        Title = item.Title,
                        Description = item.Description,
                        Location = item.Location,
                        StartAt = item.StartAt,
                        EndAt = item.EndAt,
                        IsAllDay = item.IsAllDay,
                        Timezone = item.Timezone ?? calendar.Timezone,
                        Status = item.Status,
                        RecurrenceMasterId = master.Id,
                        RecurrenceMaster = master,
                        RecurrenceOriginalStartAt = recIdUtc,
                        IsRecurrenceExceptionDeleted = false,
                        Source = "ICS Import",
                        CreatedAt = now,
                        UpdatedAt = now,
                    };
                    db.CalendarEvents.Add(createdOv);
                    existingOverridesByKey[key] = createdOv;
                    importedCount++;
                    touched.Add(createdOv);
                }
            }
            else
            {
                var standalone = new CalendarEvent
                {
                    Id = Guid.NewGuid(),
                    CalendarId = calendar.Id,
                    UserId = calendar.UserId,
                    Title = item.Title,
                    Description = item.Description,
                    Location = item.Location,
                    StartAt = item.StartAt,
                    EndAt = item.EndAt,
                    IsAllDay = item.IsAllDay,
                    Timezone = item.Timezone ?? calendar.Timezone,
                    Status = item.Status,
                    Source = "ICS Import",
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.CalendarEvents.Add(standalone);
                importedCount++;
                touched.Add(standalone);
            }
        }

        await db.SaveChangesAsync(ct);

        var eventDtos = touched
            .OrderBy(e => e.StartAt)
            .ThenBy(e => e.EndAt)
            .Select(e => ToEventDto(e, calendar))
            .ToList();

        return new IcsImportResponse(
            ToCalendarDto(calendar),
            importedCount,
            updatedCount,
            parsed.Warnings.Count,
            warnings,
            eventDtos);
    }

    private static string? ValidateEventPayload(
        string? title,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        string? location,
        string? timezone,
        string? status,
        string? recurrenceRule,
        out string normalizedTitle,
        out string normalizedStatus,
        out string? normalizedRRule,
        string? source = null)
    {
        normalizedTitle = (title ?? string.Empty).Trim();
        normalizedStatus = string.IsNullOrWhiteSpace(status) ? "confirmed" : status.Trim().ToLowerInvariant();
        normalizedRRule = string.IsNullOrWhiteSpace(recurrenceRule) ? null : recurrenceRule.Trim();

        if (string.IsNullOrEmpty(normalizedTitle) || normalizedTitle.Length > 200)
        {
            return "Event title must be 1–200 characters.";
        }

        if (endAt < startAt)
        {
            return "Event end time must be on or after start time.";
        }

        if (location is not null && location.Trim().Length > 280)
        {
            return "Location must be 280 characters or fewer.";
        }

        if (timezone is not null && timezone.Trim().Length > 64)
        {
            return "Timezone must be 64 characters or fewer.";
        }

        if (source is not null && source.Trim().Length > 80)
        {
            return "Source must be 80 characters or fewer.";
        }

        if (!ValidEventStatuses.Contains(normalizedStatus))
        {
            return "Status must be confirmed, tentative, or cancelled.";
        }

        if (normalizedRRule is not null)
        {
            if (normalizedRRule.Length > 255)
            {
                return "Recurrence rule must be 255 characters or fewer.";
            }

            if (!RRuleExpander.TryParse(normalizedRRule, out _, out var rruleError))
            {
                return rruleError ?? "Invalid recurrence rule.";
            }
        }

        return null;
    }

    private static string DeriveCalendarNameFromFilename(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "Imported Calendar";
        }

        var stem = Path.GetFileNameWithoutExtension(fileName).Trim();
        return string.IsNullOrWhiteSpace(stem) ? "Imported Calendar" : stem;
    }

    private static string SanitizeFileName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.Trim())
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_')
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (char.IsWhiteSpace(ch))
            {
                sb.Append('-');
            }
        }

        return sb.Length > 0 ? sb.ToString() : "calendar";
    }

    private static CalendarDto ToCalendarDto(UserCalendar c) => new(
        c.Id,
        c.Name,
        c.Description,
        c.Color,
        c.Timezone,
        c.IsDefault,
        c.IsVisible,
        c.CreatedAt,
        c.UpdatedAt);

    private static CalendarEventDto ToEventDto(CalendarEvent e, UserCalendar c)
    {
        var isException = e.RecurrenceMasterId.HasValue;
        var isRecurring = isException || !string.IsNullOrWhiteSpace(e.RecurrenceRule);
        var seriesMasterId = isException ? e.RecurrenceMasterId : (isRecurring ? e.Id : (Guid?)null);
        var occurrenceStartAt = isException
            ? e.RecurrenceOriginalStartAt?.ToUniversalTime()
            : (isRecurring ? e.StartAt.ToUniversalTime() : (DateTimeOffset?)null);
        var effectiveRRule = isException ? e.RecurrenceMaster?.RecurrenceRule : e.RecurrenceRule;

        return new CalendarEventDto(
            e.Id,
            e.CalendarId,
            c.Name,
            c.Color,
            e.Title,
            e.Description,
            e.Location,
            e.StartAt.ToUniversalTime(),
            e.EndAt.ToUniversalTime(),
            e.IsAllDay,
            e.Timezone,
            e.Status,
            e.ExternalUid,
            effectiveRRule,
            e.Source,
            e.CreatedAt,
            e.UpdatedAt,
            seriesMasterId,
            occurrenceStartAt,
            isRecurring,
            isException);
    }

    private static CalendarEventDto ToExpandedOccurrenceDto(
        CalendarEvent master,
        UserCalendar calendar,
        DateTimeOffset occStartUtc,
        DateTimeOffset occEndUtc) => new(
            master.Id,
            master.CalendarId,
            calendar.Name,
            calendar.Color,
            master.Title,
            master.Description,
            master.Location,
            occStartUtc,
            occEndUtc,
            master.IsAllDay,
            master.Timezone,
            master.Status,
            master.ExternalUid,
            master.RecurrenceRule,
            master.Source,
            master.CreatedAt,
            master.UpdatedAt,
            master.Id,
            occStartUtc,
            IsRecurring: true,
            IsRecurrenceException: false);
}
