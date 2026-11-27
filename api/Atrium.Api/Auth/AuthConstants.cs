namespace Atrium.Api.Auth;

public static class AuthConstants
{
    public const string CompositeScheme = "AtriumComposite";
    public const string JwtScheme = "AtriumJwt";
    public const string PatScheme = "AtriumPat";

    public const string HumanJwtOnlyPolicy = "HumanJwtOnly";
    public const string OwnerOnlyPolicy = "OwnerOnly";
    public const string HumanOrAgentPolicy = "HumanOrAgent";

    public const string AuthRateLimitPolicy = "AuthRateLimit";

    public const string OwnerRole = "Owner";
    public const string MemberRole = "Member";

    public const string AuthTypeClaim = "auth_type";
    public const string ScopeClaim = "scope";
    public const string JwtAuthType = "jwt";
    public const string PatAuthType = "pat";

    public const string RefreshCookieName = "atrium_refresh";
    public const string CsrfHeaderName = "X-Atrium-CSRF";
    public const string CsrfHeaderValue = "1";

    public const string PatPrefixTag = "atr_";
    public const string InvitePrefixTag = "inv_";

    public const int MaxAvatarDataUrlChars = 2_800_000; // ~2 MB binary image base64-encoded

    public static readonly IReadOnlySet<string> AllowedScopes = new HashSet<string>(StringComparer.Ordinal)
    {
        "calendar:read",
        "calendar:propose",
        "tasks:read",
        "tasks:propose",
        "vault:read",
        "notes:read",
        "university:read",
        "university:propose",
    };
}
