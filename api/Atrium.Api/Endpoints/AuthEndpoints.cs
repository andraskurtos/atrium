using Atrium.Api.Auth;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Atrium.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .RequireRateLimiting(AuthConstants.AuthRateLimitPolicy);

        group.MapGet("/status", async (AppDbContext db, CancellationToken ct) =>
        {
            var initialized = await db.Users.AnyAsync(ct);
            return Results.Ok(new AuthStatusResponse(initialized));
        });

        group.MapPost("/setup", async (
            SetupRequest request,
            HttpContext httpContext,
            AppDbContext db,
            IPasswordHasher<User> passwordHasher,
            JwtTokenService jwtService,
            IOptions<AuthOptions> authOptions,
            CancellationToken ct) =>
        {
            var validationError = ValidateRegistrationFields(
                request.Username,
                request.Email,
                request.DisplayName,
                request.Password,
                out var username,
                out var email,
                out var displayName);

            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            if (await db.Users.AnyAsync(ct))
            {
                return Results.Conflict(new { error = "Atrium is already initialized. Sign in or use an invite." });
            }

            var now = DateTimeOffset.UtcNow;
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                Email = email,
                DisplayName = displayName,
                Bio = "Workspace owner",
                Timezone = "UTC",
                Role = AuthConstants.OwnerRole,
                IsActive = true,
                PasswordUpdatedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            };
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

            var prefs = CreateDefaultPreferences(user.Id);
            db.Users.Add(user);
            db.UserPreferences.Add(prefs);

            var (session, rawRefresh) = CreateRefreshSession(user.Id, httpContext, now, authOptions.Value);
            db.RefreshSessions.Add(session);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                return Results.Conflict(new { error = "Atrium is already initialized." });
            }

            var (accessToken, accessExpiresAt) = jwtService.CreateAccessToken(user, session.Id);
            RefreshCookieHelper.AppendRefreshCookie(httpContext, rawRefresh, session.ExpiresAt);

            return Results.Ok(new AuthSessionResponse(
                accessToken,
                accessExpiresAt,
                AuthMappings.ToUserDto(user),
                AuthMappings.ToPreferencesDto(prefs)));
        });

        group.MapPost("/login", async (
            LoginRequest request,
            HttpContext httpContext,
            AppDbContext db,
            IPasswordHasher<User> passwordHasher,
            JwtTokenService jwtService,
            IOptions<AuthOptions> authOptions,
            CancellationToken ct) =>
        {
            var identifier = (request.Identifier ?? string.Empty).Trim().TrimStart('@').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrEmpty(request.Password))
            {
                return Results.Json(new { error = "Invalid credentials." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = await db.Users
                .Include(u => u.Preferences)
                .FirstOrDefaultAsync(u => u.Username == identifier || u.Email == identifier, ct);

            if (user is null || !user.IsActive)
            {
                return Results.Json(new { error = "Invalid credentials." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var verify = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (verify == PasswordVerificationResult.Failed)
            {
                return Results.Json(new { error = "Invalid credentials." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var now = DateTimeOffset.UtcNow;
            if (verify == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
                user.UpdatedAt = now;
            }

            var prefs = user.Preferences;
            if (prefs is null)
            {
                prefs = CreateDefaultPreferences(user.Id);
                db.UserPreferences.Add(prefs);
            }

            var (session, rawRefresh) = CreateRefreshSession(user.Id, httpContext, now, authOptions.Value);
            db.RefreshSessions.Add(session);
            await db.SaveChangesAsync(ct);

            var (accessToken, accessExpiresAt) = jwtService.CreateAccessToken(user, session.Id);
            RefreshCookieHelper.AppendRefreshCookie(httpContext, rawRefresh, session.ExpiresAt);

            return Results.Ok(new AuthSessionResponse(
                accessToken,
                accessExpiresAt,
                AuthMappings.ToUserDto(user),
                AuthMappings.ToPreferencesDto(prefs)));
        });

        group.MapPost("/register", async (
            RegisterWithInviteRequest request,
            HttpContext httpContext,
            AppDbContext db,
            IPasswordHasher<User> passwordHasher,
            JwtTokenService jwtService,
            IOptions<AuthOptions> authOptions,
            CancellationToken ct) =>
        {
            var rawInvite = (request.InviteCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rawInvite))
            {
                return Results.BadRequest(new { error = "An invite code is required to register." });
            }

            var validationError = ValidateRegistrationFields(
                request.Username,
                request.Email,
                request.DisplayName,
                request.Password,
                out var username,
                out var email,
                out var displayName);

            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            var now = DateTimeOffset.UtcNow;
            var inviteHash = TokenCrypto.Sha256Hex(rawInvite);
            var invite = await db.Invites.FirstOrDefaultAsync(i => i.CodeHash == inviteHash, ct);
            if (invite is null || invite.RevokedAt is not null || invite.UsedAt is not null || invite.ExpiresAt <= now)
            {
                return Results.BadRequest(new { error = "This invite code is invalid, expired, or has already been used." });
            }

            var conflict = await db.Users.AnyAsync(u => u.Username == username || u.Email == email, ct);
            if (conflict)
            {
                return Results.Conflict(new { error = "That username or email address is already in use." });
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                Email = email,
                DisplayName = displayName,
                Bio = "Household member",
                Timezone = "UTC",
                Role = AuthConstants.MemberRole,
                IsActive = true,
                PasswordUpdatedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            };
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

            var prefs = CreateDefaultPreferences(user.Id);
            invite.UsedAt = now;
            invite.UsedByUserId = user.Id;

            db.Users.Add(user);
            db.UserPreferences.Add(prefs);

            var (session, rawRefresh) = CreateRefreshSession(user.Id, httpContext, now, authOptions.Value);
            db.RefreshSessions.Add(session);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                return Results.Conflict(new { error = "Username, email, or invite code could not be claimed." });
            }

            var (accessToken, accessExpiresAt) = jwtService.CreateAccessToken(user, session.Id);
            RefreshCookieHelper.AppendRefreshCookie(httpContext, rawRefresh, session.ExpiresAt);

            return Results.Ok(new AuthSessionResponse(
                accessToken,
                accessExpiresAt,
                AuthMappings.ToUserDto(user),
                AuthMappings.ToPreferencesDto(prefs)));
        });

        group.MapPost("/refresh", async (
            HttpContext httpContext,
            AppDbContext db,
            JwtTokenService jwtService,
            IOptions<AuthOptions> authOptions,
            CancellationToken ct) =>
        {
            if (!RefreshCookieHelper.HasValidCsrfHeader(httpContext.Request))
            {
                return Results.Json(new { error = "Missing CSRF header." }, statusCode: StatusCodes.Status403Forbidden);
            }

            if (!httpContext.Request.Cookies.TryGetValue(AuthConstants.RefreshCookieName, out var rawToken)
                || string.IsNullOrWhiteSpace(rawToken))
            {
                return Results.Json(new { error = "No active session." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var now = DateTimeOffset.UtcNow;
            var tokenHash = TokenCrypto.Sha256Hex(rawToken);
            var existing = await db.RefreshSessions
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct);

            if (existing is null)
            {
                RefreshCookieHelper.ClearRefreshCookie(httpContext);
                return Results.Json(new { error = "Invalid session." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            // Reuse detection: presenting an already-rotated/revoked token compromises the whole family.
            if (existing.RevokedAt is not null)
            {
                var familySessions = await db.RefreshSessions
                    .Where(s => s.FamilyId == existing.FamilyId && s.RevokedAt == null)
                    .ToListAsync(ct);

                foreach (var active in familySessions)
                {
                    active.RevokedAt = now;
                }

                await db.SaveChangesAsync(ct);
                RefreshCookieHelper.ClearRefreshCookie(httpContext);
                return Results.Json(new { error = "Session invalidated due to token reuse." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (existing.ExpiresAt <= now || !existing.User.IsActive)
            {
                existing.RevokedAt = now;
                await db.SaveChangesAsync(ct);
                RefreshCookieHelper.ClearRefreshCookie(httpContext);
                return Results.Json(new { error = "Session expired or account inactive." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var opts = authOptions.Value;
            var slidingExpiry = now.AddDays(opts.RefreshSlidingDays);
            var absoluteExpiry = existing.FamilyCreatedAt.AddDays(opts.RefreshAbsoluteDays);
            var nextExpiry = slidingExpiry < absoluteExpiry ? slidingExpiry : absoluteExpiry;

            var nextRawToken = TokenCrypto.GenerateOpaqueToken(32);
            var nextSession = new RefreshSession
            {
                Id = Guid.NewGuid(),
                UserId = existing.UserId,
                FamilyId = existing.FamilyId,
                FamilyCreatedAt = existing.FamilyCreatedAt,
                TokenHash = TokenCrypto.Sha256Hex(nextRawToken),
                DeviceLabel = TokenCrypto.ParseDeviceLabel(httpContext.Request.Headers.UserAgent.ToString()),
                IpAddress = TokenCrypto.GetClientIp(httpContext),
                CreatedAt = now,
                LastActiveAt = now,
                ExpiresAt = nextExpiry,
            };

            existing.RevokedAt = now;
            existing.ReplacedBySessionId = nextSession.Id;

            db.RefreshSessions.Add(nextSession);
            await db.SaveChangesAsync(ct);

            var (accessToken, accessExpiresAt) = jwtService.CreateAccessToken(existing.User, nextSession.Id);
            RefreshCookieHelper.AppendRefreshCookie(httpContext, nextRawToken, nextSession.ExpiresAt);

            return Results.Ok(new RefreshResponse(accessToken, accessExpiresAt));
        });

        group.MapPost("/logout", async (
            HttpContext httpContext,
            AppDbContext db,
            CancellationToken ct) =>
        {
            if (!RefreshCookieHelper.HasValidCsrfHeader(httpContext.Request))
            {
                return Results.Json(new { error = "Missing CSRF header." }, statusCode: StatusCodes.Status403Forbidden);
            }

            if (httpContext.Request.Cookies.TryGetValue(AuthConstants.RefreshCookieName, out var rawToken)
                && !string.IsNullOrWhiteSpace(rawToken))
            {
                var tokenHash = TokenCrypto.Sha256Hex(rawToken);
                var session = await db.RefreshSessions.FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct);
                if (session is not null && session.RevokedAt is null)
                {
                    session.RevokedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }

            RefreshCookieHelper.ClearRefreshCookie(httpContext);
            return Results.NoContent();
        });
    }

    private static string? ValidateRegistrationFields(
        string? rawUsername,
        string? rawEmail,
        string? rawDisplayName,
        string? password,
        out string username,
        out string email,
        out string displayName)
    {
        username = TokenCrypto.NormalizeUsername(rawUsername);
        email = TokenCrypto.NormalizeEmail(rawEmail);
        displayName = (rawDisplayName ?? string.Empty).Trim();

        if (!TokenCrypto.IsValidUsername(username))
        {
            return "Username must be 2–32 characters using lowercase letters, digits, dots, underscores, or hyphens.";
        }

        if (!TokenCrypto.IsValidEmail(email))
        {
            return "Please enter a valid email address.";
        }

        if (displayName.Length > 50)
        {
            return "Display name must be 50 characters or fewer.";
        }

        if (string.IsNullOrEmpty(displayName))
        {
            displayName = username;
        }

        return TokenCrypto.ValidatePassword(password);
    }

    private static UserPreferences CreateDefaultPreferences(Guid userId) =>
        new()
        {
            UserId = userId,
            Theme = "system",
            WeekStartsOn = "monday",
            TimeFormat = "24h",
            RequireAgentApproval = true,
            MorningBrief = true,
            EventReminders = true,
        };

    private static (RefreshSession Session, string RawToken) CreateRefreshSession(
        Guid userId,
        HttpContext httpContext,
        DateTimeOffset now,
        AuthOptions opts)
    {
        var rawToken = TokenCrypto.GenerateOpaqueToken(32);
        var session = new RefreshSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = Guid.NewGuid(),
            FamilyCreatedAt = now,
            TokenHash = TokenCrypto.Sha256Hex(rawToken),
            DeviceLabel = TokenCrypto.ParseDeviceLabel(httpContext.Request.Headers.UserAgent.ToString()),
            IpAddress = TokenCrypto.GetClientIp(httpContext),
            CreatedAt = now,
            LastActiveAt = now,
            ExpiresAt = now.AddDays(opts.RefreshSlidingDays),
        };
        return (session, rawToken);
    }
}
