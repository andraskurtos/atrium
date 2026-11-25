using System.Net;
using System.Net.Http.Json;
using Atrium.Api.Auth;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class AuthFlowTests
{
    private const string StrongPassword = "Atrium-Owner-Pass-2026!";

    [Fact]
    public async Task Status_ReturnsUninitialized_OnEmptyDatabase()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuthStatusResponse>();
        Assert.NotNull(payload);
        Assert.False(payload.Initialized);
    }

    [Fact]
    public async Task Setup_CreatesSoleOwner_AndBlocksSecondSetupWith409()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync(
            "/api/auth/setup",
            new SetupRequest("andras", "andras@atrium.local", "András", StrongPassword));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var session = await first.Content.ReadFromJsonAsync<AuthSessionResponse>();
        Assert.NotNull(session);
        Assert.False(string.IsNullOrWhiteSpace(session.AccessToken));
        Assert.Equal("Owner", session.User.Role);
        Assert.Equal("andras", session.User.Username);

        var status = await client.GetFromJsonAsync<AuthStatusResponse>("/api/auth/status");
        Assert.NotNull(status);
        Assert.True(status.Initialized);

        var second = await client.PostAsJsonAsync(
            "/api/auth/setup",
            new SetupRequest("intruder", "intruder@atrium.local", "Intruder", StrongPassword));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Setup_RejectsWeakPassword()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/setup",
            new SetupRequest("andras", "andras@atrium.local", "András", "short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_SucceedsWithUsernameOrEmail_AndRejectsInvalidCredentials()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();

        var setup = await client.PostAsJsonAsync(
            "/api/auth/setup",
            new SetupRequest("andras", "andras@atrium.local", "András", StrongPassword));
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);

        var byUsername = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("andras", StrongPassword));
        Assert.Equal(HttpStatusCode.OK, byUsername.StatusCode);

        var byEmail = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("ANDRAS@atrium.local", StrongPassword));
        Assert.Equal(HttpStatusCode.OK, byEmail.StatusCode);

        var badPass = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("andras", "WrongPassword123!"));
        Assert.Equal(HttpStatusCode.Unauthorized, badPass.StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndRevokesEntireFamilyOnReplayReuse()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });

        var setup = await client.PostAsJsonAsync(
            "/api/auth/setup",
            new SetupRequest("andras", "andras@atrium.local", "András", StrongPassword));
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);

        var cookie1 = ExtractRefreshCookie(setup);
        Assert.False(string.IsNullOrWhiteSpace(cookie1));

        // First refresh rotates token1 -> token2
        using var refreshReq1 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshReq1.Headers.Add("Cookie", cookie1);
        refreshReq1.Headers.Add(AuthConstants.CsrfHeaderName, AuthConstants.CsrfHeaderValue);
        var refreshRes1 = await client.SendAsync(refreshReq1);
        Assert.Equal(HttpStatusCode.OK, refreshRes1.StatusCode);
        var cookie2 = ExtractRefreshCookie(refreshRes1);
        Assert.NotEqual(cookie1, cookie2);

        // Replaying old cookie1 triggers reuse detection and revokes the entire family
        using var replayReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        replayReq.Headers.Add("Cookie", cookie1);
        replayReq.Headers.Add(AuthConstants.CsrfHeaderName, AuthConstants.CsrfHeaderValue);
        var replayRes = await client.SendAsync(replayReq);
        Assert.Equal(HttpStatusCode.Unauthorized, replayRes.StatusCode);

        // Legitimate cookie2 is now also revoked
        using var afterReuseReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        afterReuseReq.Headers.Add("Cookie", cookie2);
        afterReuseReq.Headers.Add(AuthConstants.CsrfHeaderName, AuthConstants.CsrfHeaderValue);
        var afterReuseRes = await client.SendAsync(afterReuseReq);
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuseRes.StatusCode);
    }

    [Fact]
    public async Task Refresh_AndLogout_RequireCsrfHeader()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();

        var refreshRes = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Forbidden, refreshRes.StatusCode);

        var logoutRes = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.Forbidden, logoutRes.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesSession_AndPreventsFurtherRefresh()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });

        var setup = await client.PostAsJsonAsync(
            "/api/auth/setup",
            new SetupRequest("andras", "andras@atrium.local", "András", StrongPassword));
        var cookie = ExtractRefreshCookie(setup);

        using var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutReq.Headers.Add("Cookie", cookie);
        logoutReq.Headers.Add(AuthConstants.CsrfHeaderName, AuthConstants.CsrfHeaderValue);
        var logoutRes = await client.SendAsync(logoutReq);
        Assert.Equal(HttpStatusCode.NoContent, logoutRes.StatusCode);

        using var refreshReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshReq.Headers.Add("Cookie", cookie);
        refreshReq.Headers.Add(AuthConstants.CsrfHeaderName, AuthConstants.CsrfHeaderValue);
        var refreshRes = await client.SendAsync(refreshReq);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshRes.StatusCode);
    }

    public static string ExtractRefreshCookie(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers))
        {
            return string.Empty;
        }

        foreach (var header in headers)
        {
            if (header.StartsWith($"{AuthConstants.RefreshCookieName}=", StringComparison.Ordinal))
            {
                return header.Split(';', 2)[0];
            }
        }

        return string.Empty;
    }
}
