namespace Atrium.Api.Auth;

public static class RefreshCookieHelper
{
    public static void AppendRefreshCookie(HttpContext context, string refreshToken, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.Cookies.Append(
            AuthConstants.RefreshCookieName,
            refreshToken,
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
                Path = "/api/auth",
                Expires = expiresAt,
                IsEssential = true,
            });
    }

    public static void ClearRefreshCookie(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.Cookies.Delete(
            AuthConstants.RefreshCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
                Path = "/api/auth",
            });
    }

    public static bool HasValidCsrfHeader(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Headers.TryGetValue(AuthConstants.CsrfHeaderName, out var values)
            && string.Equals(values.ToString(), AuthConstants.CsrfHeaderValue, StringComparison.Ordinal);
    }
}
