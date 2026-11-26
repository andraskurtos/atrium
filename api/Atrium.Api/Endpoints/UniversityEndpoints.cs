using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Atrium.Api.Auth;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Atrium.Api.University;
using Microsoft.EntityFrameworkCore;

namespace Atrium.Api.Endpoints;

public static partial class UniversityEndpoints
{
    private const int MaxBulkSeriesCount = 52;
    private const int MaxMetadataJsonChars = 16_384;

    [GeneratedRegex(@"^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorRegex();

    public static void MapUniversityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/university");

        group.MapGet("/workspace", async (
            string? semester,
            string? status,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var accessError = CheckUniversityReadAccess(principal, out var userId);
            if (accessError is not null) return accessError;

            var uniCalendar = await EnsureUniversityCalendarAsync(db, userId, ct);
            var allCourses = await LoadUserCoursesAsync(db, userId, ct);

            var semesters = allCourses
                .Select(c => c.Semester)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var filtered = FilterCourses(allCourses, semester, status)
                .Select(ToCourseDto)
                .ToList();

            return Results.Ok(new UniversityWorkspaceResponse(
                CalendarEndpoints.ToCalendarDto(uniCalendar),
                semesters,
                filtered));
        }).RequireAuthorization(AuthConstants.HumanOrAgentPolicy);

        group.MapGet("/courses", async (
            string? semester,
            string? status,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var accessError = CheckUniversityReadAccess(principal, out var userId);
            if (accessError is not null) return accessError;

            var courses = await LoadUserCoursesAsync(db, userId, ct);
            var filtered = FilterCourses(courses, semester, status)
                .Select(ToCourseDto)
                .ToList();

            return Results.Ok(filtered);
        }).RequireAuthorization(AuthConstants.HumanOrAgentPolicy);

        group.MapGet("/courses/{courseId:guid}", async (
            Guid courseId,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var accessError = CheckUniversityReadAccess(principal, out var userId);
            if (accessError is not null) return accessError;

            var course = await LoadSingleCourseAsync(db, userId, courseId, ct);
            return course is null ? Results.NotFound() : Results.Ok(ToCourseDto(course));
        }).RequireAuthorization(AuthConstants.HumanOrAgentPolicy);

        group.MapPost("/courses", async (
            CreateCourseRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var userExists = await db.Users.AnyAsync(u => u.Id == userId.Value && u.IsActive, ct);
            if (!userExists) return Results.Unauthorized();

            var validationError = ValidateCourseInput(
                request.Name,
                request.Code,
                request.Semester,
                request.Instructor,
                request.Credits,
                request.Color,
                request.Status,
                request.MetadataJson,
                out var normalizedColor,
                out var normalizedStatus);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            if (request.Activities is not null)
            {
                foreach (var actReq in request.Activities)
                {
                    var actError = ValidateActivityInput(
                        actReq.ActivityType,
                        actReq.Title,
                        actReq.DefaultLocation,
                        actReq.MinAttendanceCount,
                        actReq.MinAttendancePercent,
                        actReq.PlannedTotalCount,
                        actReq.MetadataJson,
                        out _);
                    if (actError is not null)
                    {
                        return Results.BadRequest(new { error = actError });
                    }
                }
            }

            await EnsureUniversityCalendarAsync(db, userId.Value, ct);

            var now = DateTimeOffset.UtcNow;
            var course = new UniversityCourse
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                Name = request.Name.Trim(),
                Code = TrimOrNull(request.Code),
                Semester = TrimOrNull(request.Semester),
                Description = TrimOrNull(request.Description),
                Instructor = TrimOrNull(request.Instructor),
                Credits = request.Credits,
                Color = normalizedColor,
                Status = normalizedStatus,
                MetadataJson = TrimOrNull(request.MetadataJson),
                CreatedAt = now,
                UpdatedAt = now,
            };

            if (request.Activities is not null)
            {
                var order = 0;
                foreach (var actReq in request.Activities)
                {
                    var normalizedType = actReq.ActivityType.Trim().ToLowerInvariant();
                    var supportsAttendance = UniversityConstants.SupportsAttendance(normalizedType);
                    course.Activities.Add(new UniversityCourseActivity
                    {
                        Id = Guid.NewGuid(),
                        CourseId = course.Id,
                        UserId = userId.Value,
                        ActivityType = normalizedType,
                        Title = TrimOrNull(actReq.Title),
                        Description = TrimOrNull(actReq.Description),
                        DefaultLocation = TrimOrNull(actReq.DefaultLocation),
                        MinAttendanceCount = supportsAttendance ? actReq.MinAttendanceCount : null,
                        MinAttendancePercent = supportsAttendance ? actReq.MinAttendancePercent : null,
                        PlannedTotalCount = supportsAttendance ? actReq.PlannedTotalCount : null,
                        SortOrder = actReq.SortOrder ?? order,
                        MetadataJson = TrimOrNull(actReq.MetadataJson),
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                    order++;
                }
            }

            db.UniversityCourses.Add(course);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/university/courses/{course.Id}", ToCourseDto(course));
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapPut("/courses/{courseId:guid}", async (
            Guid courseId,
            UpdateCourseRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var course = await db.UniversityCourses
                .Include(c => c.Activities)
                    .ThenInclude(a => a.Items)
                        .ThenInclude(i => i.CalendarEvent)
                .FirstOrDefaultAsync(c => c.Id == courseId && c.UserId == userId.Value, ct);
            if (course is null) return Results.NotFound();

            var validationError = ValidateCourseInput(
                request.Name,
                request.Code,
                request.Semester,
                request.Instructor,
                request.Credits,
                request.Color ?? course.Color,
                request.Status ?? course.Status,
                request.MetadataJson,
                out var normalizedColor,
                out var normalizedStatus);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            var now = DateTimeOffset.UtcNow;
            course.Name = request.Name.Trim();
            course.Code = TrimOrNull(request.Code);
            course.Semester = TrimOrNull(request.Semester);
            course.Description = TrimOrNull(request.Description);
            course.Instructor = TrimOrNull(request.Instructor);
            course.Credits = request.Credits;
            course.Color = normalizedColor;
            course.Status = normalizedStatus;
            course.MetadataJson = TrimOrNull(request.MetadataJson);
            course.UpdatedAt = now;

            // Keep linked calendar event titles in sync with course code/name
            foreach (var activity in course.Activities)
            {
                foreach (var item in activity.Items)
                {
                    if (item.CalendarEvent is not null)
                    {
                        item.CalendarEvent.Title = BuildCalendarEventTitle(course, item.Title);
                        item.CalendarEvent.UpdatedAt = now;
                    }
                }
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToCourseDto(course));
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapDelete("/courses/{courseId:guid}", async (
            Guid courseId,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var course = await db.UniversityCourses
                .Include(c => c.Activities)
                    .ThenInclude(a => a.Items)
                        .ThenInclude(i => i.CalendarEvent)
                .FirstOrDefaultAsync(c => c.Id == courseId && c.UserId == userId.Value, ct);
            if (course is null) return Results.NotFound();

            var linkedEvents = course.Activities
                .SelectMany(a => a.Items)
                .Where(i => i.CalendarEvent is not null)
                .Select(i => i.CalendarEvent!)
                .ToList();

            if (linkedEvents.Count > 0)
            {
                db.CalendarEvents.RemoveRange(linkedEvents);
            }

            db.UniversityCourses.Remove(course);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapPost("/courses/{courseId:guid}/activities", async (
            Guid courseId,
            CreateActivityRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var course = await db.UniversityCourses
                .Include(c => c.Activities)
                .FirstOrDefaultAsync(c => c.Id == courseId && c.UserId == userId.Value, ct);
            if (course is null) return Results.NotFound();

            var validationError = ValidateActivityInput(
                request.ActivityType,
                request.Title,
                request.DefaultLocation,
                request.MinAttendanceCount,
                request.MinAttendancePercent,
                request.PlannedTotalCount,
                request.MetadataJson,
                out var normalizedType);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            var supportsAttendance = UniversityConstants.SupportsAttendance(normalizedType);
            var now = DateTimeOffset.UtcNow;
            var nextSortOrder = request.SortOrder ??
                (course.Activities.Count > 0 ? course.Activities.Max(a => a.SortOrder) + 1 : 0);

            var activity = new UniversityCourseActivity
            {
                Id = Guid.NewGuid(),
                CourseId = course.Id,
                UserId = userId.Value,
                ActivityType = normalizedType,
                Title = TrimOrNull(request.Title),
                Description = TrimOrNull(request.Description),
                DefaultLocation = TrimOrNull(request.DefaultLocation),
                MinAttendanceCount = supportsAttendance ? request.MinAttendanceCount : null,
                MinAttendancePercent = supportsAttendance ? request.MinAttendancePercent : null,
                PlannedTotalCount = supportsAttendance ? request.PlannedTotalCount : null,
                SortOrder = nextSortOrder,
                MetadataJson = TrimOrNull(request.MetadataJson),
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.UniversityCourseActivities.Add(activity);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/university/activities/{activity.Id}", ToActivityDto(activity));
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapPut("/activities/{activityId:guid}", async (
            Guid activityId,
            UpdateActivityRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var activity = await db.UniversityCourseActivities
                .Include(a => a.Items)
                    .ThenInclude(i => i.CalendarEvent)
                .FirstOrDefaultAsync(a => a.Id == activityId && a.UserId == userId.Value, ct);
            if (activity is null) return Results.NotFound();

            var validationError = ValidateActivityInput(
                activity.ActivityType,
                request.Title,
                request.DefaultLocation,
                request.MinAttendanceCount,
                request.MinAttendancePercent,
                request.PlannedTotalCount,
                request.MetadataJson,
                out _);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            var supportsAttendance = UniversityConstants.SupportsAttendance(activity.ActivityType);
            var now = DateTimeOffset.UtcNow;
            activity.Title = TrimOrNull(request.Title);
            activity.Description = TrimOrNull(request.Description);
            activity.DefaultLocation = TrimOrNull(request.DefaultLocation);
            activity.MinAttendanceCount = supportsAttendance ? request.MinAttendanceCount : null;
            activity.MinAttendancePercent = supportsAttendance ? request.MinAttendancePercent : null;
            activity.PlannedTotalCount = supportsAttendance ? request.PlannedTotalCount : null;
            if (request.SortOrder.HasValue)
            {
                activity.SortOrder = request.SortOrder.Value;
            }

            activity.MetadataJson = TrimOrNull(request.MetadataJson);
            activity.UpdatedAt = now;

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToActivityDto(activity));
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapDelete("/activities/{activityId:guid}", async (
            Guid activityId,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var activity = await db.UniversityCourseActivities
                .Include(a => a.Items)
                    .ThenInclude(i => i.CalendarEvent)
                .FirstOrDefaultAsync(a => a.Id == activityId && a.UserId == userId.Value, ct);
            if (activity is null) return Results.NotFound();

            var linkedEvents = activity.Items
                .Where(i => i.CalendarEvent is not null)
                .Select(i => i.CalendarEvent!)
                .ToList();

            if (linkedEvents.Count > 0)
            {
                db.CalendarEvents.RemoveRange(linkedEvents);
            }

            db.UniversityCourseActivities.Remove(activity);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapPost("/activities/{activityId:guid}/items", async (
            Guid activityId,
            CreateActivityItemRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var activity = await db.UniversityCourseActivities
                .Include(a => a.Course)
                .Include(a => a.Items)
                .FirstOrDefaultAsync(a => a.Id == activityId && a.UserId == userId.Value, ct);
            if (activity is null) return Results.NotFound();

            var validationError = ValidateItemInput(
                request.Title,
                request.SequenceNumber,
                request.AttendanceStatus,
                request.Schedule,
                request.MetadataJson,
                out var normalizedStatus);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            var now = DateTimeOffset.UtcNow;
            var title = request.Title.Trim();
            var theme = TrimOrNull(request.Theme);
            var notes = TrimOrNull(request.Notes);

            CalendarEvent? linkedEvent = null;
            if (request.Schedule is not null)
            {
                var uniCalendar = await EnsureUniversityCalendarAsync(db, userId.Value, ct);
                linkedEvent = CreateLinkedCalendarEvent(
                    uniCalendar,
                    userId.Value,
                    activity.Course,
                    activity,
                    title,
                    theme,
                    notes,
                    request.Schedule,
                    now);
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
                Title = title,
                Theme = theme,
                Notes = notes,
                SequenceNumber = request.SequenceNumber,
                AttendanceStatus = normalizedStatus,
                MetadataJson = TrimOrNull(request.MetadataJson),
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.UniversityActivityItems.Add(item);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/university/items/{item.Id}", ToItemDto(item));
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapPost("/activities/{activityId:guid}/items/generate-series", async (
            Guid activityId,
            GenerateActivitySeriesRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var activity = await db.UniversityCourseActivities
                .Include(a => a.Course)
                .Include(a => a.Items)
                    .ThenInclude(i => i.CalendarEvent)
                .FirstOrDefaultAsync(a => a.Id == activityId && a.UserId == userId.Value, ct);
            if (activity is null) return Results.NotFound();

            if (request.Count < 1 || request.Count > MaxBulkSeriesCount)
            {
                return Results.BadRequest(new { error = $"Count must be between 1 and {MaxBulkSeriesCount}." });
            }

            if (request.IntervalWeeks < 1 || request.IntervalWeeks > 8)
            {
                return Results.BadRequest(new { error = "IntervalWeeks must be between 1 and 8." });
            }

            if (request.StartNumber < 0 || request.StartNumber > 10_000)
            {
                return Results.BadRequest(new { error = "StartNumber must be between 0 and 10000." });
            }

            if (request.EndAt < request.StartAt)
            {
                return Results.BadRequest(new { error = "End time must be on or after start time." });
            }

            var titlePrefix = string.IsNullOrWhiteSpace(request.TitlePrefix)
                ? (activity.Title ?? UniversityConstants.GetDefaultActivityLabel(activity.ActivityType))
                : request.TitlePrefix.Trim();
            if (titlePrefix.Length > 140)
            {
                return Results.BadRequest(new { error = "Title prefix must be 140 characters or fewer." });
            }

            var location = TrimOrNull(request.Location) ?? activity.DefaultLocation;
            if (location is not null && location.Length > 280)
            {
                return Results.BadRequest(new { error = "Location must be 280 characters or fewer." });
            }

            var defaultTheme = TrimOrNull(request.DefaultTheme);
            var uniCalendar = await EnsureUniversityCalendarAsync(db, userId.Value, ct);
            var duration = request.EndAt - request.StartAt;
            var firstStartUtc = request.StartAt.ToUniversalTime();
            var seriesTimeZone = request.IsAllDay ? TimeZoneInfo.Utc : ResolveTimeZone(uniCalendar.Timezone);
            var now = DateTimeOffset.UtcNow;

            var createdItems = new List<UniversityActivityItem>(request.Count);
            for (var index = 0; index < request.Count; index++)
            {
                var seq = request.StartNumber + index;
                var occStart = AddWeeksPreservingWallClock(
                    firstStartUtc,
                    index * request.IntervalWeeks,
                    request.IsAllDay,
                    seriesTimeZone);
                var occEnd = occStart + duration;
                var itemTitle = string.Create(CultureInfo.InvariantCulture, $"{titlePrefix} {seq}");

                var schedule = new ActivityItemScheduleInput(
                    occStart,
                    occEnd,
                    request.IsAllDay,
                    location);

                var calEvent = CreateLinkedCalendarEvent(
                    uniCalendar,
                    userId.Value,
                    activity.Course,
                    activity,
                    itemTitle,
                    defaultTheme,
                    null,
                    schedule,
                    now);

                var item = new UniversityActivityItem
                {
                    Id = Guid.NewGuid(),
                    ActivityId = activity.Id,
                    CourseId = activity.CourseId,
                    UserId = userId.Value,
                    CalendarEventId = calEvent.Id,
                    CalendarEvent = calEvent,
                    Title = itemTitle,
                    Theme = defaultTheme,
                    Notes = null,
                    SequenceNumber = seq,
                    AttendanceStatus = "pending",
                    MetadataJson = null,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                db.CalendarEvents.Add(calEvent);
                db.UniversityActivityItems.Add(item);
                createdItems.Add(item);
            }

            await db.SaveChangesAsync(ct);

            var response = new GenerateActivitySeriesResponse(
                createdItems.Select(ToItemDto).ToList(),
                UniversityAttendanceCalculator.Calculate(activity));

            return Results.Created($"/api/university/activities/{activity.Id}/items", response);
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapPut("/items/{itemId:guid}", async (
            Guid itemId,
            UpdateActivityItemRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var item = await db.UniversityActivityItems
                .Include(i => i.Activity)
                .Include(i => i.Course)
                .Include(i => i.CalendarEvent)
                .FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == userId.Value, ct);
            if (item is null) return Results.NotFound();

            var validationError = ValidateItemInput(
                request.Title,
                request.SequenceNumber,
                request.AttendanceStatus ?? item.AttendanceStatus,
                request.RemoveCalendarEvent ? null : request.Schedule,
                request.MetadataJson,
                out var normalizedStatus);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            var now = DateTimeOffset.UtcNow;
            item.Title = request.Title.Trim();
            item.Theme = TrimOrNull(request.Theme);
            item.Notes = TrimOrNull(request.Notes);
            item.SequenceNumber = request.SequenceNumber;
            item.AttendanceStatus = normalizedStatus;
            item.MetadataJson = TrimOrNull(request.MetadataJson);
            item.UpdatedAt = now;

            if (request.RemoveCalendarEvent)
            {
                if (item.CalendarEvent is not null)
                {
                    db.CalendarEvents.Remove(item.CalendarEvent);
                    item.CalendarEvent = null;
                }

                item.CalendarEventId = null;
            }
            else if (request.Schedule is not null)
            {
                if (item.CalendarEvent is null)
                {
                    var uniCalendar = await EnsureUniversityCalendarAsync(db, userId.Value, ct);
                    var createdEvent = CreateLinkedCalendarEvent(
                        uniCalendar,
                        userId.Value,
                        item.Course,
                        item.Activity,
                        item.Title,
                        item.Theme,
                        item.Notes,
                        request.Schedule,
                        now);
                    db.CalendarEvents.Add(createdEvent);
                    item.CalendarEventId = createdEvent.Id;
                    item.CalendarEvent = createdEvent;
                }
                else
                {
                    item.CalendarEvent.Title = BuildCalendarEventTitle(item.Course, item.Title);
                    item.CalendarEvent.Description = BuildCalendarEventDescription(item.Activity, item.Theme, item.Notes);
                    item.CalendarEvent.Location = TrimOrNull(request.Schedule.Location);
                    item.CalendarEvent.StartAt = request.Schedule.StartAt.ToUniversalTime();
                    item.CalendarEvent.EndAt = request.Schedule.EndAt.ToUniversalTime();
                    item.CalendarEvent.IsAllDay = request.Schedule.IsAllDay;
                    item.CalendarEvent.UpdatedAt = now;
                }
            }
            else if (item.CalendarEvent is not null)
            {
                item.CalendarEvent.Title = BuildCalendarEventTitle(item.Course, item.Title);
                item.CalendarEvent.Description = BuildCalendarEventDescription(item.Activity, item.Theme, item.Notes);
                item.CalendarEvent.UpdatedAt = now;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToItemDto(item));
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapPatch("/items/{itemId:guid}/attendance", async (
            Guid itemId,
            UpdateAttendanceStatusRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var normalizedStatus = (request.AttendanceStatus ?? string.Empty).Trim().ToLowerInvariant();
            if (!UniversityConstants.ValidAttendanceStatuses.Contains(normalizedStatus))
            {
                return Results.BadRequest(new
                {
                    error = "Attendance status must be pending, attended, missed, or excused.",
                });
            }

            var item = await db.UniversityActivityItems
                .Include(i => i.CalendarEvent)
                .Include(i => i.Activity)
                    .ThenInclude(a => a.Items)
                .FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == userId.Value, ct);
            if (item is null) return Results.NotFound();

            item.AttendanceStatus = normalizedStatus;
            item.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);

            var summary = UniversityAttendanceCalculator.Calculate(item.Activity);
            return Results.Ok(new UpdateAttendanceStatusResponse(ToItemDto(item), summary));
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapDelete("/items/{itemId:guid}", async (
            Guid itemId,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var item = await db.UniversityActivityItems
                .Include(i => i.CalendarEvent)
                .FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == userId.Value, ct);
            if (item is null) return Results.NotFound();

            if (item.CalendarEvent is not null)
            {
                db.CalendarEvents.Remove(item.CalendarEvent);
            }

            db.UniversityActivityItems.Remove(item);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);
    }

    private static IResult? CheckUniversityReadAccess(ClaimsPrincipal principal, out Guid userId)
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
                .Any(c => string.Equals(c.Value, "university:read", StringComparison.Ordinal));
            if (!hasScope)
            {
                return Results.Forbid();
            }
        }

        return null;
    }

    private static async Task<UserCalendar> EnsureUniversityCalendarAsync(
        AppDbContext db,
        Guid userId,
        CancellationToken ct)
    {
        var calendars = await CalendarEndpoints.EnsureUserCalendarsAsync(db, userId, ct);
        var existing = calendars.FirstOrDefault(c =>
            string.Equals(c.Module, UniversityConstants.CalendarModuleKey, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var userTimezone = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Timezone)
            .FirstOrDefaultAsync(ct) ?? "UTC";

        var now = DateTimeOffset.UtcNow;
        var uniCalendar = new UserCalendar
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = UniversityConstants.DefaultCalendarName,
            Description = UniversityConstants.DefaultCalendarDescription,
            Color = UniversityConstants.DefaultCalendarColor,
            Timezone = string.IsNullOrWhiteSpace(userTimezone) ? "UTC" : userTimezone,
            IsDefault = false,
            IsVisible = true,
            Module = UniversityConstants.CalendarModuleKey,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Calendars.Add(uniCalendar);
        try
        {
            await db.SaveChangesAsync(ct);
            return uniCalendar;
        }
        catch (DbUpdateException)
        {
            db.Entry(uniCalendar).State = EntityState.Detached;
            var raced = await db.Calendars.FirstOrDefaultAsync(
                c => c.UserId == userId && c.Module == UniversityConstants.CalendarModuleKey,
                ct);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private static DateTimeOffset AddWeeksPreservingWallClock(
        DateTimeOffset startUtc,
        int weeks,
        bool isAllDay,
        TimeZoneInfo tzInfo)
    {
        if (weeks == 0)
        {
            return startUtc;
        }

        if (isAllDay || tzInfo.Equals(TimeZoneInfo.Utc))
        {
            return startUtc.AddDays(weeks * 7);
        }

        var localStart = TimeZoneInfo.ConvertTimeFromUtc(startUtc.UtcDateTime, tzInfo);
        var targetLocal = DateTime.SpecifyKind(localStart.AddDays(weeks * 7), DateTimeKind.Unspecified);
        if (tzInfo.IsInvalidTime(targetLocal))
        {
            targetLocal = targetLocal.AddHours(1);
        }

        var offset = tzInfo.GetUtcOffset(targetLocal);
        return new DateTimeOffset(targetLocal, offset).ToUniversalTime();
    }

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

    private static async Task<List<UniversityCourse>> LoadUserCoursesAsync(
        AppDbContext db,
        Guid userId,
        CancellationToken ct)
    {
        return await db.UniversityCourses
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Include(c => c.Activities)
                .ThenInclude(a => a.Items)
                    .ThenInclude(i => i.CalendarEvent)
            .ToListAsync(ct);
    }

    private static async Task<UniversityCourse?> LoadSingleCourseAsync(
        AppDbContext db,
        Guid userId,
        Guid courseId,
        CancellationToken ct)
    {
        return await db.UniversityCourses
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.Id == courseId)
            .Include(c => c.Activities)
                .ThenInclude(a => a.Items)
                    .ThenInclude(i => i.CalendarEvent)
            .FirstOrDefaultAsync(ct);
    }

    private static IEnumerable<UniversityCourse> FilterCourses(
        List<UniversityCourse> courses,
        string? semester,
        string? status)
    {
        IEnumerable<UniversityCourse> query = courses;

        if (!string.IsNullOrWhiteSpace(semester))
        {
            var trimmedSemester = semester.Trim();
            if (string.Equals(trimmedSemester, "__unassigned__", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(c => string.IsNullOrWhiteSpace(c.Semester));
            }
            else if (!string.Equals(trimmedSemester, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(c => string.Equals(c.Semester, trimmedSemester, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var trimmedStatus = status.Trim();
            if (!string.Equals(trimmedStatus, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(c => string.Equals(c.Status, trimmedStatus, StringComparison.OrdinalIgnoreCase));
            }
        }

        return query
            .OrderBy(c => c.Status == "active" ? 0 : (c.Status == "completed" ? 1 : 2))
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.CreatedAt);
    }

    private static string? ValidateCourseInput(
        string? name,
        string? code,
        string? semester,
        string? instructor,
        decimal? credits,
        string? color,
        string? status,
        string? metadataJson,
        out string normalizedColor,
        out string normalizedStatus)
    {
        normalizedColor = UniversityConstants.DefaultCalendarColor;
        normalizedStatus = "active";

        var trimmedName = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmedName) || trimmedName.Length > 140)
        {
            return "Course name must be 1–140 characters.";
        }

        if (code is not null && code.Trim().Length > 32)
        {
            return "Course code must be 32 characters or fewer.";
        }

        if (semester is not null && semester.Trim().Length > 64)
        {
            return "Semester must be 64 characters or fewer.";
        }

        if (instructor is not null && instructor.Trim().Length > 120)
        {
            return "Instructor must be 120 characters or fewer.";
        }

        if (credits.HasValue && (credits.Value < 0m || credits.Value > 100m))
        {
            return "Credits must be between 0 and 100.";
        }

        var candidateColor = string.IsNullOrWhiteSpace(color) ? UniversityConstants.DefaultCalendarColor : color.Trim();
        if (!HexColorRegex().IsMatch(candidateColor))
        {
            return "Course color must be a 6-digit hex code like #3b6ea5.";
        }

        normalizedColor = candidateColor.ToLowerInvariant();

        var candidateStatus = string.IsNullOrWhiteSpace(status) ? "active" : status.Trim().ToLowerInvariant();
        if (!UniversityConstants.ValidCourseStatuses.Contains(candidateStatus))
        {
            return "Course status must be active, completed, or archived.";
        }

        normalizedStatus = candidateStatus;

        return ValidateMetadataJson(metadataJson);
    }

    private static string? ValidateActivityInput(
        string? activityType,
        string? title,
        string? defaultLocation,
        int? minAttendanceCount,
        decimal? minAttendancePercent,
        int? plannedTotalCount,
        string? metadataJson,
        out string normalizedType)
    {
        normalizedType = (activityType ?? string.Empty).Trim().ToLowerInvariant();
        if (!UniversityConstants.ValidActivityTypes.Contains(normalizedType))
        {
            return "Activity type must be lecture, practice, laboratory, homework_assignment, semester_assignment, midterm, or exam.";
        }

        if (title is not null && title.Trim().Length > 120)
        {
            return "Activity title must be 120 characters or fewer.";
        }

        if (defaultLocation is not null && defaultLocation.Trim().Length > 280)
        {
            return "Default location must be 280 characters or fewer.";
        }

        var supportsAttendance = UniversityConstants.SupportsAttendance(normalizedType);
        if (!supportsAttendance && (minAttendanceCount.HasValue || minAttendancePercent.HasValue || plannedTotalCount.HasValue))
        {
            return "Attendance requirements are only supported for lectures, practices, laboratories, and homework assignments.";
        }

        if (minAttendanceCount.HasValue && (minAttendanceCount.Value < 0 || minAttendanceCount.Value > 1000))
        {
            return "Minimum attendance count must be between 0 and 1000.";
        }

        if (minAttendancePercent.HasValue && (minAttendancePercent.Value < 0m || minAttendancePercent.Value > 100m))
        {
            return "Minimum attendance percentage must be between 0 and 100.";
        }

        if (plannedTotalCount.HasValue && (plannedTotalCount.Value < 1 || plannedTotalCount.Value > 1000))
        {
            return "Planned total count must be between 1 and 1000.";
        }

        return ValidateMetadataJson(metadataJson);
    }

    private static string? ValidateItemInput(
        string? title,
        int? sequenceNumber,
        string? attendanceStatus,
        ActivityItemScheduleInput? schedule,
        string? metadataJson,
        out string normalizedStatus)
    {
        normalizedStatus = "pending";

        var trimmedTitle = (title ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmedTitle) || trimmedTitle.Length > 200)
        {
            return "Item title must be 1–200 characters.";
        }

        if (sequenceNumber.HasValue && (sequenceNumber.Value < 0 || sequenceNumber.Value > 10_000))
        {
            return "Sequence number must be between 0 and 10000.";
        }

        var candidateStatus = string.IsNullOrWhiteSpace(attendanceStatus) ? "pending" : attendanceStatus.Trim().ToLowerInvariant();
        if (!UniversityConstants.ValidAttendanceStatuses.Contains(candidateStatus))
        {
            return "Attendance status must be pending, attended, missed, or excused.";
        }

        normalizedStatus = candidateStatus;

        if (schedule is not null)
        {
            if (schedule.EndAt < schedule.StartAt)
            {
                return "Event end time must be on or after start time.";
            }

            if (schedule.Location is not null && schedule.Location.Trim().Length > 280)
            {
                return "Location must be 280 characters or fewer.";
            }
        }

        return ValidateMetadataJson(metadataJson);
    }

    private static string? ValidateMetadataJson(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        var trimmed = metadataJson.Trim();
        if (trimmed.Length > MaxMetadataJsonChars)
        {
            return $"Metadata JSON must be {MaxMetadataJsonChars} characters or fewer.";
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return "Metadata JSON must be a JSON object.";
            }

            return null;
        }
        catch (JsonException)
        {
            return "Metadata JSON must be valid JSON.";
        }
    }

    private static CalendarEvent CreateLinkedCalendarEvent(
        UserCalendar uniCalendar,
        Guid userId,
        UniversityCourse course,
        UniversityCourseActivity activity,
        string itemTitle,
        string? theme,
        string? notes,
        ActivityItemScheduleInput schedule,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            CalendarId = uniCalendar.Id,
            UserId = userId,
            Title = BuildCalendarEventTitle(course, itemTitle),
            Description = BuildCalendarEventDescription(activity, theme, notes),
            Location = TrimOrNull(schedule.Location) ?? activity.DefaultLocation,
            StartAt = schedule.StartAt.ToUniversalTime(),
            EndAt = schedule.EndAt.ToUniversalTime(),
            IsAllDay = schedule.IsAllDay,
            Timezone = uniCalendar.Timezone,
            Status = "confirmed",
            Source = UniversityConstants.CalendarEventSource,
            CreatedAt = now,
            UpdatedAt = now,
        };

    private static string BuildCalendarEventTitle(UniversityCourse course, string itemTitle)
    {
        var prefix = !string.IsNullOrWhiteSpace(course.Code) ? course.Code : course.Name;
        var combined = $"{prefix} · {itemTitle}";
        return combined.Length <= 200 ? combined : combined[..200];
    }

    private static string? BuildCalendarEventDescription(
        UniversityCourseActivity activity,
        string? theme,
        string? notes)
    {
        var sb = new StringBuilder();
        var activityLabel = activity.Title ?? UniversityConstants.GetDefaultActivityLabel(activity.ActivityType);
        sb.Append(activityLabel);

        if (!string.IsNullOrWhiteSpace(theme))
        {
            sb.Append("\nTheme: ");
            sb.Append(theme);
        }

        if (!string.IsNullOrWhiteSpace(notes))
        {
            sb.Append("\nNotes: ");
            sb.Append(notes);
        }

        return sb.ToString();
    }

    private static string? TrimOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static UniversityCourseDto ToCourseDto(UniversityCourse c)
    {
        var orderedActivities = c.Activities
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.CreatedAt)
            .Select(ToActivityDto)
            .ToList();

        return new UniversityCourseDto(
            c.Id,
            c.Name,
            c.Code,
            c.Semester,
            c.Description,
            c.Instructor,
            c.Credits,
            c.Color,
            c.Status,
            c.MetadataJson,
            orderedActivities,
            c.CreatedAt,
            c.UpdatedAt);
    }

    private static UniversityCourseActivityDto ToActivityDto(UniversityCourseActivity a)
    {
        var orderedItems = a.Items
            .OrderBy(i => i.CalendarEvent is null ? 1 : 0)
            .ThenBy(i => i.CalendarEvent?.StartAt ?? DateTimeOffset.MaxValue)
            .ThenBy(i => i.SequenceNumber ?? int.MaxValue)
            .ThenBy(i => i.CreatedAt)
            .Select(ToItemDto)
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
            orderedItems,
            a.CreatedAt,
            a.UpdatedAt);
    }

    private static UniversityActivityItemDto ToItemDto(UniversityActivityItem i) => new(
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
        i.UpdatedAt);
}
