using System.Security.Claims;
using Atrium.Api.Auth;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Atrium.Api.Endpoints;

public static class AccountEndpoints
{
    private static readonly HashSet<string> ValidThemes = new(StringComparer.Ordinal) { "system", "light", "dark" };
    private static readonly HashSet<string> ValidWeekStarts = new(StringComparer.Ordinal) { "monday", "sunday" };
    private static readonly HashSet<string> ValidTimeFormats = new(StringComparer.Ordinal) { "24h", "12h" };

    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/account")
            .RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapGet("/me", async (ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var user = await db.Users
                .Include(u => u.Preferences)
                .FirstOrDefaultAsync(u => u.Id == userId.Value, ct);

            if (user is null || !user.IsActive) return Results.Unauthorized();

            var prefs = user.Preferences ?? new UserPreferences { UserId = user.Id };
            return Results.Ok(new AccountMeResponse(
                AuthMappings.ToUserDto(user),
                AuthMappings.ToPreferencesDto(prefs)));
        });

        group.MapPut("/profile", async (
            UpdateProfileRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
            if (user is null || !user.IsActive) return Results.Unauthorized();

            var username = TokenCrypto.NormalizeUsername(request.Username);
            var email = TokenCrypto.NormalizeEmail(request.Email);
            var displayName = (request.DisplayName ?? string.Empty).Trim();
            var bio = (request.Bio ?? string.Empty).Trim();
            var timezone = string.IsNullOrWhiteSpace(request.Timezone) ? "UTC" : request.Timezone.Trim();

            if (!TokenCrypto.IsValidUsername(username))
            {
                return Results.BadRequest(new { error = "Username must be 2–32 characters using lowercase letters, digits, dots, underscores, or hyphens." });
            }

            if (!TokenCrypto.IsValidEmail(email))
            {
                return Results.BadRequest(new { error = "Please enter a valid email address." });
            }

            if (displayName.Length > 50 || bio.Length > 120 || timezone.Length > 64)
            {
                return Results.BadRequest(new { error = "One or more profile fields exceed maximum length." });
            }

            if (request.AvatarDataUrl is not null)
            {
                if (request.AvatarDataUrl.Length > AuthConstants.MaxAvatarDataUrlChars)
                {
                    return Results.BadRequest(new { error = "Profile image must be 2 MB or smaller." });
                }

                if (!request.AvatarDataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new { error = "Profile image must be a valid image data URL." });
                }
            }

            var conflict = await db.Users.AnyAsync(
                u => u.Id != user.Id && (u.Username == username || u.Email == email),
                ct);
            if (conflict)
            {
                return Results.Conflict(new { error = "That username or email address is already in use." });
            }

            user.DisplayName = string.IsNullOrEmpty(displayName) ? username : displayName;
            user.Username = username;
            user.Email = email;
            user.Bio = bio;
            user.Timezone = timezone;
            user.AvatarDataUrl = request.AvatarDataUrl;
            user.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            return Results.Ok(AuthMappings.ToUserDto(user));
        });

        group.MapPut("/preferences", async (
            UpdatePreferencesRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var user = await db.Users
                .Include(u => u.Preferences)
                .FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
            if (user is null || !user.IsActive) return Results.Unauthorized();

            if (!ValidThemes.Contains(request.Theme)
                || !ValidWeekStarts.Contains(request.WeekStartsOn)
                || !ValidTimeFormats.Contains(request.TimeFormat))
            {
                return Results.BadRequest(new { error = "Invalid preference value." });
            }

            var prefs = user.Preferences;
            if (prefs is null)
            {
                prefs = new UserPreferences { UserId = user.Id };
                db.UserPreferences.Add(prefs);
            }

            prefs.Theme = request.Theme;
            prefs.WeekStartsOn = request.WeekStartsOn;
            prefs.TimeFormat = request.TimeFormat;
            prefs.RequireAgentApproval = request.RequireAgentApproval;
            prefs.MorningBrief = request.MorningBrief;
            prefs.EventReminders = request.EventReminders;

            await db.SaveChangesAsync(ct);
            return Results.Ok(AuthMappings.ToPreferencesDto(prefs));
        });

        group.MapPost("/password", async (
            ChangePasswordRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            IPasswordHasher<User> passwordHasher,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
            if (user is null || !user.IsActive) return Results.Unauthorized();

            if (string.IsNullOrEmpty(request.CurrentPassword))
            {
                return Results.BadRequest(new { error = "Current password is required." });
            }

            var verify = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
            if (verify == PasswordVerificationResult.Failed)
            {
                return Results.Json(new { error = "Current password is incorrect." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var passwordError = TokenCrypto.ValidatePassword(request.NewPassword);
            if (passwordError is not null)
            {
                return Results.BadRequest(new { error = passwordError });
            }

            var now = DateTimeOffset.UtcNow;
            user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
            user.PasswordUpdatedAt = now;
            user.UpdatedAt = now;

            var currentSid = principal.GetSessionId();
            var otherSessions = await db.RefreshSessions
                .Where(s => s.UserId == user.Id && s.RevokedAt == null && (currentSid == null || s.Id != currentSid.Value))
                .ToListAsync(ct);

            foreach (var s in otherSessions)
            {
                s.RevokedAt = now;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { passwordUpdatedAt = user.PasswordUpdatedAt });
        });

        group.MapGet("/sessions", async (ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var currentSid = principal.GetSessionId();
            var now = DateTimeOffset.UtcNow;

            var rows = await db.RefreshSessions
                .Where(s => s.UserId == userId.Value && s.RevokedAt == null)
                .ToListAsync(ct);

            var active = rows
                .Where(s => s.ExpiresAt > now)
                .OrderByDescending(s => s.LastActiveAt)
                .Select(s => new SessionDto(
                    s.Id,
                    s.DeviceLabel,
                    s.IpAddress,
                    s.LastActiveAt,
                    s.ExpiresAt,
                    currentSid is not null && s.Id == currentSid.Value))
                .ToList();

            return Results.Ok(active);
        });

        group.MapDelete("/sessions/{id:guid}", async (Guid id, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var session = await db.RefreshSessions
                .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId.Value, ct);

            if (session is null) return Results.NotFound();

            if (session.RevokedAt is null)
            {
                session.RevokedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            return Results.NoContent();
        });

        group.MapPost("/sessions/revoke-others", async (ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var currentSid = principal.GetSessionId();
            var now = DateTimeOffset.UtcNow;

            var others = await db.RefreshSessions
                .Where(s => s.UserId == userId.Value && s.RevokedAt == null && (currentSid == null || s.Id != currentSid.Value))
                .ToListAsync(ct);

            foreach (var s in others)
            {
                s.RevokedAt = now;
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
