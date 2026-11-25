using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Atrium.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    public static Guid? GetSessionId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var sid = principal.FindFirstValue(JwtRegisteredClaimNames.Sid);
        return Guid.TryParse(sid, out var id) ? id : null;
    }
}
