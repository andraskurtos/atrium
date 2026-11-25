using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Atrium.Api.Auth;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atrium.Api.Endpoints;

public static class TokenEndpoints
{
    public static void MapTokenEndpoints(this IEndpointRouteBuilder app)
    {
        var accountGroup = app.MapGroup("/api/account/tokens")
            .RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        accountGroup.MapGet("/", async (ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var tokens = await db.AgentTokens
                .Where(t => t.UserId == userId.Value && t.RevokedAt == null)
                .ToListAsync(ct);

            var ordered = tokens
                .OrderByDescending(t => t.CreatedAt)
                .Select(AuthMappings.ToAgentTokenDto)
                .ToList();

            return Results.Ok(ordered);
        });

        accountGroup.MapPost("/", async (
            CreateAgentTokenRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
            if (user is null || !user.IsActive) return Results.Unauthorized();

            var name = (request.Name ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 80)
            {
                return Results.BadRequest(new { error = "Token label must be 1–80 characters." });
            }

            var normalizedScopes = (request.Scopes ?? [])
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (normalizedScopes.Count == 0 || normalizedScopes.Any(s => !AuthConstants.AllowedScopes.Contains(s)))
            {
                return Results.BadRequest(new { error = "Select at least one valid MCP scope." });
            }

            var (prefix, fullSecret) = TokenCrypto.GeneratePrefixedToken(AuthConstants.PatPrefixTag);
            var token = new AgentToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Name = name,
                Prefix = prefix,
                TokenHash = TokenCrypto.Sha256Hex(fullSecret),
                Scopes = string.Join(',', normalizedScopes),
                CreatedAt = DateTimeOffset.UtcNow,
            };

            db.AgentTokens.Add(token);
            await db.SaveChangesAsync(ct);

            var response = new CreateAgentTokenResponse(AuthMappings.ToAgentTokenDto(token), fullSecret);
            return Results.Created($"/api/account/tokens/{token.Id}", response);
        });

        accountGroup.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var token = await db.AgentTokens
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId.Value, ct);
            if (token is null) return Results.NotFound();

            if (token.RevokedAt is null)
            {
                token.RevokedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            return Results.NoContent();
        });

        app.MapGet("/api/agent/whoami", (ClaimsPrincipal principal) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var username = principal.FindFirstValue(JwtRegisteredClaimNames.UniqueName)
                           ?? principal.Identity?.Name
                           ?? string.Empty;
            var authType = principal.FindFirstValue(AuthConstants.AuthTypeClaim) ?? string.Empty;
            var scopes = principal.FindAll(AuthConstants.ScopeClaim).Select(c => c.Value).ToList();

            return Results.Ok(new AgentWhoAmIResponse(userId.Value, username, authType, scopes));
        }).RequireAuthorization(AuthConstants.HumanOrAgentPolicy);
    }
}
