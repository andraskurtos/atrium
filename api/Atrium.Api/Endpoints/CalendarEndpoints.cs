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

            var events = await db.CalendarEvents
                .AsNoTracking()
                .Where(e => e.CalendarId == calendar.Id && e.UserId == userId)
                .OrderBy(e => e.StartAt)
                .ThenBy(e => e.EndAt)
                .ToListAsync(ct);

            var dtos = events.Select(e => ToEventDto(e, calendar)).ToList();
            var icsContent = IcsParser.Serialize(calendar.Name, calendar.Description, calendar.Timezone, dtos);
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

            IQueryable<CalendarEvent> query = db.CalendarEvents
                .AsNoTracking()
                .Include(e => e.Calendar)
                .Where(e => e.UserId == userId);

            if (calendarId is { Length: > 0 })
            {
                query = query.Where(e => calendarId.Contains(e.CalendarId));
            }

            if (visibleOnly == true)
            {
                query = query.Where(e => e.Calendar.IsVisible);
            }

            if (from.HasValue)
            {
                var fromUtc = from.Value.ToUniversalTime();
                query = query.Where(e => e.EndAt > fromUtc || (e.StartAt == e.EndAt && e.StartAt >= fromUtc));
            }

            if (to.HasValue)
            {
                var toUtc = to.Value.ToUniversalTime();
                query = query.Where(e => e.StartAt < toUtc);
            }

            var items = await query
                .OrderBy(e => e.StartAt)
                .ThenBy(e => e.EndAt)
                .ThenBy(e => e.Title)
                .ToListAsync(ct);

            return Results.Ok(items.Select(e => ToEventDto(e, e.Calendar)).ToList());
        }).RequireAuthorization(AuthConstants.HumanOrAgentPolicy);

        eventsGroup.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var accessError = CheckCalendarReadAccess(principal, out var userId);
            if (accessError is not null) return accessError;

            var evt = await db.CalendarEvents
                .AsNoTracking()
                .Include(e => e.Calendar)
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId, ct);

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
                out var normalizedStatus);
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
                RecurrenceRule = string.IsNullOrWhiteSpace(request.RecurrenceRule) ? null : request.RecurrenceRule.Trim(),
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
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var evt = await db.CalendarEvents
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId.Value, ct);
            if (evt is null) return Results.NotFound();

            var validationError = ValidateEventPayload(
                request.Title,
                request.StartAt,
                request.EndAt,
                request.Location,
                request.Timezone,
                request.Status,
                request.RecurrenceRule,
                out var normalizedTitle,
                out var normalizedStatus);
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

            evt.CalendarId = targetCalendar.Id;
            evt.Title = normalizedTitle;
            evt.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            evt.Location = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim();
            evt.StartAt = request.StartAt.ToUniversalTime();
            evt.EndAt = request.EndAt.ToUniversalTime();
            evt.IsAllDay = request.IsAllDay;
            evt.Timezone = string.IsNullOrWhiteSpace(request.Timezone) ? targetCalendar.Timezone : request.Timezone.Trim();
            evt.Status = normalizedStatus;
            evt.RecurrenceRule = string.IsNullOrWhiteSpace(request.RecurrenceRule) ? null : request.RecurrenceRule.Trim();
            evt.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToEventDto(evt, targetCalendar));
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        eventsGroup.MapDelete("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var evt = await db.CalendarEvents
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId.Value, ct);
            if (evt is null) return Results.NotFound();

            db.CalendarEvents.Remove(evt);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);
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

        using var bodyReader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
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
        var uids = parsed.Events
            .Where(e => !string.IsNullOrWhiteSpace(e.Uid))
            .Select(e => e.Uid!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var existingByUid = uids.Count == 0
            ? new Dictionary<string, CalendarEvent>(StringComparer.Ordinal)
            : await db.CalendarEvents
                .Where(e => e.CalendarId == calendar.Id && e.ExternalUid != null && uids.Contains(e.ExternalUid))
                .ToDictionaryAsync(e => e.ExternalUid!, StringComparer.Ordinal, ct);

        var importedCount = 0;
        var updatedCount = 0;
        var now = DateTimeOffset.UtcNow;
        var touched = new List<CalendarEvent>(parsed.Events.Count);

        foreach (var item in parsed.Events)
        {
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
                existing.RecurrenceRule = item.RecurrenceRule;
                existing.Source = "ICS Import";
                existing.UpdatedAt = now;
                updatedCount++;
                touched.Add(existing);
            }
            else
            {
                var created = new CalendarEvent
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
                    RecurrenceRule = item.RecurrenceRule,
                    Source = "ICS Import",
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.CalendarEvents.Add(created);
                if (item.Uid is not null)
                {
                    existingByUid[item.Uid] = created;
                }

                importedCount++;
                touched.Add(created);
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
            parsed.Warnings,
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
        out string normalizedStatus)
    {
        normalizedTitle = (title ?? string.Empty).Trim();
        normalizedStatus = string.IsNullOrWhiteSpace(status) ? "confirmed" : status.Trim().ToLowerInvariant();

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

        if (!ValidEventStatuses.Contains(normalizedStatus))
        {
            return "Status must be confirmed, tentative, or cancelled.";
        }

        if (recurrenceRule is not null && recurrenceRule.Trim().Length > 255)
        {
            return "Recurrence rule must be 255 characters or fewer.";
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

    private static CalendarEventDto ToEventDto(CalendarEvent e, UserCalendar c) => new(
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
        e.RecurrenceRule,
        e.Source,
        e.CreatedAt,
        e.UpdatedAt);
}
