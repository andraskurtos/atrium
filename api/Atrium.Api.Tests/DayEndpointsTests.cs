using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Atrium.Api.Auth;
using Atrium.Api.Days;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class DayEndpointsTests
{
    private const string OwnerPassword = "Atrium-Owner-Pass-2026!";
    private const string MemberPassword = "Atrium-Member-Pass-2026!";

    private static async Task<AuthSessionResponse> SetupOwnerAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync(
            "/api/auth/setup",
            new SetupRequest("andras", "andras@atrium.local", "András", OwnerPassword));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var session = await res.Content.ReadFromJsonAsync<AuthSessionResponse>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return session;
    }

    private static async Task<AuthSessionResponse> CreateInvitedMemberAsync(
        HttpClient ownerClient,
        HttpClient memberClient)
    {
        var inviteRes = await ownerClient.PostAsJsonAsync("/api/account/invites", new CreateInviteRequest("Partner", 7));
        Assert.Equal(HttpStatusCode.Created, inviteRes.StatusCode);
        var invite = await inviteRes.Content.ReadFromJsonAsync<CreateInviteResponse>();
        Assert.NotNull(invite);

        var regRes = await memberClient.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterWithInviteRequest(invite.Code, "partner", "partner@atrium.local", "Partner", MemberPassword));
        Assert.Equal(HttpStatusCode.OK, regRes.StatusCode);
        var session = await regRes.Content.ReadFromJsonAsync<AuthSessionResponse>();
        Assert.NotNull(session);
        memberClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return session;
    }

    [Fact]
    public async Task GetDay_EmptyDate_ReturnsEmptyChecklistAndNullNote()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var day = await client.GetFromJsonAsync<DailyTrackerResponse>("/api/days/2026-11-26");
        Assert.NotNull(day);
        Assert.Equal("2026-11-26", day.Date);
        Assert.Empty(day.ChecklistItems);
        Assert.Null(day.Note);
    }

    [Fact]
    public async Task Checklist_CreateToggleEditMoveToTomorrowAndDelete_WorksEndToEnd()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        // Create two items on 2026-11-26
        var create1Res = await client.PostAsJsonAsync(
            "/api/days/2026-11-26/checklist",
            new CreateDailyCheckItemRequest("Review pull request", "09:30"));
        Assert.Equal(HttpStatusCode.Created, create1Res.StatusCode);
        var item1 = await create1Res.Content.ReadFromJsonAsync<DailyCheckItemDto>();
        Assert.NotNull(item1);
        Assert.Equal("2026-11-26", item1.Date);
        Assert.Equal("Review pull request", item1.Title);
        Assert.Equal("09:30", item1.DueTime);
        Assert.False(item1.IsCompleted);
        Assert.Null(item1.CompletedAt);
        Assert.Equal(0, item1.SortOrder);

        var create2Res = await client.PostAsJsonAsync(
            "/api/days/2026-11-26/checklist",
            new CreateDailyCheckItemRequest("Buy groceries"));
        Assert.Equal(HttpStatusCode.Created, create2Res.StatusCode);
        var item2 = await create2Res.Content.ReadFromJsonAsync<DailyCheckItemDto>();
        Assert.NotNull(item2);
        Assert.Equal(1, item2.SortOrder);

        // Toggle item1 completed and edit title + clear due time
        var toggleRes = await client.PutAsJsonAsync(
            $"/api/days/checklist/{item1.Id}",
            new UpdateDailyCheckItemRequest(Title: "Review PR #42", IsCompleted: true, ClearDueTime: true));
        Assert.Equal(HttpStatusCode.OK, toggleRes.StatusCode);
        var updated1 = await toggleRes.Content.ReadFromJsonAsync<DailyCheckItemDto>();
        Assert.NotNull(updated1);
        Assert.Equal("Review PR #42", updated1.Title);
        Assert.True(updated1.IsCompleted);
        Assert.NotNull(updated1.CompletedAt);
        Assert.Null(updated1.DueTime);

        // Uncheck item1 -> CompletedAt clears
        var uncheckRes = await client.PutAsJsonAsync(
            $"/api/days/checklist/{item1.Id}",
            new UpdateDailyCheckItemRequest(IsCompleted: false));
        Assert.Equal(HttpStatusCode.OK, uncheckRes.StatusCode);
        var unchecked1 = await uncheckRes.Content.ReadFromJsonAsync<DailyCheckItemDto>();
        Assert.NotNull(unchecked1);
        Assert.False(unchecked1.IsCompleted);
        Assert.Null(unchecked1.CompletedAt);

        // Create an existing item on tomorrow (2026-11-27), then move item2 to tomorrow
        var tomorrowItemRes = await client.PostAsJsonAsync(
            "/api/days/2026-11-27/checklist",
            new CreateDailyCheckItemRequest("Morning run"));
        Assert.Equal(HttpStatusCode.Created, tomorrowItemRes.StatusCode);

        var moveRes = await client.PutAsJsonAsync(
            $"/api/days/checklist/{item2.Id}",
            new UpdateDailyCheckItemRequest(Date: "2026-11-27"));
        Assert.Equal(HttpStatusCode.OK, moveRes.StatusCode);
        var moved2 = await moveRes.Content.ReadFromJsonAsync<DailyCheckItemDto>();
        Assert.NotNull(moved2);
        Assert.Equal("2026-11-27", moved2.Date);
        Assert.Equal(1, moved2.SortOrder);

        var day26 = await client.GetFromJsonAsync<DailyTrackerResponse>("/api/days/2026-11-26");
        Assert.NotNull(day26);
        Assert.Single(day26.ChecklistItems);
        Assert.Equal(item1.Id, day26.ChecklistItems[0].Id);

        var day27 = await client.GetFromJsonAsync<DailyTrackerResponse>("/api/days/2026-11-27");
        Assert.NotNull(day27);
        Assert.Equal(2, day27.ChecklistItems.Count);
        Assert.Equal("Morning run", day27.ChecklistItems[0].Title);
        Assert.Equal("Buy groceries", day27.ChecklistItems[1].Title);

        // Delete item1
        var deleteRes = await client.DeleteAsync($"/api/days/checklist/{item1.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        var day26After = await client.GetFromJsonAsync<DailyTrackerResponse>("/api/days/2026-11-26");
        Assert.NotNull(day26After);
        Assert.Empty(day26After.ChecklistItems);
    }

    [Fact]
    public async Task DailyNote_UpsertUpdateAndClearWhitespace_WorksEndToEnd()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        // Create note
        var createRes = await client.PutAsJsonAsync(
            "/api/days/2026-11-26/note",
            new UpsertDailyNoteRequest("Focused morning session."));
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<UpsertDailyNoteResponse>();
        Assert.NotNull(created);
        Assert.Equal("2026-11-26", created.Date);
        Assert.NotNull(created.Note);
        Assert.Equal("Focused morning session.", created.Note.Content);

        // Update same day note
        var updateRes = await client.PutAsJsonAsync(
            "/api/days/2026-11-26/note",
            new UpsertDailyNoteRequest("Focused morning session.\nShipped Day Tracker."));
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        var updated = await updateRes.Content.ReadFromJsonAsync<UpsertDailyNoteResponse>();
        Assert.NotNull(updated?.Note);
        Assert.Equal(created.Note.Id, updated.Note.Id);
        Assert.Contains("Shipped Day Tracker.", updated.Note.Content, StringComparison.Ordinal);

        // Clear to whitespace -> deletes row and returns null Note
        var clearRes = await client.PutAsJsonAsync(
            "/api/days/2026-11-26/note",
            new UpsertDailyNoteRequest("   \n  "));
        Assert.Equal(HttpStatusCode.OK, clearRes.StatusCode);
        var cleared = await clearRes.Content.ReadFromJsonAsync<UpsertDailyNoteResponse>();
        Assert.NotNull(cleared);
        Assert.Null(cleared.Note);

        var dayAfterClear = await client.GetFromJsonAsync<DailyTrackerResponse>("/api/days/2026-11-26");
        Assert.NotNull(dayAfterClear);
        Assert.Null(dayAfterClear.Note);
    }

    [Fact]
    public async Task Validation_RejectsInvalidDates_InvalidDueTime_EmptyTitle_AndOversizedNote()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/days/2026-02-30")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/days/not-a-date")).StatusCode);

        var badDueTime = await client.PostAsJsonAsync(
            "/api/days/2026-11-26/checklist",
            new CreateDailyCheckItemRequest("Task", "25:00"));
        Assert.Equal(HttpStatusCode.BadRequest, badDueTime.StatusCode);

        var emptyTitle = await client.PostAsJsonAsync(
            "/api/days/2026-11-26/checklist",
            new CreateDailyCheckItemRequest("   "));
        Assert.Equal(HttpStatusCode.BadRequest, emptyTitle.StatusCode);

        var longTitle = await client.PostAsJsonAsync(
            "/api/days/2026-11-26/checklist",
            new CreateDailyCheckItemRequest(new string('x', 281)));
        Assert.Equal(HttpStatusCode.BadRequest, longTitle.StatusCode);

        var oversizedNote = await client.PutAsJsonAsync(
            "/api/days/2026-11-26/note",
            new UpsertDailyNoteRequest(new string('n', 10_001)));
        Assert.Equal(HttpStatusCode.BadRequest, oversizedNote.StatusCode);
    }

    [Fact]
    public async Task UserIsolation_AndPatScopes_AreEnforced()
    {
        using var factory = new TestAppFactory();
        using var ownerClient = factory.CreateClient();
        using var memberClient = factory.CreateClient();
        await SetupOwnerAsync(ownerClient);
        await CreateInvitedMemberAsync(ownerClient, memberClient);

        var ownerItem = await (await ownerClient.PostAsJsonAsync(
            "/api/days/2026-11-26/checklist",
            new CreateDailyCheckItemRequest("Owner secret task")))
            .Content.ReadFromJsonAsync<DailyCheckItemDto>();
        Assert.NotNull(ownerItem);

        await ownerClient.PutAsJsonAsync(
            "/api/days/2026-11-26/note",
            new UpsertDailyNoteRequest("Owner private note"));

        // Member sees empty day and gets 404 on owner's checklist mutations
        var memberDay = await memberClient.GetFromJsonAsync<DailyTrackerResponse>("/api/days/2026-11-26");
        Assert.NotNull(memberDay);
        Assert.Empty(memberDay.ChecklistItems);
        Assert.Null(memberDay.Note);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await memberClient.PutAsJsonAsync(
                $"/api/days/checklist/{ownerItem.Id}",
                new UpdateDailyCheckItemRequest(IsCompleted: true))).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await memberClient.DeleteAsync($"/api/days/checklist/{ownerItem.Id}")).StatusCode);

        // PAT with tasks:read can GET /api/days/{date} but cannot mutate
        var patRead = await (await ownerClient.PostAsJsonAsync(
            "/api/account/tokens",
            new CreateAgentTokenRequest("Tasks Agent", ["tasks:read"])))
            .Content.ReadFromJsonAsync<CreateAgentTokenResponse>();
        Assert.NotNull(patRead);

        using var patReadClient = factory.CreateClient();
        patReadClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", patRead.Secret);
        var patGetRes = await patReadClient.GetAsync("/api/days/2026-11-26");
        Assert.Equal(HttpStatusCode.OK, patGetRes.StatusCode);

        var patWriteRes = await patReadClient.PostAsJsonAsync(
            "/api/days/2026-11-26/checklist",
            new CreateDailyCheckItemRequest("Blocked"));
        Assert.Equal(HttpStatusCode.Unauthorized, patWriteRes.StatusCode);

        // PAT without tasks:read gets 403
        var patNoScope = await (await ownerClient.PostAsJsonAsync(
            "/api/account/tokens",
            new CreateAgentTokenRequest("Vault Agent", ["vault:read"])))
            .Content.ReadFromJsonAsync<CreateAgentTokenResponse>();
        Assert.NotNull(patNoScope);

        using var patNoScopeClient = factory.CreateClient();
        patNoScopeClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", patNoScope.Secret);
        var forbiddenRes = await patNoScopeClient.GetAsync("/api/days/2026-11-26");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRes.StatusCode);
    }
}

