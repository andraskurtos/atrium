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
}

