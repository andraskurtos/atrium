using System.Text;

namespace Atrium.Api.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Atrium:Auth";
    public const string DevJwtSigningKeySentinel = "atrium-dev-only-jwt-signing-key-do-not-use-in-production";

    public string JwtSigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "atrium";
    public string Audience { get; set; } = "atrium-client";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshSlidingDays { get; set; } = 30;
    public int RefreshAbsoluteDays { get; set; } = 90;

    public void ValidateForEnvironment(bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(JwtSigningKey))
        {
            throw new InvalidOperationException(
                "Missing required JWT signing key 'Atrium:Auth:JwtSigningKey' (env: ATRIUM_JWT_SECRET / Atrium__Auth__JwtSigningKey).");
        }

        if (Encoding.UTF8.GetByteCount(JwtSigningKey) < 32)
        {
            throw new InvalidOperationException(
                "JWT signing key 'Atrium:Auth:JwtSigningKey' must be at least 32 bytes long.");
        }

        if (!isDevelopment && string.Equals(JwtSigningKey, DevJwtSigningKeySentinel, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "JWT signing key 'Atrium:Auth:JwtSigningKey' cannot use the development placeholder in Production.");
        }
    }
}
