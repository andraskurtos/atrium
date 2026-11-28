using System.Security.Claims;
using System.Text.Json;
using Atrium.Api.Auth;
using Atrium.Api.Chat;
using Atrium.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Atrium.Api.Endpoints;

public static class ChatEndpoints
{
    private const int MaxMessages = 40;
    private const int MaxMessageChars = 16_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat/stream", async (
            ChatStreamRequest request,
            HttpContext httpContext,
            ClaimsPrincipal principal,
            AppDbContext db,
            SecretProtector secretProtector,
            OpenAiChatService chatService,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            if (request.Messages is null || request.Messages.Count == 0 || request.Messages.Count > MaxMessages)
            {
                return Results.BadRequest(new { error = $"Provide between 1 and {MaxMessages} chat messages." });
            }

            var normalizedMessages = new List<ChatMessageInput>(request.Messages.Count);
            foreach (var msg in request.Messages)
            {
                var role = (msg.Role ?? string.Empty).Trim().ToLowerInvariant();
                if (role is not ("user" or "assistant"))
                {
                    return Results.BadRequest(new { error = "Message role must be 'user' or 'assistant'." });
                }

                var content = (msg.Content ?? string.Empty).Trim();
                if (content.Length == 0 || content.Length > MaxMessageChars)
                {
                    return Results.BadRequest(new { error = $"Each message must be 1–{MaxMessageChars} characters." });
                }

                normalizedMessages.Add(new ChatMessageInput(role, content));
            }

            var prefs = await db.UserPreferences
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId.Value, ct);

            if (prefs is null
                || string.IsNullOrWhiteSpace(prefs.AiInferenceUrl)
                || string.IsNullOrWhiteSpace(prefs.AiModel))
            {
                return Results.BadRequest(new
                {
                    error = "AI Assistant is not configured yet. Add an Inference URL and Model name in Account → AI Assistant.",
                });
            }

            string? courseName = null;
            if (request.CourseId.HasValue)
            {
                var course = await db.UniversityCourses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == request.CourseId.Value && c.UserId == userId.Value, ct);
                if (course is null)
                {
                    return Results.NotFound(new { error = "Selected course was not found." });
                }

                courseName = string.IsNullOrWhiteSpace(course.Code) ? course.Name : $"{course.Code} · {course.Name}";
            }

            var apiKey = secretProtector.Unprotect(prefs.AiApiKeyEncrypted);

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers["X-Accel-Buffering"] = "no";

            async Task WriteEventAsync(object evt)
            {
                var line = "data: " + JsonSerializer.Serialize(evt, JsonOptions) + "\n\n";
                await httpContext.Response.WriteAsync(line, ct);
                await httpContext.Response.Body.FlushAsync(ct);
            }

            await chatService.StreamChatAsync(
                principal,
                userId.Value,
                prefs.AiInferenceUrl,
                prefs.AiModel,
                apiKey,
                normalizedMessages,
                request.CourseId,
                courseName,
                WriteEventAsync,
                ct);

            return Results.Empty;
        }).RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);
    }
}

