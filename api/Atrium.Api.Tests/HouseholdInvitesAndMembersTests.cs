using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Atrium.Api.Auth;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class HouseholdInvitesAndMembersTests
{
    private const string StrongPassword = "Atrium-Household-2026!";

    [Fact]
    public async Task OwnerInvite_RegistersMember_BlocksMemberFromOwnerEndpoints_AndDeactivationLocksMemberOut()
    {
        using var factory = new TestAppFactory();
        using var ownerClient = factory.CreateClient();

        // 1. Setup sole Owner
        var setupRes = await ownerClient.PostAsJsonAsync(
            "/api/auth/setup",
            new SetupRequest("andras", "andras@atrium.local", "András", StrongPassword));
        Assert.Equal(HttpStatusCode.OK, setupRes.StatusCode);
        var ownerSession = await setupRes.Content.ReadFromJsonAsync<AuthSessionResponse>();
        Assert.NotNull(ownerSession);
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerSession.AccessToken);

        // 2. Owner creates invite
        var inviteRes = await ownerClient.PostAsJsonAsync(
            "/api/account/invites",
            new CreateInviteRequest("Partner", 7));
        Assert.Equal(HttpStatusCode.Created, inviteRes.StatusCode);
        var invite = await inviteRes.Content.ReadFromJsonAsync<CreateInviteResponse>();
        Assert.NotNull(invite);
        Assert.StartsWith("inv_", invite.Code, StringComparison.Ordinal);

        // 3. Member registers using invite
        using var memberClient = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });
        var regRes = await memberClient.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterWithInviteRequest(invite.Code, "maya", "maya@atrium.local", "Maya", StrongPassword));
        Assert.Equal(HttpStatusCode.OK, regRes.StatusCode);
        var memberSession = await regRes.Content.ReadFromJsonAsync<AuthSessionResponse>();
        Assert.NotNull(memberSession);
        Assert.Equal("Member", memberSession.User.Role);
        var memberRefreshCookie = AuthFlowTests.ExtractRefreshCookie(regRes);
        memberClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberSession.AccessToken);

        // 4. Reusing spent invite fails
        var reuseRes = await memberClient.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterWithInviteRequest(invite.Code, "other", "other@atrium.local", "Other", StrongPassword));
        Assert.Equal(HttpStatusCode.BadRequest, reuseRes.StatusCode);

        // 5. Member cannot access Owner-only endpoints
        var memberInvitesAttempt = await memberClient.GetAsync("/api/account/invites");
        Assert.Equal(HttpStatusCode.Forbidden, memberInvitesAttempt.StatusCode);

        // 6. Member creates an MCP PAT
        var memberPatRes = await memberClient.PostAsJsonAsync(
            "/api/account/tokens",
            new CreateAgentTokenRequest("Maya Agent", ["tasks:read"]));
        Assert.Equal(HttpStatusCode.Created, memberPatRes.StatusCode);
        var memberPat = await memberPatRes.Content.ReadFromJsonAsync<CreateAgentTokenResponse>();
        Assert.NotNull(memberPat);

        // 7. Owner cannot deactivate self
        var selfDeactivate = await ownerClient.PatchAsJsonAsync(
            $"/api/account/members/{ownerSession.User.Id}",
            new UpdateMemberStatusRequest(false));
        Assert.Equal(HttpStatusCode.BadRequest, selfDeactivate.StatusCode);

        // 8. Owner deactivates Member -> revokes member sessions & PATs immediately
        var deactivateRes = await ownerClient.PatchAsJsonAsync(
            $"/api/account/members/{memberSession.User.Id}",
            new UpdateMemberStatusRequest(false));
        Assert.Equal(HttpStatusCode.OK, deactivateRes.StatusCode);

        // Member's still-unexpired JWT must immediately fail on all authenticated endpoints
        var jwtSessionsAfterDeactivate = await memberClient.GetAsync("/api/account/sessions");
        Assert.Equal(HttpStatusCode.Unauthorized, jwtSessionsAfterDeactivate.StatusCode);

        var jwtTokensAfterDeactivate = await memberClient.GetAsync("/api/account/tokens");
        Assert.Equal(HttpStatusCode.Unauthorized, jwtTokensAfterDeactivate.StatusCode);

        var jwtWhoamiAfterDeactivate = await memberClient.GetAsync("/api/agent/whoami");
        Assert.Equal(HttpStatusCode.Unauthorized, jwtWhoamiAfterDeactivate.StatusCode);

        // Member refresh fails
        using var refreshReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshReq.Headers.Add("Cookie", memberRefreshCookie);
        refreshReq.Headers.Add(AuthConstants.CsrfHeaderName, AuthConstants.CsrfHeaderValue);
        var refreshAfterDeactivate = await memberClient.SendAsync(refreshReq);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterDeactivate.StatusCode);

        // Member login fails
        var loginAfterDeactivate = await memberClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("maya", StrongPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, loginAfterDeactivate.StatusCode);

        // Member PAT fails
        using var patClient = factory.CreateClient();
        patClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberPat.Secret);
        var patAfterDeactivate = await patClient.GetAsync("/api/agent/whoami");
        Assert.Equal(HttpStatusCode.Unauthorized, patAfterDeactivate.StatusCode);

        // 9. Owner deletes Member
        var deleteRes = await ownerClient.DeleteAsync($"/api/account/members/{memberSession.User.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        var remaining = await ownerClient.GetFromJsonAsync<List<HouseholdMemberDto>>("/api/account/members");
        Assert.NotNull(remaining);
        Assert.Single(remaining);
        Assert.Equal("andras", remaining[0].Username);
    }
}
