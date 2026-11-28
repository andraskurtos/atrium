using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Atrium.Api.Auth;
using Atrium.Api.Chat;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Atrium.Api.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class AiConfigAndAttachmentsTests
{
    private const string OwnerPassword = "Atrium-Owner-Pass-2026!";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    private static async Task<AuthSessionResponse> RegisterSecondUserAsync(HttpClient ownerClient, HttpClient secondClient)
    {
        var inviteRes = await ownerClient.PostAsJsonAsync("/api/account/invites", new CreateInviteRequest("Second", 7));
        Assert.Equal(HttpStatusCode.Created, inviteRes.StatusCode);
        var invite = await inviteRes.Content.ReadFromJsonAsync<CreateInviteResponse>();
        Assert.NotNull(invite);

        var regRes = await secondClient.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterWithInviteRequest(invite.Code, "bela", "bela@atrium.local", "Béla", OwnerPassword));
        Assert.Equal(HttpStatusCode.OK, regRes.StatusCode);
        var session = await regRes.Content.ReadFromJsonAsync<AuthSessionResponse>();
        Assert.NotNull(session);
        secondClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return session;
    }

    [Fact]
    public async Task AiConfig_LegacyPreferences_AutoBackfillIntoDefaultProviderAndModel()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var prefRes = await client.PutAsJsonAsync(
            "/api/account/preferences",
            new UpdatePreferencesRequest(
                "system",
                "monday",
                "24h",
                true,
                true,
                true,
                AiInferenceUrl: "https://legacy.example.test/v1",
                AiModel: "gpt-4o-mini",
                AiApiKey: "sk-legacy-secret-123"));
        Assert.Equal(HttpStatusCode.OK, prefRes.StatusCode);

        var configRes = await client.GetAsync("/api/ai/config");
        Assert.Equal(HttpStatusCode.OK, configRes.StatusCode);
        var rawJson = await configRes.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sk-legacy-secret-123", rawJson, StringComparison.Ordinal);

        var config = JsonSerializer.Deserialize<AiConfigResponse>(rawJson, JsonOptions);
        Assert.NotNull(config);
        Assert.Single(config.Providers);
        Assert.Equal("Default", config.Providers[0].Name);
        Assert.Equal("https://legacy.example.test/v1", config.Providers[0].BaseUrl);
        Assert.True(config.Providers[0].HasApiKey);

        Assert.Single(config.Models);
        Assert.Equal("gpt-4o-mini", config.Models[0].ModelId);
        Assert.Equal(config.Models[0].Id, config.DefaultModelId);
        Assert.Equal("duckduckgo", config.WebSearch.Provider);

        // Deleting the migrated provider must NOT resurrect it on the next GET /api/ai/config
        var delMigrated = await client.DeleteAsync($"/api/ai/providers/{config.Providers[0].Id}");
        Assert.Equal(HttpStatusCode.NoContent, delMigrated.StatusCode);

        var afterDelete = await client.GetFromJsonAsync<AiConfigResponse>("/api/ai/config");
        Assert.NotNull(afterDelete);
        Assert.Empty(afterDelete.Providers);
        Assert.Empty(afterDelete.Models);
    }

    [Fact]
    public async Task AiConfig_CrudProvidersModelsDefaults_AndDeleteNullsDefaults()
    {
        await using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var p1Res = await client.PostAsJsonAsync(
            "/api/ai/providers",
            new CreateAiProviderRequest("OpenRouter", "https://openrouter.ai/api/v1", "sk-or-secret"));
        Assert.Equal(HttpStatusCode.Created, p1Res.StatusCode);
        var p1 = await p1Res.Content.ReadFromJsonAsync<AiProviderDto>();
        Assert.NotNull(p1);
        Assert.True(p1.HasApiKey);

        var m1Res = await client.PostAsJsonAsync(
            "/api/ai/models",
            new CreateAiModelRequest(p1.Id, "deepseek/deepseek-r1", "DeepSeek R1", SupportsVision: false));
        Assert.Equal(HttpStatusCode.Created, m1Res.StatusCode);
        var m1 = await m1Res.Content.ReadFromJsonAsync<AiModelDto>();
        Assert.NotNull(m1);

        var m2Res = await client.PostAsJsonAsync(
            "/api/ai/models",
            new CreateAiModelRequest(p1.Id, "openai/gpt-4o", "GPT-4o Vision", SupportsVision: true));
        Assert.Equal(HttpStatusCode.Created, m2Res.StatusCode);
        var m2 = await m2Res.Content.ReadFromJsonAsync<AiModelDto>();
        Assert.NotNull(m2);

        var defaultsRes = await client.PutAsJsonAsync(
            "/api/ai/defaults",
            new UpdateAiDefaultsRequest(
                DefaultModelId: m1.Id,
                DefaultVisionModelId: m2.Id,
                WebSearchProvider: "tavily",
                WebSearchUrl: null,
                WebSearchApiKey: "tvly-secret-key",
                ClearWebSearchApiKey: false));
        Assert.Equal(HttpStatusCode.OK, defaultsRes.StatusCode);
        var updated = await defaultsRes.Content.ReadFromJsonAsync<AiConfigResponse>();
        Assert.NotNull(updated);
        Assert.Equal(m1.Id, updated.DefaultModelId);
        Assert.Equal(m2.Id, updated.DefaultVisionModelId);
        Assert.Equal("tavily", updated.WebSearch.Provider);
        Assert.True(updated.WebSearch.HasApiKey);

        // Deleting a model referenced as DefaultVisionModelId should cleanly null it out
        var delRes = await client.DeleteAsync($"/api/ai/models/{m2.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        var afterDel = await client.GetFromJsonAsync<AiConfigResponse>("/api/ai/config");
        Assert.NotNull(afterDel);
        Assert.Equal(m1.Id, afterDel.DefaultModelId);
        Assert.Null(afterDel.DefaultVisionModelId);
        Assert.Single(afterDel.Models);
    }

    [Fact]
    public async Task AiConfig_CrossUserIsolation_RejectsOtherUsersProviderAndModel()
    {
        await using var factory = new TestAppFactory();
        using var ownerClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        await SetupOwnerAsync(ownerClient);
        await RegisterSecondUserAsync(ownerClient, secondClient);

        var pRes = await ownerClient.PostAsJsonAsync(
            "/api/ai/providers",
            new CreateAiProviderRequest("OwnerProvider", "https://owner.example.test/v1", "sk-owner"));
        Assert.Equal(HttpStatusCode.Created, pRes.StatusCode);
        var p = await pRes.Content.ReadFromJsonAsync<AiProviderDto>();
        Assert.NotNull(p);

        var mRes = await ownerClient.PostAsJsonAsync(
            "/api/ai/models",
            new CreateAiModelRequest(p.Id, "owner-model", "Owner Model", SupportsVision: true));
        Assert.Equal(HttpStatusCode.Created, mRes.StatusCode);
        var m = await mRes.Content.ReadFromJsonAsync<AiModelDto>();
        Assert.NotNull(m);

        var delOtherProvider = await secondClient.DeleteAsync($"/api/ai/providers/{p.Id}");
        Assert.Equal(HttpStatusCode.NotFound, delOtherProvider.StatusCode);

        var addModelToOtherProvider = await secondClient.PostAsJsonAsync(
            "/api/ai/models",
            new CreateAiModelRequest(p.Id, "hijack", "Hijack", false));
        Assert.Equal(HttpStatusCode.NotFound, addModelToOtherProvider.StatusCode);

        var setOtherDefault = await secondClient.PutAsJsonAsync(
            "/api/ai/defaults",
            new UpdateAiDefaultsRequest(m.Id, null, null, null, null, false));
        Assert.Equal(HttpStatusCode.BadRequest, setOtherDefault.StatusCode);
    }

    [Fact]
    public async Task ChatAttachments_UploadIcsTxtPng_RejectsExe_AndQueryNeptunIcs()
    {
        await using var factory = new TestAppFactory();
        using var ownerClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        var owner = await SetupOwnerAsync(ownerClient);
        await RegisterSecondUserAsync(ownerClient, secondClient);

        const string sampleIcs = """
            BEGIN:VCALENDAR
            VERSION:2.0
            X-WR-TIMEZONE:Europe/Budapest
            BEGIN:VEVENT
            UID:e1
            DTSTART:20260908T101500Z
            DTEND:20260908T120000Z
            LOCATION:IE220
            SUMMARY:Mesterséges általános intelligencia ( - EA) - Dr. Antal Péter - Tanóra
            END:VEVENT
            BEGIN:VEVENT
            UID:e2
            DTSTART:20260915T101500Z
            DTEND:20260915T120000Z
            LOCATION:IE220
            SUMMARY:Mesterséges általános intelligencia ( - EA) - Dr. Antal Péter - Tanóra
            END:VEVENT
            BEGIN:VEVENT
            UID:e3
            DTSTART:20260910T081500Z
            DTEND:20260910T100000Z
            LOCATION:QBF10
            SUMMARY:Deep Learning ( - LAB) - Tanóra
            END:VEVENT
            END:VCALENDAR
            """;

        using var icsForm = new MultipartFormDataContent();
        using var icsBytes = new ByteArrayContent(Encoding.UTF8.GetBytes(sampleIcs));
        icsBytes.Headers.ContentType = MediaTypeHeaderValue.Parse("text/calendar");
        icsForm.Add(icsBytes, "file", "NeptunExport.ics");
        var icsRes = await ownerClient.PostAsync("/api/chat/attachments", icsForm);
        Assert.Equal(HttpStatusCode.Created, icsRes.StatusCode);
        var icsDto = await icsRes.Content.ReadFromJsonAsync<ChatAttachmentDto>();
        Assert.NotNull(icsDto);
        Assert.Equal("ics", icsDto.Kind);
        Assert.Contains("3 events", icsDto.Summary, StringComparison.OrdinalIgnoreCase);

        // Disallowed extension rejected
        using var exeForm = new MultipartFormDataContent();
        using var exeBytes = new ByteArrayContent("MZ"u8.ToArray());
        exeForm.Add(exeBytes, "file", "malware.exe");
        var exeRes = await ownerClient.PostAsync("/api/chat/attachments", exeForm);
        Assert.Equal(HttpStatusCode.BadRequest, exeRes.StatusCode);

        // Cross-user download blocked
        var otherGet = await secondClient.GetAsync($"/api/chat/attachments/{icsDto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, otherGet.StatusCode);

        // Owner download works
        var ownerGet = await ownerClient.GetAsync($"/api/chat/attachments/{icsDto.Id}");
        Assert.Equal(HttpStatusCode.OK, ownerGet.StatusCode);

        // Bind to a thread and query via ChatAttachmentService
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ChatAttachmentService>();
        var storage = scope.ServiceProvider.GetRequiredService<IAttachmentStorage>();

        var thread = new ChatThread
        {
            Id = Guid.NewGuid(),
            UserId = owner.User.Id,
            Title = "Course setup",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var msg = new ChatMessageEntity
        {
            Id = Guid.NewGuid(),
            ThreadId = thread.Id,
            UserId = owner.User.Id,
            Role = "user",
            Content = "Set up my course",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ChatThreads.Add(thread);
        db.ChatMessages.Add(msg);
        await db.SaveChangesAsync();

        var bound = await svc.BindToMessageAsync(owner.User.Id, thread.Id, msg.Id, [icsDto.Id], CancellationToken.None);
        Assert.Single(bound);
        var storageKey = bound[0].StorageKey;

        var overviewJson = await svc.QueryIcsAsync(owner.User.Id, thread.Id, icsDto.Id, null, null, null, 20, "Europe/Budapest", CancellationToken.None);
        Assert.Contains("Mesterséges általános intelligencia", overviewJson, StringComparison.Ordinal);
        Assert.Contains("Deep Learning", overviewJson, StringComparison.Ordinal);

        var filteredJson = await svc.QueryIcsAsync(owner.User.Id, thread.Id, icsDto.Id, "Mesterséges", null, null, 20, "Europe/Budapest", CancellationToken.None);
        Assert.Contains("IE220", filteredJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Deep Learning", filteredJson, StringComparison.Ordinal);

        // Deleting thread cleans up backing file in storage
        var delThreadRes = await ownerClient.DeleteAsync($"/api/chat/threads/{thread.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delThreadRes.StatusCode);
        var afterStream = await storage.OpenReadAsync(storageKey, CancellationToken.None);
        Assert.Null(afterStream);
    }
}
