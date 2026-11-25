using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Atrium.Api.Auth;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class AccountAndTokensTests
{
    private const string StrongPassword = "Atrium-Owner-Pass-2026!";
    private const string NewStrongPassword = "Atrium-Rotated-Pass-2027!";

    private static async Task<AuthSessionResponse> SetupOwnerAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync(
            "/api/auth/setup",
            new SetupRequest("andras", "andras@atrium.local", "András", StrongPassword));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var session = await res.Content.ReadFromJsonAsync<AuthSessionResponse>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return session;
    }

    [Fact]
    public async Task Profile_AndPreferences_PersistChanges()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var profileRes = await client.PutAsJsonAsync(
            "/api/account/profile",
            new UpdateProfileRequest("András K.", "andras.k", "andras.k@atrium.local", "Builder", "Europe/Budapest", null));
        Assert.Equal(HttpStatusCode.OK, profileRes.StatusCode);

        var prefsRes = await client.PutAsJsonAsync(
            "/api/account/preferences",
            new UpdatePreferencesRequest("dark", "sunday", "12h", false, true, false));
        Assert.Equal(HttpStatusCode.OK, prefsRes.StatusCode);

        var me = await client.GetFromJsonAsync<AccountMeResponse>("/api/account/me");
        Assert.NotNull(me);
        Assert.Equal("andras.k", me.User.Username);
        Assert.Equal("Europe/Budapest", me.User.Timezone);
        Assert.Equal("dark", me.Preferences.Theme);
        Assert.Equal("12h", me.Preferences.TimeFormat);
        Assert.False(me.Preferences.RequireAgentApproval);
    }

    [Fact]
    public async Task ChangePassword_VerifiesCurrent_AndRevokesOtherSessions()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        // Open a second session via login
        using var secondClient = factory.CreateClient();
        var secondLogin = await secondClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("andras", StrongPassword));
        Assert.Equal(HttpStatusCode.OK, secondLogin.StatusCode);

        var beforeSessions = await client.GetFromJsonAsync<List<SessionDto>>("/api/account/sessions");
        Assert.NotNull(beforeSessions);
        Assert.Equal(2, beforeSessions.Count);

        var wrongCurrent = await client.PostAsJsonAsync(
            "/api/account/password",
            new ChangePasswordRequest("WrongCurrent123!", NewStrongPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongCurrent.StatusCode);

        var changed = await client.PostAsJsonAsync(
            "/api/account/password",
            new ChangePasswordRequest(StrongPassword, NewStrongPassword));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        var afterSessions = await client.GetFromJsonAsync<List<SessionDto>>("/api/account/sessions");
        Assert.NotNull(afterSessions);
        Assert.Single(afterSessions);
        Assert.True(afterSessions[0].IsCurrent);
    }

    [Fact]
    public async Task AgentToken_AuthenticatesAgentEndpoint_IsRejectedOnAccountEndpoint_AndStopsWorkingWhenRevoked()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var createRes = await client.PostAsJsonAsync(
            "/api/account/tokens",
            new CreateAgentTokenRequest("Claude MCP", ["calendar:read", "tasks:propose"]));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

        var created = await createRes.Content.ReadFromJsonAsync<CreateAgentTokenResponse>();
        Assert.NotNull(created);
        Assert.StartsWith("atr_", created.Secret, StringComparison.Ordinal);

        // Use PAT on agent endpoint -> 200 with scopes
        using var agentClient = factory.CreateClient();
        agentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Secret);
        var whoamiRes = await agentClient.GetAsync("/api/agent/whoami");
        Assert.Equal(HttpStatusCode.OK, whoamiRes.StatusCode);

        var whoami = await whoamiRes.Content.ReadFromJsonAsync<AgentWhoAmIResponse>();
        Assert.NotNull(whoami);
        Assert.Equal(AuthConstants.PatAuthType, whoami.AuthType);
        Assert.Contains("calendar:read", whoami.Scopes);
        Assert.Contains("tasks:propose", whoami.Scopes);

        // PAT must be rejected on human-only account endpoints
        var accountRes = await agentClient.GetAsync("/api/account/me");
        Assert.Equal(HttpStatusCode.Unauthorized, accountRes.StatusCode);

        // Revoke PAT -> agent endpoint now returns 401
        var revokeRes = await client.DeleteAsync($"/api/account/tokens/{created.Token.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revokeRes.StatusCode);

        var afterRevoke = await agentClient.GetAsync("/api/agent/whoami");
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
    }
}
