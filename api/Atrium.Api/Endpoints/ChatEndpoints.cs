using System.Security.Claims;
using System.Text.Json;
using Atrium.Api.Auth;
using Atrium.Api.Chat;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Atrium.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Atrium.Api.Endpoints;

public static class ChatEndpoints
{
    private const int MaxMessages = 40;
    private const int MaxMessageChars = 16_000;
    private const int MaxThreadTitleLength = 80;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/chat")
            .RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapGet("/threads", async (
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var threads = await db.ChatThreads
                .AsNoTracking()
                .Include(t => t.Course)
                .Where(t => t.UserId == userId.Value)
                .ToListAsync(ct);

            var threadIds = threads.Select(t => t.Id).ToList();
            var counts = threadIds.Count > 0
                ? await db.ChatMessages
                    .AsNoTracking()
                    .Where(m => m.UserId == userId.Value && threadIds.Contains(m.ThreadId))
                    .GroupBy(m => m.ThreadId)
                    .Select(g => new { ThreadId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.ThreadId, x => x.Count, ct)
                : new Dictionary<Guid, int>();

            var result = threads
                .OrderByDescending(t => t.UpdatedAt)
                .Take(100)
                .Select(t => ToSummaryDto(t, counts.GetValueOrDefault(t.Id, 0)))
                .ToList();

            return Results.Ok(result);
        });

        group.MapGet("/threads/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var thread = await db.ChatThreads
                .AsNoTracking()
                .Include(t => t.Course)
                .Include(t => t.Messages)
                    .ThenInclude(m => m.Attachments)
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId.Value, ct);

            if (thread is null) return Results.NotFound();

            var messages = thread.Messages
                .OrderBy(m => m.CreatedAt)
                .Select(ToMessageDto)
                .ToList();

            return Results.Ok(new ChatThreadDetailDto(
                thread.Id,
                thread.CourseId,
                FormatCourseLabel(thread.Course),
                thread.Title,
                thread.CreatedAt,
                thread.UpdatedAt,
                messages,
                thread.LastModelId));
        });

        group.MapDelete("/threads/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            AppDbContext db,
            ChatAttachmentService attachmentService,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var thread = await db.ChatThreads
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId.Value, ct);

            if (thread is null) return Results.NotFound();

            await attachmentService.DeleteThreadFilesAsync(userId.Value, thread.Id, ct);
            db.ChatThreads.Remove(thread);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/attachments", async (
            HttpRequest httpRequest,
            ClaimsPrincipal principal,
            AppDbContext db,
            ChatAttachmentService attachmentService,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            if (!httpRequest.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Expected multipart/form-data upload." });
            }

            var form = await httpRequest.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? (form.Files.Count > 0 ? form.Files[0] : null);
            if (file is null)
            {
                return Results.BadRequest(new { error = "No file was uploaded." });
            }

            var userTz = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId.Value)
                .Select(u => u.Timezone)
                .FirstOrDefaultAsync(ct) ?? "UTC";

            var result = await attachmentService.UploadAsync(userId.Value, file, userTz, ct);
            if (!result.Success || result.Attachment is null)
            {
                return Results.BadRequest(new { error = result.Error ?? "Upload failed." });
            }

            return Results.Created(result.Attachment.Url, result.Attachment);
        }).DisableAntiforgery();

        group.MapGet("/attachments/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            AppDbContext db,
            IAttachmentStorage storage,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var att = await db.ChatAttachments
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId.Value, ct);
            if (att is null) return Results.NotFound();

            var stream = await storage.OpenReadAsync(att.StorageKey, ct);
            if (stream is null) return Results.NotFound();

            return Results.File(stream, att.ContentType, att.FileName);
        });

        group.MapPost("/stream", async (
            ChatStreamRequest request,
            HttpContext httpContext,
            ClaimsPrincipal principal,
            AppDbContext db,
            AiConfigService aiConfigService,
            ChatAttachmentService attachmentService,
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

            var attCheck = await attachmentService.ValidateAttachmentIdsAsync(
                userId.Value,
                request.ThreadId,
                request.AttachmentIds,
                ct);
            if (!attCheck.Success)
            {
                return Results.BadRequest(new { error = attCheck.Error ?? "Invalid attachment IDs." });
            }

            var resolvedModel = await aiConfigService.ResolveModelForChatAsync(
                userId.Value,
                request.ModelId,
                attCheck.HasImages,
                ct);
            if (!resolvedModel.Success || resolvedModel.Model is null)
            {
                return Results.BadRequest(new
                {
                    error = resolvedModel.ErrorMessage ?? "AI Assistant is not configured yet.",
                });
            }

            ChatThread? thread = null;
            Guid? effectiveCourseId = request.CourseId;
            UniversityCourse? courseEntity = null;

            if (request.ThreadId.HasValue)
            {
                thread = await db.ChatThreads
                    .Include(t => t.Course)
                    .FirstOrDefaultAsync(t => t.Id == request.ThreadId.Value && t.UserId == userId.Value, ct);
                if (thread is null)
                {
                    return Results.NotFound(new { error = "Chat thread not found." });
                }

                effectiveCourseId = thread.CourseId;
                courseEntity = thread.Course;
            }
            else if (effectiveCourseId.HasValue)
            {
                courseEntity = await db.UniversityCourses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == effectiveCourseId.Value && c.UserId == userId.Value, ct);
                if (courseEntity is null)
                {
                    return Results.NotFound(new { error = "Selected course was not found." });
                }
            }

            var now = DateTimeOffset.UtcNow;
            var latestUserPrompt = normalizedMessages.LastOrDefault(m => m.Role == "user")?.Content ?? normalizedMessages[^1].Content;

            if (thread is null)
            {
                thread = new ChatThread
                {
                    Id = Guid.NewGuid(),
                    UserId = userId.Value,
                    CourseId = effectiveCourseId,
                    LastModelId = resolvedModel.UserSelectedModelId,
                    Title = BuildThreadTitle(latestUserPrompt),
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.ChatThreads.Add(thread);
            }
            else
            {
                thread.LastModelId = resolvedModel.UserSelectedModelId;
                thread.UpdatedAt = now;
            }

            var userMsg = new ChatMessageEntity
            {
                Id = Guid.NewGuid(),
                ThreadId = thread.Id,
                UserId = userId.Value,
                Role = "user",
                Content = latestUserPrompt,
                CreatedAt = now,
            };
            db.ChatMessages.Add(userMsg);
            await db.SaveChangesAsync(ct);

            if (request.AttachmentIds is { Count: > 0 })
            {
                await attachmentService.BindToMessageAsync(
                    userId.Value,
                    thread.Id,
                    userMsg.Id,
                    request.AttachmentIds,
                    ct);
            }

            var existingCount = await db.ChatMessages
                .AsNoTracking()
                .CountAsync(m => m.ThreadId == thread.Id && m.UserId == userId.Value, ct);

            var userTimezone = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId.Value)
                .Select(u => u.Timezone)
                .FirstOrDefaultAsync(ct) ?? "UTC";

            var courseLabel = FormatCourseLabel(courseEntity);

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

            await WriteEventAsync(new
            {
                type = "thread",
                thread = new ChatThreadSummaryDto(
                    thread.Id,
                    thread.CourseId,
                    courseLabel,
                    thread.Title,
                    thread.CreatedAt,
                    thread.UpdatedAt,
                    existingCount),
            });

            if (resolvedModel.SwitchedToVision)
            {
                await WriteEventAsync(new
                {
                    type = "status",
                    message = $"Switched to {resolvedModel.Model.DisplayName} for image input…",
                });
            }

            var summary = await chatService.StreamChatAsync(
                principal,
                userId.Value,
                resolvedModel.Model.BaseUrl,
                resolvedModel.Model.ModelId,
                resolvedModel.Model.ApiKey,
                normalizedMessages,
                effectiveCourseId,
                courseLabel,
                WriteEventAsync,
                ct,
                threadId: thread.Id,
                supportsVision: resolvedModel.Model.SupportsVision,
                userTimezone: userTimezone,
                enableWebSearch: request.EnableWebSearch,
                allowWriteTools: request.AllowWriteTools);

            var trimmedReply = summary.AssistantText.Trim();
            var trimmedReasoning = string.IsNullOrWhiteSpace(summary.ReasoningText) ? null : summary.ReasoningText.Trim();
            if (trimmedReply.Length > 0 || trimmedReasoning is not null)
            {
                var replyTime = DateTimeOffset.UtcNow;
                db.ChatMessages.Add(new ChatMessageEntity
                {
                    Id = Guid.NewGuid(),
                    ThreadId = thread.Id,
                    UserId = userId.Value,
                    Role = "assistant",
                    Content = trimmedReply,
                    Reasoning = trimmedReasoning,
                    CitationsJson = summary.Citations.Count > 0
                        ? JsonSerializer.Serialize(summary.Citations, JsonOptions)
                        : null,
                    CreatedAt = replyTime,
                });
                thread.UpdatedAt = replyTime;
                await db.SaveChangesAsync(CancellationToken.None);
            }

            return Results.Empty;
        });
    }

    private static string? FormatCourseLabel(UniversityCourse? course)
    {
        if (course is null) return null;
        return string.IsNullOrWhiteSpace(course.Code) ? course.Name : $"{course.Code} · {course.Name}";
    }

    private static string BuildThreadTitle(string prompt)
    {
        var collapsed = string.Join(' ', prompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length == 0) return "New Chat";
        return collapsed.Length <= MaxThreadTitleLength ? collapsed : collapsed[..(MaxThreadTitleLength - 1)].TrimEnd() + "…";
    }

    private static ChatThreadSummaryDto ToSummaryDto(ChatThread t, int messageCount) => new(
        t.Id,
        t.CourseId,
        FormatCourseLabel(t.Course),
        t.Title,
        t.CreatedAt,
        t.UpdatedAt,
        messageCount);

    private static ChatMessageDto ToMessageDto(ChatMessageEntity m)
    {
        IReadOnlyList<ChatCitationDto> citations = [];
        if (!string.IsNullOrWhiteSpace(m.CitationsJson))
        {
            try
            {
                citations = JsonSerializer.Deserialize<List<ChatCitationDto>>(m.CitationsJson, JsonOptions) ?? [];
            }
            catch (JsonException)
            {
                citations = [];
            }
        }

        var attachments = m.Attachments
            .OrderBy(a => a.CreatedAt)
            .Select(ChatAttachmentService.ToDto)
            .ToList();

        return new ChatMessageDto(m.Id, m.Role, m.Content, m.Reasoning, citations, m.CreatedAt, attachments);
    }
}
