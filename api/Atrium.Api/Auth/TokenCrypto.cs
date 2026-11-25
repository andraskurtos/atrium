using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Atrium.Api.Auth;

public static partial class TokenCrypto
{
    [GeneratedRegex("^[a-z0-9._-]{2,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernameRegex();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex("[a-z]", RegexOptions.CultureInvariant)]
    private static partial Regex LowercaseRegex();

    [GeneratedRegex("[A-Z]", RegexOptions.CultureInvariant)]
    private static partial Regex UppercaseRegex();

    [GeneratedRegex(@"[0-9!@#$%^&*()_+\-=\[\]{};':""\\|,.<>/?]", RegexOptions.CultureInvariant)]
    private static partial Regex DigitOrSymbolRegex();

    public static string GenerateOpaqueToken(int byteCount = 32)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static (string Prefix, string FullToken) GeneratePrefixedToken(string prefixTag)
    {
        var prefixHex = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        var prefix = $"{prefixTag}{prefixHex}";
        var secret = GenerateOpaqueToken(32);
        return (prefix, $"{prefix}_{secret}");
    }

    public static string Sha256Hex(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash);
    }

    public static string NormalizeUsername(string? raw) =>
        (raw ?? string.Empty).Trim().TrimStart('@').ToLowerInvariant();

    public static string NormalizeEmail(string? raw) =>
        (raw ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValidUsername(string normalizedUsername) =>
        UsernameRegex().IsMatch(normalizedUsername);

    public static bool IsValidEmail(string normalizedEmail) =>
        normalizedEmail.Length <= 254 && EmailRegex().IsMatch(normalizedEmail);

    public static string? ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 12)
        {
            return "Password must be at least 12 characters long.";
        }

        if (!LowercaseRegex().IsMatch(password)
            || !UppercaseRegex().IsMatch(password)
            || !DigitOrSymbolRegex().IsMatch(password))
        {
            return "Password must include uppercase, lowercase, and a number or symbol.";
        }

        return null;
    }

    public static string ParseDeviceLabel(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "Unknown device";
        }

        var browser = userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge"
            : userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox"
            : userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Chrome"
            : userAgent.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari"
            : "Browser";

        var os = userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
                 || userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iOS"
            : userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android"
            : userAgent.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase) ? "macOS"
            : userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows"
            : userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux"
            : "Desktop";

        return $"{browser} on {os}";
    }

    public static string GetClientIp(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Connection.RemoteIpAddress?.ToString() ?? "local";
    }
}
