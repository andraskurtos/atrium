using System.Security.Claims;
using Atrium.Api.Auth;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atrium.Api.Endpoints;

public static class HouseholdEndpoints
{
    public static void MapHouseholdEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/account")
            .RequireAuthorization(AuthConstants.OwnerOnlyPolicy);

        group.MapGet("/invites", async (AppDbContext db, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var rows = await db.Invites.ToListAsync(ct);
            var dtos = rows
                .OrderByDescending(i => i.CreatedAt)
                .Select(i => AuthMappings.ToInviteDto(i, now))
                .ToList();
            return Results.Ok(dtos);
        });

        group.MapPost("/invites", async (
            CreateInviteRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var ownerId = principal.GetUserId();
            if (ownerId is null) return Results.Unauthorized();

            var expiresInDays = request.ExpiresInDays ?? 7;
            if (expiresInDays < 1 || expiresInDays > 30)
            {
                return Results.BadRequest(new { error = "Invite expiration must be between 1 and 30 days." });
            }

            var label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim();
            if (label is not null && label.Length > 80)
            {
                return Results.BadRequest(new { error = "Invite label must be 80 characters or fewer." });
            }

            var now = DateTimeOffset.UtcNow;
            var (prefix, code) = TokenCrypto.GeneratePrefixedToken(AuthConstants.InvitePrefixTag);
            var invite = new Invite
            {
                Id = Guid.NewGuid(),
                CreatedByUserId = ownerId.Value,
                CodePrefix = prefix,
                CodeHash = TokenCrypto.Sha256Hex(code),
                Label = label,
                CreatedAt = now,
                ExpiresAt = now.AddDays(expiresInDays),
            };

            db.Invites.Add(invite);
            await db.SaveChangesAsync(ct);

            var response = new CreateInviteResponse(
                AuthMappings.ToInviteDto(invite, now),
                code,
                $"/#invite={code}");

            return Results.Created($"/api/account/invites/{invite.Id}", response);
        });

        group.MapDelete("/invites/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var invite = await db.Invites.FirstOrDefaultAsync(i => i.Id == id, ct);
            if (invite is null) return Results.NotFound();

            if (invite.UsedAt is not null)
            {
                return Results.Conflict(new { error = "Invite has already been used." });
            }

            if (invite.RevokedAt is null)
            {
                invite.RevokedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            return Results.NoContent();
        });

        group.MapGet("/members", async (AppDbContext db, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var users = await db.Users
                .Include(u => u.RefreshSessions)
                .Include(u => u.AgentTokens)
                .ToListAsync(ct);

            var members = users
                .OrderByDescending(u => u.Role == AuthConstants.OwnerRole)
                .ThenBy(u => u.CreatedAt)
                .Select(u =>
                {
                    var activeSessions = u.RefreshSessions
                        .Where(s => s.RevokedAt == null && s.ExpiresAt > now)
                        .ToList();
                    var activeTokens = u.AgentTokens
                        .Count(t => t.RevokedAt == null);
                    var lastActive = activeSessions.Count > 0
                        ? activeSessions.Max(s => s.LastActiveAt)
                        : (DateTimeOffset?)null;

                    return new HouseholdMemberDto(
                        u.Id,
                        u.Username,
                        u.DisplayName,
                        u.Email,
                        u.Role,
                        u.IsActive,
                        u.CreatedAt,
                        lastActive,
                        activeSessions.Count,
                        activeTokens);
                })
                .ToList();

            return Results.Ok(members);
        });

        group.MapPatch("/members/{id:guid}", async (
            Guid id,
            UpdateMemberStatusRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var ownerId = principal.GetUserId();
            if (ownerId is null) return Results.Unauthorized();

            var target = await db.Users
                .Include(u => u.RefreshSessions)
                .Include(u => u.AgentTokens)
                .FirstOrDefaultAsync(u => u.Id == id, ct);

            if (target is null) return Results.NotFound();

            if (target.Id == ownerId.Value || target.Role == AuthConstants.OwnerRole)
            {
                return Results.BadRequest(new { error = "The workspace Owner account cannot be deactivated." });
            }

            var now = DateTimeOffset.UtcNow;
            target.IsActive = request.IsActive;
            target.UpdatedAt = now;

            if (!request.IsActive)
            {
                foreach (var session in target.RefreshSessions.Where(s => s.RevokedAt == null))
                {
                    session.RevokedAt = now;
                }

                foreach (var token in target.AgentTokens.Where(t => t.RevokedAt == null))
                {
                    token.RevokedAt = now;
                }
            }

            await db.SaveChangesAsync(ct);

            var activeSessions = target.RefreshSessions.Count(s => s.RevokedAt == null && s.ExpiresAt > now);
            var activeTokens = target.AgentTokens.Count(t => t.RevokedAt == null);

            return Results.Ok(new HouseholdMemberDto(
                target.Id,
                target.Username,
                target.DisplayName,
                target.Email,
                target.Role,
                target.IsActive,
                target.CreatedAt,
                null,
                activeSessions,
                activeTokens));
        });

        group.MapDelete("/members/{id:guid}", async (Guid id, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var ownerId = principal.GetUserId();
            if (ownerId is null) return Results.Unauthorized();

            var target = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (target is null) return Results.NotFound();

            if (target.Id == ownerId.Value || target.Role == AuthConstants.OwnerRole)
            {
                return Results.BadRequest(new { error = "The workspace Owner account cannot be deleted." });
            }

            db.Users.Remove(target);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
