using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Atrium.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Atrium.Api.Auth;

public sealed class PatAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
        {
            return AuthenticateResult.NoResult();
        }

        var header = headerValues.ToString();
        const string bearerPrefix = "Bearer ";
        if (!header.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var rawToken = header[bearerPrefix.Length..].Trim();
        if (!rawToken.StartsWith(AuthConstants.PatPrefixTag, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        var tokenHash = TokenCrypto.Sha256Hex(rawToken);
        var tokenRecord = await db.AgentTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, Context.RequestAborted);

        if (tokenRecord is null || tokenRecord.RevokedAt is not null || !tokenRecord.User.IsActive)
        {
            return AuthenticateResult.Fail("Invalid or revoked agent token.");
        }

        var now = DateTimeOffset.UtcNow;
        if (tokenRecord.LastUsedAt is null || (now - tokenRecord.LastUsedAt.Value) >= TimeSpan.FromMinutes(1))
        {
            tokenRecord.LastUsedAt = now;
            await db.SaveChangesAsync(Context.RequestAborted);
        }

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, tokenRecord.User.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, tokenRecord.User.Username),
            new(ClaimTypes.Role, tokenRecord.User.Role),
            new(AuthConstants.AuthTypeClaim, AuthConstants.PatAuthType),
        };

        var scopes = tokenRecord.Scopes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var scope in scopes)
        {
            claims.Add(new Claim(AuthConstants.ScopeClaim, scope));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }
}
