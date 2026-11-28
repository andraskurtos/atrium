using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Atrium.Api.Auth;
using Atrium.Api.Chat;
using Atrium.Api.Data;
using Atrium.Api.Notes;
using Atrium.Api.University;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class ChatEndpointsTests
{
    private const string OwnerPassword = "Atrium-Owner-Pass-2026!";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class FakeOpenAiHandler(Func<HttpRequestMessage, string, int, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        private int callCount;
        public List<(string? AuthHeader, string Body)> RecordedRequests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is not null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : string.Empty;
            RecordedRequests.Add((request.Headers.Authorization?.ToString(), body));
            var idx = Interlocked.Increment(ref callCount) - 1;
            return responder(request, body, idx);
        }
    }

    private static HttpClient CreateChatClient(TestAppFactory baseFactory, FakeOpenAiHandler handler)
    {
        var configured = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient(OpenAiChatService.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => handler);
            });
        });
        return configured.CreateClient();
    }

    private static HttpResponseMessage SseResponse(params string[] dataLines)
    {
        var sb = new StringBuilder();
        foreach (var line in dataLines)
        {
            sb.Append("data: ").Append(line).Append("\n\n");
        }

        sb.Append("data: [DONE]\n\n");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sb.ToString(), Encoding.UTF8, "text/event-stream"),
        };
    }

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

    private static async Task ConfigureAiAsync(HttpClient client, string url, string model, string? apiKey = "sk-test-secret-123")
    {
        var res = await client.PutAsJsonAsync(
            "/api/account/preferences",
            new UpdatePreferencesRequest("system", "monday", "24h", true, true, true, url, model, apiKey, false));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Preferences_EncryptsApiKey_NeverExposesPlaintext_AndSupportsClear()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var putRes = await client.PutAsJsonAsync(
            "/api/account/preferences",
            new UpdatePreferencesRequest(
                "dark",
                "monday",
                "24h",
                true,
                true,
                true,
                "https://api.openai.com/v1/",
                "gpt-4o-mini",
                "sk-super-secret-value",
                false));
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        var rawJson = await putRes.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sk-super-secret-value", rawJson, StringComparison.Ordinal);

        var dto = JsonSerializer.Deserialize<PreferencesDto>(rawJson, JsonOptions);
        Assert.NotNull(dto);
        Assert.Equal("https://api.openai.com/v1", dto.AiInferenceUrl);
        Assert.Equal("gpt-4o-mini", dto.AiModel);
        Assert.True(dto.HasAiApiKey);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pref = await db.UserPreferences.FirstAsync();
            Assert.NotNull(pref.AiApiKeyEncrypted);
            Assert.DoesNotContain("sk-super-secret-value", pref.AiApiKeyEncrypted, StringComparison.Ordinal);
            var protector = scope.ServiceProvider.GetRequiredService<SecretProtector>();
            Assert.Equal("sk-super-secret-value", protector.Unprotect(pref.AiApiKeyEncrypted));
        }

        var clearRes = await client.PutAsJsonAsync(
            "/api/account/preferences",
            new UpdatePreferencesRequest("dark", "monday", "24h", true, true, true, "https://api.openai.com/v1", "gpt-4o-mini", null, true));
        Assert.Equal(HttpStatusCode.OK, clearRes.StatusCode);
        var cleared = await clearRes.Content.ReadFromJsonAsync<PreferencesDto>();
        Assert.NotNull(cleared);
        Assert.False(cleared.HasAiApiKey);
    }

    [Fact]
    public async Task ChatStream_GlobalToolCalling_InsertsParagraphBreakAfterPreToolText_AndStreamsCitations()
    {
        using var handler = new FakeOpenAiHandler((_, _, round) =>
        {
            if (round == 0)
            {
                return SseResponse(
                    """{"choices":[{"delta":{"content":"Let me check your notes."}}]}""",
                    """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"search_knowledge","arguments":"{\"query\":\"Dijkstra\"}"}}]}}]}""");
            }

            return SseResponse(
                """{"choices":[{"delta":{"content":"Dijkstra runs in "}}]}""",
                """{"choices":[{"delta":{"content":"O(E log V)."}}]}""");
        });

        using var factory = new TestAppFactory();
        using var client = CreateChatClient(factory, handler);
        await SetupOwnerAsync(client);
        await ConfigureAiAsync(client, "https://inference.local/v1", "gpt-4o-mini", "sk-live-abc");

        var note = await (await client.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("Graph Algorithms", null, "Dijkstra shortest path runs in O(E log V).")))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(note);

        var chatRes = await client.PostAsJsonAsync(
            "/api/chat/stream",
            new ChatStreamRequest([new ChatMessageInput("user", "What is Dijkstra's complexity?")]));
        Assert.Equal(HttpStatusCode.OK, chatRes.StatusCode);

        var sseBody = await chatRes.Content.ReadAsStringAsync();
        Assert.Contains("\"text\":\"Let me check your notes.\"", sseBody, StringComparison.Ordinal);
        Assert.Contains("\"text\":\"\\n\\n\"", sseBody, StringComparison.Ordinal);
        Assert.Contains("\"text\":\"Dijkstra runs in \"", sseBody, StringComparison.Ordinal);
        Assert.Contains("\"text\":\"O(E log V).\"", sseBody, StringComparison.Ordinal);
        Assert.Contains(note.Id.ToString(), sseBody, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"done\"", sseBody, StringComparison.Ordinal);

        Assert.Equal(2, handler.RecordedRequests.Count);
        Assert.Equal("Bearer sk-live-abc", handler.RecordedRequests[0].AuthHeader);
        Assert.Contains("Graph Algorithms", handler.RecordedRequests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatStream_CourseScoped_BlocksCrossCourseNote_AndRestrictsSearch()
    {
        Guid otherNoteId = Guid.Empty;
        using var handler = new FakeOpenAiHandler((_, _, round) =>
        {
            if (round == 0)
            {
                return SseResponse(
                    $"{{\"choices\":[{{\"delta\":{{\"tool_calls\":[{{\"index\":0,\"id\":\"call_cross\",\"function\":{{\"name\":\"read_note\",\"arguments\":\"{{\\\"noteId\\\":\\\"{otherNoteId}\\\"}}\"}}}}]}}}}]}}");
            }

            return SseResponse("""{"choices":[{"delta":{"content":"Only course A is accessible."}}]}""");
        });

        using var factory = new TestAppFactory();
        using var client = CreateChatClient(factory, handler);
        await SetupOwnerAsync(client);
        await ConfigureAiAsync(client, "https://inference.local/v1", "gpt-4o-mini", null);

        var courseA = await (await client.PostAsJsonAsync(
            "/api/university/courses",
            new CreateCourseRequest("Algorithms", "CS201", "2026 Fall")))
            .Content.ReadFromJsonAsync<UniversityCourseDto>();
        var courseB = await (await client.PostAsJsonAsync(
            "/api/university/courses",
            new CreateCourseRequest("History", "HI101", "2026 Fall")))
            .Content.ReadFromJsonAsync<UniversityCourseDto>();
        Assert.NotNull(courseA);
        Assert.NotNull(courseB);

        var otherNote = await (await client.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("Roman Empire", null, "Secret history text", true, courseB.Id)))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(otherNote);
        otherNoteId = otherNote.Id;

        var chatRes = await client.PostAsJsonAsync(
            "/api/chat/stream",
            new ChatStreamRequest([new ChatMessageInput("user", "Read the history note")], courseA.Id));
        Assert.Equal(HttpStatusCode.OK, chatRes.StatusCode);

        var sseBody = await chatRes.Content.ReadAsStringAsync();
        Assert.Contains("Only course A is accessible.", sseBody, StringComparison.Ordinal);
        Assert.Equal(2, handler.RecordedRequests.Count);
        Assert.Contains("Access denied", handler.RecordedRequests[1].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret history text", handler.RecordedRequests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatStream_FallsBackToRag_WhenUpstreamRejectsTools()
    {
        using var handler = new FakeOpenAiHandler((_, body, round) =>
        {
            if (round == 0)
            {
                Assert.Contains("\"tools\"", body, StringComparison.Ordinal);
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("""{"error":"Model does not support tools"}""", Encoding.UTF8, "application/json"),
                };
            }

            Assert.DoesNotContain("\"tools\"", body, StringComparison.Ordinal);
            return SseResponse("""{"choices":[{"delta":{"content":"Answered via RAG fallback."}}]}""");
        });

        using var factory = new TestAppFactory();
        using var client = CreateChatClient(factory, handler);
        await SetupOwnerAsync(client);
        await ConfigureAiAsync(client, "http://localhost:11434/v1", "llama3.2", null);

        await client.PostAsJsonAsync("/api/notes", new CreateNoteRequest("OS Notes", null, "Virtual memory uses page tables."));

        var chatRes = await client.PostAsJsonAsync(
            "/api/chat/stream",
            new ChatStreamRequest([new ChatMessageInput("user", "page tables")]));
        Assert.Equal(HttpStatusCode.OK, chatRes.StatusCode);

        var sseBody = await chatRes.Content.ReadAsStringAsync();
        Assert.Contains("Answered via RAG fallback.", sseBody, StringComparison.Ordinal);
        Assert.Equal(2, handler.RecordedRequests.Count);
        Assert.Contains("Virtual memory uses page tables.", handler.RecordedRequests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatHistory_SavesReasoningSeparately_ListsContinuesAndDeletesThreads()
    {
        using var handler = new FakeOpenAiHandler((_, _, round) =>
        {
            if (round == 0)
            {
                return SseResponse(
                    """{"choices":[{"delta":{"reasoning_content":"First I think about it."}}]}""",
                    """{"choices":[{"delta":{"content":"<think> And inline think.</think>Final answer."}}]}""");
            }

            return SseResponse("""{"choices":[{"delta":{"content":"Follow-up answer."}}]}""");
        });

        using var factory = new TestAppFactory();
        using var client = CreateChatClient(factory, handler);
        await SetupOwnerAsync(client);
        await ConfigureAiAsync(client, "https://inference.local/v1", "deepseek-r1", null);

        var res1 = await client.PostAsJsonAsync(
            "/api/chat/stream",
            new ChatStreamRequest([new ChatMessageInput("user", "Explain gravity")]));
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        var sse1 = await res1.Content.ReadAsStringAsync();
        Assert.Contains("\"type\":\"reasoning\"", sse1, StringComparison.Ordinal);
        Assert.Contains("Final answer.", sse1, StringComparison.Ordinal);

        // Thread is listed in GET /api/chat/threads
        var threads = await client.GetFromJsonAsync<List<ChatThreadSummaryDto>>("/api/chat/threads");
        Assert.NotNull(threads);
        Assert.Single(threads);
        var threadId = threads[0].Id;
        Assert.Equal("Explain gravity", threads[0].Title);
        Assert.Equal(2, threads[0].MessageCount);

        // Detail has separated reasoning + clean content
        var detail = await client.GetFromJsonAsync<ChatThreadDetailDto>($"/api/chat/threads/{threadId}");
        Assert.NotNull(detail);
        Assert.Equal(2, detail.Messages.Count);
        Assert.Equal("Final answer.", detail.Messages[1].Content);
        Assert.NotNull(detail.Messages[1].Reasoning);
        Assert.Contains("First I think about it.", detail.Messages[1].Reasoning, StringComparison.Ordinal);
        Assert.Contains("And inline think.", detail.Messages[1].Reasoning, StringComparison.Ordinal);

        // Continue existing thread
        var res2 = await client.PostAsJsonAsync(
            "/api/chat/stream",
            new ChatStreamRequest(
                [
                    new ChatMessageInput("user", "Explain gravity"),
                    new ChatMessageInput("assistant", "Final answer."),
                    new ChatMessageInput("user", "Tell me more"),
                ],
                ThreadId: threadId));
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        var detail2 = await client.GetFromJsonAsync<ChatThreadDetailDto>($"/api/chat/threads/{threadId}");
        Assert.NotNull(detail2);
        Assert.Equal(4, detail2.Messages.Count);
        Assert.Equal("Follow-up answer.", detail2.Messages[3].Content);

        // Delete thread
        var delRes = await client.DeleteAsync($"/api/chat/threads/{threadId}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        var threadsAfter = await client.GetFromJsonAsync<List<ChatThreadSummaryDto>>("/api/chat/threads");
        Assert.NotNull(threadsAfter);
        Assert.Empty(threadsAfter);
    }

    [Fact]
    public async Task ChatStream_VisionAutoRouting_SwitchesForImageTurn_AndDowngradesHistoryForTurn2NonVision()
    {
        var seenRequests = new List<(string Url, string Body)>();
        var handler = new FakeOpenAiHandler((req, body, idx) =>
        {
            seenRequests.Add((req.RequestUri?.ToString() ?? string.Empty, body));
            return idx == 0
                ? SseResponse("""{"choices":[{"delta":{"content":"I see a timetable."}}]}""")
                : SseResponse("""{"choices":[{"delta":{"content":"Follow-up on text model."}}]}""");
        });

        await using var baseFactory = new TestAppFactory();
        using var client = CreateChatClient(baseFactory, handler);
        await SetupOwnerAsync(client);

        var pText = await (await client.PostAsJsonAsync(
            "/api/ai/providers",
            new CreateAiProviderRequest("FastText", "https://text.example.test/v1", "sk-text")))
            .Content.ReadFromJsonAsync<AiProviderDto>();
        Assert.NotNull(pText);
        var mText = await (await client.PostAsJsonAsync(
            "/api/ai/models",
            new CreateAiModelRequest(pText.Id, "fast-text-1", "Fast Text", SupportsVision: false)))
            .Content.ReadFromJsonAsync<AiModelDto>();
        Assert.NotNull(mText);

        var pVision = await (await client.PostAsJsonAsync(
            "/api/ai/providers",
            new CreateAiProviderRequest("VisionProv", "https://vision.example.test/v1", "sk-vision")))
            .Content.ReadFromJsonAsync<AiProviderDto>();
        Assert.NotNull(pVision);
        var mVision = await (await client.PostAsJsonAsync(
            "/api/ai/models",
            new CreateAiModelRequest(pVision.Id, "vision-pro", "Vision Pro", SupportsVision: true)))
            .Content.ReadFromJsonAsync<AiModelDto>();
        Assert.NotNull(mVision);

        await client.PutAsJsonAsync(
            "/api/ai/defaults",
            new UpdateAiDefaultsRequest(mText.Id, mVision.Id, "duckduckgo", null, null, false));

        // Upload a tiny valid PNG header
        using var imgForm = new MultipartFormDataContent();
        using var imgContent = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        imgContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        imgForm.Add(imgContent, "file", "timetable.png");
        var uploadRes = await client.PostAsync("/api/chat/attachments", imgForm);
        Assert.Equal(HttpStatusCode.Created, uploadRes.StatusCode);
        var imgAtt = await uploadRes.Content.ReadFromJsonAsync<ChatAttachmentDto>();
        Assert.NotNull(imgAtt);

        // Turn 1: sends image while non-vision model selected → auto-routes to Vision Pro
        var res1 = await client.PostAsJsonAsync(
            "/api/chat/stream",
            new ChatStreamRequest(
                [new ChatMessageInput("user", "Read this timetable")],
                ModelId: mText.Id,
                AttachmentIds: [imgAtt.Id]));
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        var sse1 = await res1.Content.ReadAsStringAsync();
        Assert.Contains("Switched to Vision Pro", sse1, StringComparison.Ordinal);
        Assert.Single(seenRequests);
        Assert.StartsWith("https://vision.example.test/v1", seenRequests[0].Url, StringComparison.Ordinal);
        Assert.Contains("image_url", seenRequests[0].Body, StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64,", seenRequests[0].Body, StringComparison.Ordinal);

        var threads = await client.GetFromJsonAsync<List<ChatThreadSummaryDto>>("/api/chat/threads");
        Assert.NotNull(threads);
        var threadId = threads[0].Id;
        var detail1 = await client.GetFromJsonAsync<ChatThreadDetailDto>($"/api/chat/threads/{threadId}");
        Assert.NotNull(detail1);
        Assert.Equal(mText.Id, detail1.LastModelId); // LastModelId keeps user's chosen model!

        // Turn 2: text-only follow-up on text model → no image_url, uses text placeholder instead
        var res2 = await client.PostAsJsonAsync(
            "/api/chat/stream",
            new ChatStreamRequest(
                [
                    new ChatMessageInput("user", "Read this timetable"),
                    new ChatMessageInput("assistant", "I see a timetable."),
                    new ChatMessageInput("user", "What time is Friday lab?"),
                ],
                ThreadId: threadId,
                ModelId: mText.Id));
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
        Assert.Equal(2, seenRequests.Count);
        Assert.StartsWith("https://text.example.test/v1", seenRequests[1].Url, StringComparison.Ordinal);
        Assert.DoesNotContain("image_url", seenRequests[1].Body, StringComparison.Ordinal);
        Assert.Contains("[Image attached: timetable.png]", seenRequests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatStream_ImageWithoutVisionModel_ReturnsBadRequest()
    {
        var handler = new FakeOpenAiHandler((_, _, _) => SseResponse("""{"choices":[{"delta":{"content":"ok"}}]}"""));
        await using var baseFactory = new TestAppFactory();
        using var client = CreateChatClient(baseFactory, handler);
        await SetupOwnerAsync(client);

        var p = await (await client.PostAsJsonAsync(
            "/api/ai/providers",
            new CreateAiProviderRequest("TextOnly", "https://text.example.test/v1", "sk-1")))
            .Content.ReadFromJsonAsync<AiProviderDto>();
        Assert.NotNull(p);
        var m = await (await client.PostAsJsonAsync(
            "/api/ai/models",
            new CreateAiModelRequest(p.Id, "text-1", "Text 1", SupportsVision: false)))
            .Content.ReadFromJsonAsync<AiModelDto>();
        Assert.NotNull(m);

        using var imgForm = new MultipartFormDataContent();
        using var imgContent = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        imgContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        imgForm.Add(imgContent, "file", "shot.png");
        var imgAtt = await (await client.PostAsync("/api/chat/attachments", imgForm)).Content.ReadFromJsonAsync<ChatAttachmentDto>();
        Assert.NotNull(imgAtt);

        var res = await client.PostAsJsonAsync(
            "/api/chat/stream",
            new ChatStreamRequest([new ChatMessageInput("user", "What is this?")], AttachmentIds: [imgAtt.Id]));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errBody = await res.Content.ReadAsStringAsync();
        Assert.Contains("vision", errBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatStream_InvalidOrCrossUserAttachmentIds_ReturnsBadRequest()
    {
        var handler = new FakeOpenAiHandler((_, _, _) => SseResponse("""{"choices":[{"delta":{"content":"ok"}}]}"""));
        await using var baseFactory = new TestAppFactory();
        using var client = CreateChatClient(baseFactory, handler);
        await SetupOwnerAsync(client);

        var p = await (await client.PostAsJsonAsync(
            "/api/ai/providers",
            new CreateAiProviderRequest("P", "https://text.example.test/v1", "sk-1")))
            .Content.ReadFromJsonAsync<AiProviderDto>();
        Assert.NotNull(p);
        await client.PostAsJsonAsync(
            "/api/ai/models",
            new CreateAiModelRequest(p.Id, "m1", "M1", SupportsVision: false));

        var res = await client.PostAsJsonAsync(
            "/api/chat/stream",
            new ChatStreamRequest(
                [new ChatMessageInput("user", "Hello")],
                AttachmentIds: [Guid.NewGuid()]));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Empty(handler.RecordedRequests);
    }

    [Fact]
    public async Task ChatStream_CourseSetupTools_QueryIcsAndBatchCreate()
    {
        string? capturedIcsId = null;
        string? capturedCourseId = null;
        string? capturedActivityId = null;

        var handler = new FakeOpenAiHandler((_, body, idx) =>
        {
            if (idx == 0)
            {
                using var reqDoc = JsonDocument.Parse(body);
                var sysPrompt = reqDoc.RootElement.GetProperty("messages")[0].GetProperty("content").GetString() ?? string.Empty;
                var marker = "- `";
                var start = sysPrompt.IndexOf(marker, StringComparison.Ordinal);
                Assert.True(start >= 0, "Expected attachment manifest in system prompt");
                capturedIcsId = sysPrompt.Substring(start + marker.Length, 36);
                return SseResponse(
                    $$$"""{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"c1","type":"function","function":{"name":"query_chat_ics","arguments":"{\"attachmentId\":\"{{{capturedIcsId}}}\",\"query\":\"AGI\"}"}}]}}]}""");
            }

            if (idx == 1)
            {
                Assert.Contains("IE220", body, StringComparison.Ordinal);
                return SseResponse(
                    """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"c2","type":"function","function":{"name":"create_course","arguments":"{\"name\":\"AGI\",\"code\":\"BMEAGI\",\"semester\":\"2026 Fall\"}"}}]}}]}""");
            }

            if (idx == 2)
            {
                using var doc = JsonDocument.Parse(body);
                var lastContent = doc.RootElement.GetProperty("messages").EnumerateArray().Last().GetProperty("content").GetString()!;
                using var courseDoc = JsonDocument.Parse(lastContent);
                capturedCourseId = courseDoc.RootElement.GetProperty("id").GetString();
                return SseResponse(
                    $$$"""{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"c3","type":"function","function":{"name":"create_activity","arguments":"{\"courseId\":\"{{{capturedCourseId}}}\",\"activityType\":\"lecture\",\"title\":\"Lectures\"}"}}]}}]}""");
            }

            if (idx == 3)
            {
                using var doc = JsonDocument.Parse(body);
                var lastContent = doc.RootElement.GetProperty("messages").EnumerateArray().Last().GetProperty("content").GetString()!;
                using var actDoc = JsonDocument.Parse(lastContent);
                capturedActivityId = actDoc.RootElement.GetProperty("id").GetString();
                return SseResponse(
                    $$$"""{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"c4","type":"function","function":{"name":"create_activity_items","arguments":"{\"activityId\":\"{{{capturedActivityId}}}\",\"items\":[{\"title\":\"Lecture 1\",\"theme\":\"Intro\",\"sequenceNumber\":1,\"startAt\":\"2026-09-08T10:15:00Z\",\"endAt\":\"2026-09-08T12:00:00Z\",\"location\":\"IE220\"},{\"title\":\"Lecture 2\",\"theme\":\"Search\",\"sequenceNumber\":2,\"startAt\":\"2026-09-15T10:15:00Z\",\"endAt\":\"2026-09-15T12:00:00Z\",\"location\":\"IE220\"}]}"}}]}}]}""");
            }

            return SseResponse("""{"choices":[{"delta":{"content":"Course AGI set up with 2 lectures."}}]}""");
        });

        await using var baseFactory = new TestAppFactory();
        using var client = CreateChatClient(baseFactory, handler);
        await SetupOwnerAsync(client);

        var p = await (await client.PostAsJsonAsync(
            "/api/ai/providers",
            new CreateAiProviderRequest("P", "https://text.example.test/v1", "sk-1")))
            .Content.ReadFromJsonAsync<AiProviderDto>();
        Assert.NotNull(p);
        await client.PostAsJsonAsync(
            "/api/ai/models",
            new CreateAiModelRequest(p.Id, "m1", "M1", SupportsVision: false));

        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            X-WR-TIMEZONE:Europe/Budapest
            BEGIN:VEVENT
            UID:agi1
            DTSTART:20260908T101500Z
            DTEND:20260908T120000Z
            LOCATION:IE220
            SUMMARY:AGI Lecture
            END:VEVENT
            END:VCALENDAR
            """;

        using var icsForm = new MultipartFormDataContent();
        using var icsBytes = new ByteArrayContent(Encoding.UTF8.GetBytes(ics));
        icsBytes.Headers.ContentType = MediaTypeHeaderValue.Parse("text/calendar");
        icsForm.Add(icsBytes, "file", "Neptun.ics");
        var icsAtt = await (await client.PostAsync("/api/chat/attachments", icsForm)).Content.ReadFromJsonAsync<ChatAttachmentDto>();
        Assert.NotNull(icsAtt);

        var res = await client.PostAsJsonAsync(
            "/api/chat/stream",
            new ChatStreamRequest(
                [new ChatMessageInput("user", "Set up AGI from Neptun.ics")],
                AttachmentIds: [icsAtt.Id]));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var sse = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"type\":\"mutation\"", sse, StringComparison.Ordinal);
        Assert.Contains("Course AGI set up with 2 lectures.", sse, StringComparison.Ordinal);

        var uni = await client.GetFromJsonAsync<UniversityWorkspaceResponse>("/api/university/workspace");
        Assert.NotNull(uni);
        var createdCourse = Assert.Single(uni.Courses, c => c.Code == "BMEAGI");
        Assert.Equal(2, createdCourse.Activities[0].Items.Count);
    }

    [Fact]
    public async Task ChatStream_PreservesGeminiThoughtSignatureAcrossToolRounds()
    {
        var handler = new FakeOpenAiHandler((_, body, idx) =>
        {
            if (idx == 0)
            {
                return SseResponse(
                    """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_g1","type":"function","extra_content":{"google":{"thought_signature":"sig_abc_123"}},"function":{"name":"get_day","arguments":"{}"}}]}}]}""");
            }

            // Round 2 MUST echo back extra_content.google.thought_signature on the assistant tool call
            Assert.Contains("sig_abc_123", body, StringComparison.Ordinal);
            Assert.Contains("extra_content", body, StringComparison.Ordinal);
            return SseResponse("""{"choices":[{"delta":{"content":"Today looks clear."}}]}""");
        });

        await using var baseFactory = new TestAppFactory();
        using var client = CreateChatClient(baseFactory, handler);
        await SetupOwnerAsync(client);

        var p = await (await client.PostAsJsonAsync(
            "/api/ai/providers",
            new CreateAiProviderRequest("Gemini", "https://generativelanguage.googleapis.com/v1beta/openai", "sk-g")))
            .Content.ReadFromJsonAsync<AiProviderDto>();
        Assert.NotNull(p);
        await client.PostAsJsonAsync(
            "/api/ai/models",
            new CreateAiModelRequest(p.Id, "gemini-2.5-pro", "Gemini", SupportsVision: true));

        var res = await client.PostAsJsonAsync(
            "/api/chat/stream",
            new ChatStreamRequest([new ChatMessageInput("user", "What is on my day today?")]));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var sse = await res.Content.ReadAsStringAsync();
        Assert.Contains("Today looks clear.", sse, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatStream_RespectsWebSearchToggleAndReadOnlyMode()
    {
        var handler = new FakeOpenAiHandler((_, body, idx) =>
        {
            if (idx == 0)
            {
                using var doc = JsonDocument.Parse(body);
                var toolNames = doc.RootElement.GetProperty("tools")
                    .EnumerateArray()
                    .Select(t => t.GetProperty("function").GetProperty("name").GetString())
                    .ToHashSet(StringComparer.Ordinal);

                Assert.DoesNotContain("web_search", toolNames);
                Assert.DoesNotContain("create_note", toolNames);
                Assert.DoesNotContain("create_course", toolNames);
                Assert.DoesNotContain("create_activity_items", toolNames);
                Assert.Contains("get_day", toolNames);
                Assert.Contains("fetch_url", toolNames);

                // Even if the model tries to call a write tool anyway, server rejects it
                return SseResponse(
                    """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"c_ro","type":"function","function":{"name":"create_note","arguments":"{\"title\":\"X\",\"content\":\"Y\"}"}}]}}]}""");
            }

            Assert.Contains("Read-only", body, StringComparison.OrdinalIgnoreCase);
            return SseResponse("""{"choices":[{"delta":{"content":"Cannot write in read-only mode."}}]}""");
        });

        await using var baseFactory = new TestAppFactory();
        using var client = CreateChatClient(baseFactory, handler);
        await SetupOwnerAsync(client);

        var p = await (await client.PostAsJsonAsync(
            "/api/ai/providers",
            new CreateAiProviderRequest("P", "https://text.example.test/v1", "sk-1")))
            .Content.ReadFromJsonAsync<AiProviderDto>();
        Assert.NotNull(p);
        await client.PostAsJsonAsync(
            "/api/ai/models",
            new CreateAiModelRequest(p.Id, "m1", "M1", SupportsVision: false));

        var res = await client.PostAsJsonAsync(
            "/api/chat/stream",
            new ChatStreamRequest(
                [new ChatMessageInput("user", "Create a note")],
                EnableWebSearch: false,
                AllowWriteTools: false));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var sse = await res.Content.ReadAsStringAsync();
        Assert.Contains("Cannot write in read-only mode.", sse, StringComparison.Ordinal);
    }
}

