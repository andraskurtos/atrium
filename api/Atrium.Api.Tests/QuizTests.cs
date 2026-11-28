using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Atrium.Api.Auth;
using Atrium.Api.Chat;
using Atrium.Api.University;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class QuizTests
{
    private const string OwnerPassword = "Atrium-Owner-Pass-2026!";

    private sealed class FakeOpenAiHandler(Func<HttpRequestMessage, string, int, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        private int callCount;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is not null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : string.Empty;
            var idx = Interlocked.Increment(ref callCount) - 1;
            return responder(request, body, idx);
        }
    }

    private static HttpResponseMessage SseResponse(params string[] dataLines)
    {
        var sb = new StringBuilder();
        foreach (var line in dataLines) sb.Append("data: ").Append(line).Append("\n\n");
        sb.Append("data: [DONE]\n\n");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sb.ToString(), Encoding.UTF8, "text/event-stream"),
        };
    }

    [Fact]
    public async Task Quizzes_CreateMultiplePerLectureAndCourse_RecordAttempts_AndDelete()
    {
        var handler = new FakeOpenAiHandler((_, _, idx) =>
        {
            if (idx == 0)
            {
                return SseResponse(
                    """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"q1","type":"function","function":{"name":"create_quiz","arguments":"{\"title\":\"Lecture 1 Quick Check\",\"description\":\"Core concepts\",\"questions\":[{\"prompt\":\"What is 2+2?\",\"options\":[\"3\",\"4\",\"5\",\"6\"],\"correctIndex\":1,\"explanation\":\"2+2 equals 4.\",\"sourceReference\":\"Slide 2\"},{\"prompt\":\"Primary color?\",\"options\":[\"Red\",\"Green\"],\"correctIndex\":0,\"explanation\":\"Red is primary.\"}]}"}}]}}]}""");
            }

            return SseResponse("""{"choices":[{"delta":{"content":"Quiz created!"}}]}""");
        });

        await using var baseFactory = new TestAppFactory();
        using var client = baseFactory.WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
                s.AddHttpClient(OpenAiChatService.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => handler)))
            .CreateClient();

        var session = await (await client.PostAsJsonAsync(
            "/api/auth/setup",
            new SetupRequest("andras", "andras@atrium.local", "András", OwnerPassword)))
            .Content.ReadFromJsonAsync<AuthSessionResponse>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var p = await (await client.PostAsJsonAsync(
            "/api/ai/providers",
            new CreateAiProviderRequest("P", "https://text.example.test/v1", "sk-1")))
            .Content.ReadFromJsonAsync<AiProviderDto>();
        Assert.NotNull(p);
        await client.PostAsJsonAsync(
            "/api/ai/models",
            new CreateAiModelRequest(p.Id, "m1", "M1", SupportsVision: false));

        var course = await (await client.PostAsJsonAsync(
            "/api/university/courses",
            new CreateCourseRequest("Algorithms", "CS201", "2026 Fall", null, null, 5, "#3b6ea5", "active", null,
                [new CreateActivityRequest("lecture", "Lectures")])))
            .Content.ReadFromJsonAsync<UniversityCourseDto>();
        Assert.NotNull(course);

        // Generate a course-scoped quiz via chat tool
        var streamRes = await client.PostAsJsonAsync(
            "/api/chat/stream",
            new ChatStreamRequest([new ChatMessageInput("user", "Generate a quiz")], CourseId: course.Id));
        Assert.Equal(HttpStatusCode.OK, streamRes.StatusCode);
        var sse = await streamRes.Content.ReadAsStringAsync();
        Assert.Contains("\"type\":\"mutation\"", sse, StringComparison.Ordinal);

        var list = await client.GetFromJsonAsync<List<UniversityQuizSummaryDto>>($"/api/university/courses/{course.Id}/quizzes");
        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Equal("Lecture 1 Quick Check", list[0].Title);
        Assert.Equal(2, list[0].QuestionCount);
        Assert.Null(list[0].BestScore);

        var detail = await client.GetFromJsonAsync<UniversityQuizDetailDto>($"/api/university/quizzes/{list[0].Id}");
        Assert.NotNull(detail);
        Assert.Equal(2, detail.Questions.Count);
        Assert.Equal("2+2 equals 4.", detail.Questions[0].Explanation);

        // Record two attempts (1/2 then 2/2)
        var a1 = await (await client.PostAsJsonAsync($"/api/university/quizzes/{detail.Id}/attempt", new RecordQuizAttemptRequest(1)))
            .Content.ReadFromJsonAsync<UniversityQuizSummaryDto>();
        Assert.NotNull(a1);
        Assert.Equal(1, a1.AttemptCount);
        Assert.Equal(1, a1.BestScore);

        var a2 = await (await client.PostAsJsonAsync($"/api/university/quizzes/{detail.Id}/attempt", new RecordQuizAttemptRequest(2)))
            .Content.ReadFromJsonAsync<UniversityQuizSummaryDto>();
        Assert.NotNull(a2);
        Assert.Equal(2, a2.AttemptCount);
        Assert.Equal(2, a2.BestScore);
        Assert.Equal(2, a2.LastScore);

        // Out-of-range score rejected
        var badAttempt = await client.PostAsJsonAsync($"/api/university/quizzes/{detail.Id}/attempt", new RecordQuizAttemptRequest(5));
        Assert.Equal(HttpStatusCode.BadRequest, badAttempt.StatusCode);

        // Delete quiz
        var del = await client.DeleteAsync($"/api/university/quizzes/{detail.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var afterDel = await client.GetFromJsonAsync<List<UniversityQuizSummaryDto>>($"/api/university/courses/{course.Id}/quizzes");
        Assert.NotNull(afterDel);
        Assert.Empty(afterDel);
    }
}

