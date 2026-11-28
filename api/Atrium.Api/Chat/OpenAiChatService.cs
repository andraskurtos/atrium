using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atrium.Api.Auth;
using Atrium.Api.Data;
using Atrium.Api.Knowledge;
using Microsoft.EntityFrameworkCore;

namespace Atrium.Api.Chat;

public sealed partial class OpenAiChatService(
    IHttpClientFactory httpClientFactory,
    KnowledgeReadService knowledge,
    KnowledgeWriteService writeService,
    AppDbContext db,
    ILogger<OpenAiChatService> logger)
{
    public const string HttpClientName = "AtriumOpenAiChat";
    private const int MaxToolRounds = 12;
    private const int MaxToolOutputChars = 24_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ChatStreamSummary> StreamChatAsync(
        ClaimsPrincipal principal,
        Guid userId,
        string inferenceBaseUrl,
        string model,
        string? apiKey,
        IReadOnlyList<ChatMessageInput> messages,
        Guid? courseId,
        string? courseName,
        Func<object, Task> writeEventAsync,
        CancellationToken ct)
    {
        var citations = new Dictionary<(string SourceType, Guid Id), ChatCitationDto>();
        var totalAssistantText = new StringBuilder();
        var totalReasoningText = new StringBuilder();
        var conversation = new List<JsonObject>
        {
            new()
            {
                ["role"] = "system",
                ["content"] = BuildSystemPrompt(courseId, courseName),
            },
        };

        foreach (var msg in messages)
        {
            conversation.Add(new JsonObject
            {
                ["role"] = msg.Role,
                ["content"] = msg.Content,
            });
        }

        var tools = BuildToolsSchema(courseId.HasValue);
        var endpointUrl = ResolveCompletionsUrl(inferenceBaseUrl);
        var client = httpClientFactory.CreateClient(HttpClientName);
        var needsParagraphBreak = false;

        async Task EmitTrackedEventAsync(object evt)
        {
            if (evt is DeltaEvent delta)
            {
                if (needsParagraphBreak)
                {
                    needsParagraphBreak = false;
                    totalAssistantText.Append("\n\n");
                    await writeEventAsync(new DeltaEvent("\n\n"));
                }

                totalAssistantText.Append(delta.Text);
                await writeEventAsync(delta);
                return;
            }

            if (evt is ReasoningEvent reasoning)
            {
                totalReasoningText.Append(reasoning.Text);
                await writeEventAsync(reasoning);
                return;
            }

            await writeEventAsync(evt);
        }

        for (var round = 0; round < MaxToolRounds; round++)
        {
            var isLastRound = round == MaxToolRounds - 1;
            var payload = new JsonObject
            {
                ["model"] = model,
                ["messages"] = new JsonArray(conversation.Select(m => m.DeepClone()).ToArray()),
                ["stream"] = true,
            };

            if (!isLastRound)
            {
                payload["tools"] = tools.DeepClone();
                payload["tool_choice"] = "auto";
            }

            var roundResult = await SendStreamingRoundAsync(client, endpointUrl, apiKey, payload, EmitTrackedEventAsync, ct);

            // If the provider rejected tool-calling on the first round, gracefully fall back to single-shot RAG
            if (!roundResult.Success
                && round == 0
                && !isLastRound
                && roundResult.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound)
            {
                await writeEventAsync(new { type = "status", message = "Searching knowledge base…" });
                var ragContext = await BuildFallbackRagContextAsync(principal, messages[^1].Content, courseId, citations, ct);
                var fallbackMessages = new JsonArray
                {
                    new JsonObject
                    {
                        ["role"] = "system",
                        ["content"] = BuildSystemPrompt(courseId, courseName) + "\n\nRelevant context from Atrium:\n" + ragContext,
                    },
                };
                foreach (var msg in messages)
                {
                    fallbackMessages.Add(new JsonObject
                    {
                        ["role"] = msg.Role,
                        ["content"] = msg.Content,
                    });
                }

                var fallbackPayload = new JsonObject
                {
                    ["model"] = model,
                    ["messages"] = fallbackMessages,
                    ["stream"] = true,
                };

                var fallbackResult = await SendStreamingRoundAsync(client, endpointUrl, apiKey, fallbackPayload, EmitTrackedEventAsync, ct);
                if (!fallbackResult.Success)
                {
                    await writeEventAsync(new { type = "error", message = fallbackResult.ErrorMessage ?? "Inference request failed." });
                    return new ChatStreamSummary(
                        totalAssistantText.ToString(),
                        totalReasoningText.Length > 0 ? totalReasoningText.ToString() : null,
                        citations.Values.ToList(),
                        HadError: true);
                }

                if (citations.Count > 0)
                {
                    await writeEventAsync(new { type = "citations", citations = citations.Values.ToList() });
                }

                await writeEventAsync(new { type = "done" });
                return new ChatStreamSummary(
                    totalAssistantText.ToString(),
                    totalReasoningText.Length > 0 ? totalReasoningText.ToString() : null,
                    citations.Values.ToList(),
                    HadError: false);
            }

            if (!roundResult.Success)
            {
                await writeEventAsync(new { type = "error", message = roundResult.ErrorMessage ?? "Inference request failed." });
                return new ChatStreamSummary(
                    totalAssistantText.ToString(),
                    totalReasoningText.Length > 0 ? totalReasoningText.ToString() : null,
                    citations.Values.ToList(),
                    HadError: true);
            }

            if (roundResult.ToolCalls.Count == 0)
            {
                if (citations.Count > 0)
                {
                    await writeEventAsync(new { type = "citations", citations = citations.Values.ToList() });
                }

                await writeEventAsync(new { type = "done" });
                return new ChatStreamSummary(
                    totalAssistantText.ToString(),
                    totalReasoningText.Length > 0 ? totalReasoningText.ToString() : null,
                    citations.Values.ToList(),
                    HadError: false);
            }

            // Record assistant tool_calls turn
            var toolCallsArray = new JsonArray();
            foreach (var tc in roundResult.ToolCalls)
            {
                toolCallsArray.Add(new JsonObject
                {
                    ["id"] = tc.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tc.Name,
                        ["arguments"] = tc.Arguments,
                    },
                });
            }

            var assistantTurn = new JsonObject
            {
                ["role"] = "assistant",
                ["tool_calls"] = toolCallsArray,
            };
            if (!string.IsNullOrEmpty(roundResult.AssistantText))
            {
                assistantTurn["content"] = roundResult.AssistantText;
                if (!roundResult.AssistantText.EndsWith("\n\n", StringComparison.Ordinal))
                {
                    needsParagraphBreak = true;
                }
            }

            conversation.Add(assistantTurn);

            foreach (var tc in roundResult.ToolCalls)
            {
                await writeEventAsync(new { type = "status", message = DescribeToolStatus(tc.Name, courseName) });
                var toolOutput = await ExecuteToolAsync(principal, userId, tc.Name, tc.Arguments, courseId, citations, writeEventAsync, ct);
                if (toolOutput.Length > MaxToolOutputChars)
                {
                    toolOutput = toolOutput[..MaxToolOutputChars] + "\n...[truncated]";
                }

                conversation.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = tc.Id,
                    ["content"] = toolOutput,
                });
            }
        }

        if (citations.Count > 0)
        {
            await writeEventAsync(new { type = "citations", citations = citations.Values.ToList() });
        }

        await writeEventAsync(new { type = "done" });
        return new ChatStreamSummary(
            totalAssistantText.ToString(),
            totalReasoningText.Length > 0 ? totalReasoningText.ToString() : null,
            citations.Values.ToList(),
            HadError: false);
    }

    private sealed record DeltaEvent(string Text)
    {
        public string Type { get; } = "delta";
    }

    private sealed record ReasoningEvent(string Text)
    {
        public string Type { get; } = "reasoning";
    }

    private sealed record AccumulatedToolCall(string Id, string Name, string Arguments);

    private sealed record StreamRoundResult(
        bool Success,
        HttpStatusCode? StatusCode,
        string? ErrorMessage,
        string AssistantText,
        List<AccumulatedToolCall> ToolCalls);

    private async Task<StreamRoundResult> SendStreamingRoundAsync(
        HttpClient client,
        string endpointUrl,
        string? apiKey,
        JsonObject payload,
        Func<object, Task> writeEventAsync,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, endpointUrl);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        req.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        HttpResponseMessage res;
        try
        {
            res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            LogInferenceRequestFailed(logger, ex);
            return new StreamRoundResult(false, null, $"Could not reach inference server: {ex.Message}", string.Empty, []);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new StreamRoundResult(false, HttpStatusCode.GatewayTimeout, "Inference server timed out.", string.Empty, []);
        }

        using (res)
        {
            if (!res.IsSuccessStatusCode)
            {
                var errBody = await res.Content.ReadAsStringAsync(ct);
                var snippet = errBody.Length > 300 ? errBody[..300] : errBody;
                return new StreamRoundResult(
                    false,
                    res.StatusCode,
                    $"Inference server returned {(int)res.StatusCode}: {snippet}",
                    string.Empty,
                    []);
            }

            var contentType = res.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!contentType.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
            {
                // Some OpenAI-compatible servers ignore stream:true and return a single JSON completion
                var json = await res.Content.ReadAsStringAsync(ct);
                return await ParseNonStreamingResponseAsync(json, writeEventAsync);
            }

            var assistantText = new StringBuilder();
            var toolBuilders = new SortedDictionary<int, (string Id, string Name, StringBuilder Args)>();
            var thinkSplitter = new ThinkTagSplitter();

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (await reader.ReadLineAsync(ct) is { } line)
            {
                ct.ThrowIfCancellationRequested();
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                var data = line["data:".Length..].Trim();
                if (string.IsNullOrEmpty(data) || data == "[DONE]") continue;

                try
                {
                    using var doc = JsonDocument.Parse(data);
                    if (!doc.RootElement.TryGetProperty("choices", out var choices)
                        || choices.ValueKind != JsonValueKind.Array
                        || choices.GetArrayLength() == 0)
                    {
                        continue;
                    }

                    var choice = choices[0];
                    if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var reasoningChunk = ExtractReasoningString(delta);
                    if (!string.IsNullOrEmpty(reasoningChunk))
                    {
                        await writeEventAsync(new ReasoningEvent(reasoningChunk));
                    }

                    if (delta.TryGetProperty("content", out var contentEl)
                        && contentEl.ValueKind == JsonValueKind.String)
                    {
                        var chunk = contentEl.GetString();
                        if (!string.IsNullOrEmpty(chunk))
                        {
                            foreach (var (isReasoning, textPart) in thinkSplitter.Feed(chunk))
                            {
                                if (isReasoning)
                                {
                                    await writeEventAsync(new ReasoningEvent(textPart));
                                }
                                else
                                {
                                    assistantText.Append(textPart);
                                    await writeEventAsync(new DeltaEvent(textPart));
                                }
                            }
                        }
                    }

                    if (delta.TryGetProperty("tool_calls", out var tcArray) && tcArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tcEl in tcArray.EnumerateArray())
                        {
                            var idx = tcEl.TryGetProperty("index", out var idxEl) && idxEl.TryGetInt32(out var parsedIdx)
                                ? parsedIdx
                                : 0;

                            if (!toolBuilders.TryGetValue(idx, out var builder))
                            {
                                builder = (string.Empty, string.Empty, new StringBuilder());
                            }

                            if (tcEl.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                            {
                                var idVal = idEl.GetString();
                                if (!string.IsNullOrEmpty(idVal)) builder.Id = idVal;
                            }

                            if (tcEl.TryGetProperty("function", out var fnEl) && fnEl.ValueKind == JsonValueKind.Object)
                            {
                                if (fnEl.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                                {
                                    var nameVal = nameEl.GetString();
                                    if (!string.IsNullOrEmpty(nameVal)) builder.Name += nameVal;
                                }

                                if (fnEl.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.String)
                                {
                                    var argsVal = argsEl.GetString();
                                    if (!string.IsNullOrEmpty(argsVal)) builder.Args.Append(argsVal);
                                }
                            }

                            toolBuilders[idx] = builder;
                        }
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed SSE frame
                }
            }

            foreach (var (isReasoning, textPart) in thinkSplitter.Flush())
            {
                if (isReasoning)
                {
                    await writeEventAsync(new ReasoningEvent(textPart));
                }
                else
                {
                    assistantText.Append(textPart);
                    await writeEventAsync(new DeltaEvent(textPart));
                }
            }

            var finalizedCalls = toolBuilders
                .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value.Name))
                .Select((kvp, i) => new AccumulatedToolCall(
                    string.IsNullOrWhiteSpace(kvp.Value.Id) ? $"call_{i}" : kvp.Value.Id,
                    kvp.Value.Name,
                    kvp.Value.Args.Length == 0 ? "{}" : kvp.Value.Args.ToString()))
                .ToList();

            return new StreamRoundResult(true, res.StatusCode, null, assistantText.ToString(), finalizedCalls);
        }
    }

    private static async Task<StreamRoundResult> ParseNonStreamingResponseAsync(
        string json,
        Func<object, Task> writeEventAsync)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
            var text = message.TryGetProperty("content", out var cEl) && cEl.ValueKind == JsonValueKind.String
                ? cEl.GetString() ?? string.Empty
                : string.Empty;

            var calls = new List<AccumulatedToolCall>();
            if (message.TryGetProperty("tool_calls", out var tcEl) && tcEl.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var item in tcEl.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? $"call_{i}" : $"call_{i}";
                    var fn = item.GetProperty("function");
                    var name = fn.GetProperty("name").GetString() ?? string.Empty;
                    var args = fn.TryGetProperty("arguments", out var aEl) ? aEl.GetString() ?? "{}" : "{}";
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        calls.Add(new AccumulatedToolCall(id, name, args));
                    }

                    i++;
                }
            }

            var reasoning = ExtractReasoningString(message);
            if (!string.IsNullOrEmpty(reasoning))
            {
                await writeEventAsync(new ReasoningEvent(reasoning));
            }

            var cleanedText = new StringBuilder();
            if (calls.Count == 0 && !string.IsNullOrEmpty(text))
            {
                var splitter = new ThinkTagSplitter();
                foreach (var (isReasoning, part) in splitter.Feed(text).Concat(splitter.Flush()))
                {
                    if (isReasoning)
                    {
                        await writeEventAsync(new ReasoningEvent(part));
                    }
                    else
                    {
                        cleanedText.Append(part);
                        await writeEventAsync(new DeltaEvent(part));
                    }
                }
            }

            return new StreamRoundResult(true, HttpStatusCode.OK, null, cleanedText.Length > 0 ? cleanedText.ToString() : text, calls);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new StreamRoundResult(false, HttpStatusCode.BadGateway, "Unexpected response format from inference server.", string.Empty, []);
        }
    }

    private async Task<string> ExecuteToolAsync(
        ClaimsPrincipal principal,
        Guid userId,
        string toolName,
        string argumentsJson,
        Guid? lockedCourseId,
        Dictionary<(string SourceType, Guid Id), ChatCitationDto> citations,
        Func<object, Task> writeEventAsync,
        CancellationToken ct)
    {
        JsonDocument argsDoc;
        try
        {
            argsDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { error = "Invalid JSON arguments." }, JsonOptions);
        }

        using (argsDoc)
        {
            var root = argsDoc.RootElement;

            if (lockedCourseId.HasValue)
            {
                return toolName switch
                {
                    "get_course" => await RunGetCourseAsync(principal, lockedCourseId.Value, ct),
                    "search_knowledge" => await RunSearchKnowledgeAsync(principal, root, lockedCourseId, citations, ct),
                    "read_note" => await RunReadNoteAsync(principal, root, lockedCourseId, citations, ct),
                    "list_artifacts" => await RunListArtifactsAsync(principal, userId, root, lockedCourseId, ct),
                    "read_artifact_text" => await RunReadArtifactTextAsync(principal, userId, root, lockedCourseId, citations, ct),
                    "create_note" => await RunCreateNoteAsync(principal, userId, root, lockedCourseId, citations, writeEventAsync, ct),
                    "update_note" => await RunUpdateNoteAsync(principal, userId, root, lockedCourseId, citations, writeEventAsync, ct),
                    "extract_pdf_images" => await RunExtractPdfImagesAsync(principal, userId, root, lockedCourseId, ct),
                    "create_activity_item" => await RunCreateActivityItemAsync(principal, userId, root, lockedCourseId, writeEventAsync, ct),
                    _ => JsonSerializer.Serialize(new { error = "Tool is not available in course-scoped chat." }, JsonOptions),
                };
            }

            return toolName switch
            {
                "get_day" => SerializeResult(await knowledge.GetDayAsync(principal, false, GetOptionalString(root, "date"), ct)),
                "list_events" => SerializeResult(await knowledge.ListEventsAsync(
                    principal,
                    false,
                    GetOptionalDateTimeOffset(root, "from"),
                    GetOptionalDateTimeOffset(root, "to"),
                    GetOptionalGuid(root, "calendarId"),
                    ct)),
                "list_courses" => SerializeResult(await knowledge.ListCoursesAsync(
                    principal,
                    false,
                    GetOptionalString(root, "semester"),
                    GetOptionalString(root, "status"),
                    ct)),
                "get_course" => await RunGetCourseAsync(principal, GetOptionalGuid(root, "courseId"), ct),
                "list_notes_tree" => SerializeResult(await knowledge.ListNotesTreeAsync(
                    principal,
                    false,
                    GetOptionalBool(root, "includeUniversity", true),
                    GetOptionalBool(root, "includeEvents", true),
                    GetOptionalBool(root, "includeDaily", true),
                    ct)),
                "read_note" => await RunReadNoteAsync(principal, root, null, citations, ct),
                "list_artifacts" => await RunListArtifactsAsync(principal, userId, root, null, ct),
                "read_artifact_text" => await RunReadArtifactTextAsync(principal, userId, root, null, citations, ct),
                "search_knowledge" => await RunSearchKnowledgeAsync(principal, root, null, citations, ct),
                "create_note" => await RunCreateNoteAsync(principal, userId, root, null, citations, writeEventAsync, ct),
                "update_note" => await RunUpdateNoteAsync(principal, userId, root, null, citations, writeEventAsync, ct),
                "extract_pdf_images" => await RunExtractPdfImagesAsync(principal, userId, root, null, ct),
                "create_checklist_item" => await RunCreateChecklistItemAsync(principal, root, writeEventAsync, ct),
                "toggle_checklist_item" => await RunToggleChecklistItemAsync(principal, root, writeEventAsync, ct),
                "create_event" => await RunCreateEventAsync(principal, root, writeEventAsync, ct),
                "create_course" => await RunCreateCourseAsync(principal, root, writeEventAsync, ct),
                "create_activity" => await RunCreateActivityAsync(principal, root, writeEventAsync, ct),
                "create_activity_item" => await RunCreateActivityItemAsync(principal, userId, root, null, writeEventAsync, ct),
                _ => JsonSerializer.Serialize(new { error = $"Unknown tool: {toolName}" }, JsonOptions),
            };
        }
    }

    private async Task<string> RunCreateNoteAsync(
        ClaimsPrincipal principal,
        Guid userId,
        JsonElement root,
        Guid? lockedCourseId,
        Dictionary<(string SourceType, Guid Id), ChatCitationDto> citations,
        Func<object, Task> writeEventAsync,
        CancellationToken ct)
    {
        var title = GetOptionalString(root, "title");
        var content = GetOptionalString(root, "content");
        var folderId = lockedCourseId.HasValue ? null : GetOptionalGuid(root, "folderId");
        var courseId = lockedCourseId ?? GetOptionalGuid(root, "universityCourseId");
        var itemId = GetOptionalGuid(root, "universityActivityItemId");
        var eventId = lockedCourseId.HasValue ? null : GetOptionalGuid(root, "calendarEventId");
        var occStart = lockedCourseId.HasValue ? null : GetOptionalDateTimeOffset(root, "occurrenceStartAt");
        var dailyDate = lockedCourseId.HasValue ? null : GetOptionalString(root, "dailyDate");

        if (lockedCourseId.HasValue && itemId.HasValue)
        {
            var belongs = await db.UniversityActivityItems.AnyAsync(
                i => i.Id == itemId.Value && i.UserId == userId && i.CourseId == lockedCourseId.Value,
                ct);
            if (!belongs)
            {
                return JsonSerializer.Serialize(new { error = "Access denied: lecture item does not belong to the selected course." }, JsonOptions);
            }
        }

        var result = await writeService.CreateNoteAsync(
            principal,
            false,
            title,
            content,
            folderId,
            courseId,
            itemId,
            eventId,
            occStart,
            dailyDate,
            ct);
        if (result.IsSuccess && result.Value is not null)
        {
            citations[("note", result.Value.Id)] = new ChatCitationDto("note", result.Value.Id, result.Value.Title, result.Value.LinkLabel, null);
            await writeEventAsync(new { type = "mutation", domain = "notes", id = result.Value.Id, title = result.Value.Title });
        }

        return SerializeResult(result);
    }

    private async Task<string> RunUpdateNoteAsync(
        ClaimsPrincipal principal,
        Guid userId,
        JsonElement root,
        Guid? lockedCourseId,
        Dictionary<(string SourceType, Guid Id), ChatCitationDto> citations,
        Func<object, Task> writeEventAsync,
        CancellationToken ct)
    {
        var noteId = GetOptionalGuid(root, "noteId");
        if (!noteId.HasValue) return JsonSerializer.Serialize(new { error = "noteId is required." }, JsonOptions);

        if (lockedCourseId.HasValue)
        {
            var belongs = await db.Notes.AnyAsync(
                n => n.Id == noteId.Value && n.UserId == userId && n.DeletedAt == null && n.UniversityCourseId == lockedCourseId.Value,
                ct);
            if (!belongs)
            {
                return JsonSerializer.Serialize(new { error = "Access denied: note does not belong to the selected course." }, JsonOptions);
            }
        }

        var result = await writeService.UpdateNoteAsync(
            principal,
            false,
            noteId.Value,
            GetOptionalString(root, "title"),
            GetOptionalString(root, "content"),
            ct);
        if (result.IsSuccess && result.Value is not null)
        {
            citations[("note", result.Value.Id)] = new ChatCitationDto("note", result.Value.Id, result.Value.Title, result.Value.LinkLabel, null);
            await writeEventAsync(new { type = "mutation", domain = "notes", id = result.Value.Id, title = result.Value.Title });
        }

        return SerializeResult(result);
    }

    private async Task<string> RunExtractPdfImagesAsync(
        ClaimsPrincipal principal,
        Guid userId,
        JsonElement root,
        Guid? lockedCourseId,
        CancellationToken ct)
    {
        var attachmentId = GetOptionalGuid(root, "attachmentId");
        var noteId = GetOptionalGuid(root, "noteId");
        if (!attachmentId.HasValue || !noteId.HasValue)
        {
            return JsonSerializer.Serialize(new { error = "attachmentId and noteId are required." }, JsonOptions);
        }

        if (lockedCourseId.HasValue)
        {
            var noteInCourse = await db.Notes.AnyAsync(
                n => n.Id == noteId.Value && n.UserId == userId && n.DeletedAt == null && n.UniversityCourseId == lockedCourseId.Value,
                ct);
            var attInCourse = await db.Attachments.AnyAsync(
                a => a.Id == attachmentId.Value && a.UserId == userId && a.DeletedAt == null && a.UniversityCourseId == lockedCourseId.Value,
                ct);
            if (!noteInCourse || !attInCourse)
            {
                return JsonSerializer.Serialize(new { error = "Access denied: PDF or note does not belong to the selected course." }, JsonOptions);
            }
        }

        var startPage = root.TryGetProperty("startPage", out var spEl) && spEl.TryGetInt32(out var sp) ? sp : (int?)null;
        var endPage = root.TryGetProperty("endPage", out var epEl) && epEl.TryGetInt32(out var ep) ? ep : (int?)null;
        var maxImages = GetOptionalInt(root, "maxImages", 4);

        var result = await writeService.ExtractPdfImagesAsync(principal, false, attachmentId.Value, noteId.Value, startPage, endPage, maxImages, ct);
        return SerializeResult(result);
    }

    private async Task<string> RunCreateChecklistItemAsync(
        ClaimsPrincipal principal,
        JsonElement root,
        Func<object, Task> writeEventAsync,
        CancellationToken ct)
    {
        var result = await writeService.CreateChecklistItemAsync(
            principal,
            false,
            GetOptionalString(root, "date"),
            GetOptionalString(root, "title"),
            GetOptionalString(root, "dueTime"),
            ct);
        if (result.IsSuccess && result.Value is not null)
        {
            await writeEventAsync(new { type = "mutation", domain = "tasks", id = result.Value.Id, title = result.Value.Title });
        }

        return SerializeResult(result);
    }

    private async Task<string> RunToggleChecklistItemAsync(
        ClaimsPrincipal principal,
        JsonElement root,
        Func<object, Task> writeEventAsync,
        CancellationToken ct)
    {
        var itemId = GetOptionalGuid(root, "itemId");
        if (!itemId.HasValue) return JsonSerializer.Serialize(new { error = "itemId is required." }, JsonOptions);
        var result = await writeService.ToggleChecklistItemAsync(
            principal,
            false,
            itemId.Value,
            GetOptionalBool(root, "isCompleted", true),
            ct);
        if (result.IsSuccess && result.Value is not null)
        {
            await writeEventAsync(new { type = "mutation", domain = "tasks", id = result.Value.Id, title = result.Value.Title });
        }

        return SerializeResult(result);
    }

    private async Task<string> RunCreateEventAsync(
        ClaimsPrincipal principal,
        JsonElement root,
        Func<object, Task> writeEventAsync,
        CancellationToken ct)
    {
        var startAt = GetOptionalDateTimeOffset(root, "startAt");
        var endAt = GetOptionalDateTimeOffset(root, "endAt");
        if (!startAt.HasValue || !endAt.HasValue)
        {
            return JsonSerializer.Serialize(new { error = "startAt and endAt ISO-8601 timestamps are required." }, JsonOptions);
        }

        var result = await writeService.CreateEventAsync(
            principal,
            false,
            GetOptionalString(root, "title"),
            startAt.Value,
            endAt.Value,
            GetOptionalGuid(root, "calendarId"),
            GetOptionalBool(root, "isAllDay", false),
            GetOptionalString(root, "location"),
            GetOptionalString(root, "description"),
            GetOptionalString(root, "recurrenceRule"),
            ct);
        if (result.IsSuccess && result.Value is not null)
        {
            await writeEventAsync(new { type = "mutation", domain = "calendar", id = result.Value.Id, title = result.Value.Title });
        }

        return SerializeResult(result);
    }

    private async Task<string> RunCreateCourseAsync(
        ClaimsPrincipal principal,
        JsonElement root,
        Func<object, Task> writeEventAsync,
        CancellationToken ct)
    {
        decimal? credits = root.TryGetProperty("credits", out var crEl) && crEl.TryGetDecimal(out var cr) ? cr : null;
        var result = await writeService.CreateCourseAsync(
            principal,
            false,
            GetOptionalString(root, "name"),
            GetOptionalString(root, "code"),
            GetOptionalString(root, "semester"),
            GetOptionalString(root, "instructor"),
            credits,
            GetOptionalString(root, "color"),
            GetOptionalString(root, "description"),
            ct);
        if (result.IsSuccess && result.Value is not null)
        {
            await writeEventAsync(new { type = "mutation", domain = "university", id = result.Value.Id, title = result.Value.Name });
        }

        return SerializeResult(result);
    }

    private async Task<string> RunCreateActivityAsync(
        ClaimsPrincipal principal,
        JsonElement root,
        Func<object, Task> writeEventAsync,
        CancellationToken ct)
    {
        var courseId = GetOptionalGuid(root, "courseId");
        if (!courseId.HasValue) return JsonSerializer.Serialize(new { error = "courseId is required." }, JsonOptions);
        int? minCount = root.TryGetProperty("minAttendanceCount", out var mcEl) && mcEl.TryGetInt32(out var mc) ? mc : null;
        decimal? minPct = root.TryGetProperty("minAttendancePercent", out var mpEl) && mpEl.TryGetDecimal(out var mp) ? mp : null;
        int? planned = root.TryGetProperty("plannedTotalCount", out var ptEl) && ptEl.TryGetInt32(out var pt) ? pt : null;

        var result = await writeService.CreateActivityAsync(
            principal,
            false,
            courseId.Value,
            GetOptionalString(root, "activityType"),
            GetOptionalString(root, "title"),
            GetOptionalString(root, "defaultLocation"),
            minCount,
            minPct,
            planned,
            ct);
        if (result.IsSuccess && result.Value is not null)
        {
            await writeEventAsync(new { type = "mutation", domain = "university", id = result.Value.Id, title = result.Value.Title ?? result.Value.ActivityType });
        }

        return SerializeResult(result);
    }

    private async Task<string> RunCreateActivityItemAsync(
        ClaimsPrincipal principal,
        Guid userId,
        JsonElement root,
        Guid? lockedCourseId,
        Func<object, Task> writeEventAsync,
        CancellationToken ct)
    {
        var activityId = GetOptionalGuid(root, "activityId");
        if (!activityId.HasValue) return JsonSerializer.Serialize(new { error = "activityId is required." }, JsonOptions);

        if (lockedCourseId.HasValue)
        {
            var belongs = await db.UniversityCourseActivities.AnyAsync(
                a => a.Id == activityId.Value && a.UserId == userId && a.CourseId == lockedCourseId.Value,
                ct);
            if (!belongs)
            {
                return JsonSerializer.Serialize(new { error = "Access denied: activity does not belong to the selected course." }, JsonOptions);
            }
        }

        int? seq = root.TryGetProperty("sequenceNumber", out var seqEl) && seqEl.TryGetInt32(out var sv) ? sv : null;
        var result = await writeService.CreateActivityItemAsync(
            principal,
            false,
            activityId.Value,
            GetOptionalString(root, "title"),
            GetOptionalString(root, "theme"),
            GetOptionalString(root, "notes"),
            seq,
            GetOptionalDateTimeOffset(root, "startAt"),
            GetOptionalDateTimeOffset(root, "endAt"),
            GetOptionalBool(root, "isAllDay", false),
            GetOptionalString(root, "location"),
            ct);
        if (result.IsSuccess && result.Value is not null)
        {
            await writeEventAsync(new { type = "mutation", domain = "university", id = result.Value.Id, title = result.Value.Title });
        }

        return SerializeResult(result);
    }

    private async Task<string> RunGetCourseAsync(ClaimsPrincipal principal, Guid? courseId, CancellationToken ct)
    {
        if (!courseId.HasValue)
        {
            return JsonSerializer.Serialize(new { error = "courseId is required." }, JsonOptions);
        }

        return SerializeResult(await knowledge.GetCourseAsync(principal, false, courseId.Value, ct));
    }

    private async Task<string> RunSearchKnowledgeAsync(
        ClaimsPrincipal principal,
        JsonElement root,
        Guid? lockedCourseId,
        Dictionary<(string SourceType, Guid Id), ChatCitationDto> citations,
        CancellationToken ct)
    {
        var query = GetOptionalString(root, "query") ?? string.Empty;
        var scope = GetOptionalString(root, "scope") ?? "all";
        var courseId = lockedCourseId ?? GetOptionalGuid(root, "courseId");
        var limit = GetOptionalInt(root, "limit", KnowledgeReadService.DefaultSearchLimit);

        var result = await knowledge.SearchKnowledgeAsync(principal, false, query, scope, courseId, limit, ct);
        if (result.IsSuccess && result.Value is not null)
        {
            foreach (var hit in result.Value.Hits)
            {
                citations.TryAdd(
                    (hit.SourceType, hit.Id),
                    new ChatCitationDto(hit.SourceType, hit.Id, hit.Title, hit.LinkLabel, hit.PageLabel));
            }
        }

        return SerializeResult(result);
    }

    private async Task<string> RunReadNoteAsync(
        ClaimsPrincipal principal,
        JsonElement root,
        Guid? lockedCourseId,
        Dictionary<(string SourceType, Guid Id), ChatCitationDto> citations,
        CancellationToken ct)
    {
        var noteId = GetOptionalGuid(root, "noteId");
        if (!noteId.HasValue)
        {
            return JsonSerializer.Serialize(new { error = "noteId is required." }, JsonOptions);
        }

        var result = await knowledge.ReadNoteAsync(principal, false, noteId.Value, ct);
        if (result.IsSuccess && result.Value is not null)
        {
            if (lockedCourseId.HasValue && result.Value.Note.UniversityCourseId != lockedCourseId.Value)
            {
                return JsonSerializer.Serialize(
                    new { error = "Access denied: note does not belong to the selected course." },
                    JsonOptions);
            }

            citations[("note", result.Value.Note.Id)] = new ChatCitationDto(
                "note",
                result.Value.Note.Id,
                result.Value.Note.Title,
                result.Value.Note.LinkLabel,
                null);
        }

        return SerializeResult(result);
    }

    private async Task<string> RunListArtifactsAsync(
        ClaimsPrincipal principal,
        Guid userId,
        JsonElement root,
        Guid? lockedCourseId,
        CancellationToken ct)
    {
        var noteId = GetOptionalGuid(root, "noteId");
        var courseId = GetOptionalGuid(root, "universityCourseId");
        var itemId = GetOptionalGuid(root, "universityActivityItemId");
        var eventId = GetOptionalGuid(root, "calendarEventId");
        var occStart = GetOptionalDateTimeOffset(root, "occurrenceStartAt");

        if (lockedCourseId.HasValue)
        {
            if (eventId.HasValue)
            {
                return JsonSerializer.Serialize(new { error = "Access denied: calendar events are outside this course scope." }, JsonOptions);
            }

            if (courseId.HasValue && courseId.Value != lockedCourseId.Value)
            {
                return JsonSerializer.Serialize(new { error = "Access denied: courseId does not match the selected course." }, JsonOptions);
            }

            if (itemId.HasValue)
            {
                var belongs = await db.UniversityActivityItems.AnyAsync(
                    i => i.Id == itemId.Value && i.UserId == userId && i.CourseId == lockedCourseId.Value,
                    ct);
                if (!belongs)
                {
                    return JsonSerializer.Serialize(new { error = "Access denied: activity item does not belong to the selected course." }, JsonOptions);
                }
            }
            else if (noteId.HasValue)
            {
                var belongs = await db.Notes.AnyAsync(
                    n => n.Id == noteId.Value && n.UserId == userId && n.DeletedAt == null && n.UniversityCourseId == lockedCourseId.Value,
                    ct);
                if (!belongs)
                {
                    return JsonSerializer.Serialize(new { error = "Access denied: note does not belong to the selected course." }, JsonOptions);
                }
            }
            else if (!courseId.HasValue)
            {
                courseId = lockedCourseId.Value;
            }
        }

        return SerializeResult(await knowledge.ListArtifactsAsync(principal, false, noteId, courseId, itemId, eventId, occStart, ct));
    }

    private async Task<string> RunReadArtifactTextAsync(
        ClaimsPrincipal principal,
        Guid userId,
        JsonElement root,
        Guid? lockedCourseId,
        Dictionary<(string SourceType, Guid Id), ChatCitationDto> citations,
        CancellationToken ct)
    {
        var attachmentId = GetOptionalGuid(root, "attachmentId");
        if (!attachmentId.HasValue)
        {
            return JsonSerializer.Serialize(new { error = "attachmentId is required." }, JsonOptions);
        }

        if (lockedCourseId.HasValue)
        {
            var att = await db.Attachments
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == attachmentId.Value && a.UserId == userId && a.DeletedAt == null, ct);
            if (att is null)
            {
                return JsonSerializer.Serialize(new { error = "Artifact not found." }, JsonOptions);
            }

            var inCourse = att.UniversityCourseId == lockedCourseId.Value;
            if (!inCourse && att.NoteId.HasValue)
            {
                inCourse = await db.Notes.AnyAsync(
                    n => n.Id == att.NoteId.Value && n.UserId == userId && n.DeletedAt == null && n.UniversityCourseId == lockedCourseId.Value,
                    ct);
            }

            if (!inCourse)
            {
                return JsonSerializer.Serialize(new { error = "Access denied: artifact does not belong to the selected course." }, JsonOptions);
            }
        }

        var startChunk = GetOptionalInt(root, "startChunk", 0);
        var maxChunks = GetOptionalInt(root, "maxChunks", KnowledgeReadService.DefaultArtifactMaxChunks);
        var result = await knowledge.ReadArtifactTextAsync(principal, false, attachmentId.Value, startChunk, maxChunks, ct);

        if (result.IsSuccess && result.Value is not null)
        {
            var pageLabel = result.Value.Chunks.Count > 0 ? result.Value.Chunks[0].PageLabel : null;
            citations[("artifact", result.Value.AttachmentId)] = new ChatCitationDto(
                "artifact",
                result.Value.AttachmentId,
                result.Value.FileName,
                null,
                pageLabel);
        }

        return SerializeResult(result);
    }

    private async Task<string> BuildFallbackRagContextAsync(
        ClaimsPrincipal principal,
        string userPrompt,
        Guid? courseId,
        Dictionary<(string SourceType, Guid Id), ChatCitationDto> citations,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        if (courseId.HasValue)
        {
            var courseRes = await knowledge.GetCourseAsync(principal, false, courseId.Value, ct);
            if (courseRes.IsSuccess && courseRes.Value is not null)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"Course: {courseRes.Value.Course.Name} ({courseRes.Value.Course.Code})");
            }
        }

        var search = await knowledge.SearchKnowledgeAsync(principal, false, userPrompt, "all", courseId, 6, ct);
        if (search.IsSuccess && search.Value is not null)
        {
            foreach (var hit in search.Value.Hits)
            {
                citations.TryAdd(
                    (hit.SourceType, hit.Id),
                    new ChatCitationDto(hit.SourceType, hit.Id, hit.Title, hit.LinkLabel, hit.PageLabel));
                sb.AppendLine(CultureInfo.InvariantCulture, $"- [{hit.SourceType}] {hit.Title} ({hit.PageLabel ?? hit.LinkLabel ?? "note"}): {hit.Snippet}");
            }
        }

        return sb.Length == 0 ? "(No matching notes or artifacts found.)" : sb.ToString();
    }

    private static string BuildSystemPrompt(Guid? courseId, string? courseName)
    {
        var todayUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (courseId.HasValue)
        {
            return $"""
                You are Atrium's study assistant. Today's UTC date is {todayUtc}.
                You are strictly scoped to the university course "{courseName ?? "Selected Course"}" (courseId: {courseId.Value}).
                You have both read tools (`search_knowledge`, `get_course`, `read_note`, `list_artifacts`, `read_artifact_text`) and write tools (`create_note`, `update_note`, `extract_pdf_images`, `create_activity_item`) scoped to this course.
                Use your read tools to inspect exact course notes and uploaded slides/PDFs before answering or creating study guides, and use your write tools when asked to create/update notes, embed PDF figures, or add lecture items.
                Be concise, cite the note title or slide/page label when referencing material, and say clearly if the course materials do not contain the answer.
                Format chat replies for a narrow sidebar (prefer short bullet lists or compact 2-3 column tables), while using rich Markdown (headings, Obsidian callouts, GFM tables, code blocks, LaTeX, and embedded PDF figures) inside notes created via `create_note`/`update_note`.
                """;
        }

        return $"""
            You are Atrium's personal assistant. Today's UTC date is {todayUtc}.
            You can read AND create/update the user's daily checklist, calendar events, university courses/activities/lectures, Markdown notes, and uploaded study artifacts (including extracting diagrams from PDFs into notes via `extract_pdf_images`).
            Always use your read tools to look up real data before answering, and use your write tools (`create_note`, `update_note`, `extract_pdf_images`, `create_checklist_item`, `toggle_checklist_item`, `create_event`, `create_course`, `create_activity`, `create_activity_item`) whenever the user asks you to create or update items.
            Be concise, helpful, and cite the note title or file/page label when referencing knowledge base items.
            Format chat replies for a narrow sidebar (prefer short bullet lists or compact 2-3 column tables), while using rich Markdown inside notes created via `create_note`/`update_note`.
            """;
    }

    private static JsonArray BuildToolsSchema(bool courseScopedOnly)
    {
        var arr = new JsonArray();

        if (!courseScopedOnly)
        {
            arr.Add(BuildTool("get_day", "Get the user's daily checklist and daily note for a yyyy-MM-dd date (defaults to today).", new JsonObject
            {
                ["date"] = new JsonObject { ["type"] = "string", ["description"] = "Optional date in yyyy-MM-dd format." },
            }));
            arr.Add(BuildTool("list_events", "List expanded calendar events in a date range (defaults to today + 7 days, max 90 days).", new JsonObject
            {
                ["from"] = new JsonObject { ["type"] = "string", ["description"] = "Optional ISO-8601 start timestamp." },
                ["to"] = new JsonObject { ["type"] = "string", ["description"] = "Optional ISO-8601 end timestamp." },
            }));
            arr.Add(BuildTool("list_courses", "List university courses with attendance, note counts, and artifact counts.", new JsonObject
            {
                ["semester"] = new JsonObject { ["type"] = "string", ["description"] = "Optional semester filter." },
                ["status"] = new JsonObject { ["type"] = "string", ["description"] = "Optional status: active, completed, archived." },
            }));
            arr.Add(BuildTool("list_notes_tree", "List folders, general notes, university notes, event notes, and daily notes.", new JsonObject
            {
                ["includeUniversity"] = new JsonObject { ["type"] = "boolean" },
                ["includeEvents"] = new JsonObject { ["type"] = "boolean" },
                ["includeDaily"] = new JsonObject { ["type"] = "boolean" },
            }));
        }

        arr.Add(BuildTool(
            "get_course",
            courseScopedOnly
                ? "Get full details of the selected university course including lectures, schedules, notes, and artifacts."
                : "Get full details of a university course including activities, lectures, notes, and artifacts.",
            courseScopedOnly
                ? new JsonObject()
                : new JsonObject { ["courseId"] = new JsonObject { ["type"] = "string", ["description"] = "Course GUID." } },
            courseScopedOnly ? null : ["courseId"]));

        arr.Add(BuildTool(
            "search_knowledge",
            courseScopedOnly
                ? "Full-text search across notes and extracted artifact text within the selected course."
                : "Full-text search across all notes and extracted artifact text (PDFs, slides, docs).",
            new JsonObject
            {
                ["query"] = new JsonObject { ["type"] = "string", ["description"] = "Search query text." },
                ["scope"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("all", "notes", "artifacts") },
                ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = "Max results (1-25)." },
            },
            ["query"]));

        arr.Add(BuildTool(
            "read_note",
            "Read the full Markdown content and attached artifact metadata of a note by its GUID.",
            new JsonObject
            {
                ["noteId"] = new JsonObject { ["type"] = "string", ["description"] = "Note GUID." },
            },
            ["noteId"]));

        arr.Add(BuildTool(
            "list_artifacts",
            "List uploaded artifacts attached to a course, lecture/activity item, or note.",
            new JsonObject
            {
                ["noteId"] = new JsonObject { ["type"] = "string" },
                ["universityCourseId"] = new JsonObject { ["type"] = "string" },
                ["universityActivityItemId"] = new JsonObject { ["type"] = "string" },
            }));

        arr.Add(BuildTool(
            "read_artifact_text",
            "Read paginated extracted plain text chunks (by page/slide) from an uploaded artifact.",
            new JsonObject
            {
                ["attachmentId"] = new JsonObject { ["type"] = "string", ["description"] = "Artifact attachment GUID." },
                ["startChunk"] = new JsonObject { ["type"] = "integer", ["description"] = "0-based starting chunk index." },
                ["maxChunks"] = new JsonObject { ["type"] = "integer", ["description"] = "Chunks to read (1-20)." },
            },
            ["attachmentId"]));

        arr.Add(BuildTool(
            "create_note",
            "Create a new Markdown note (supports rich formatting: headings, GFM tables, callouts, code blocks, LaTeX math, and embedded images). Can attach to a university lecture/item or course.",
            new JsonObject
            {
                ["title"] = new JsonObject { ["type"] = "string", ["description"] = "Note title." },
                ["content"] = new JsonObject { ["type"] = "string", ["description"] = "Full Markdown content." },
                ["universityCourseId"] = new JsonObject { ["type"] = "string", ["description"] = "Optional university course GUID." },
                ["universityActivityItemId"] = new JsonObject { ["type"] = "string", ["description"] = "Optional university lecture/item GUID." },
                ["folderId"] = new JsonObject { ["type"] = "string", ["description"] = "Optional folder GUID." },
            },
            ["title", "content"]));

        arr.Add(BuildTool(
            "update_note",
            "Update an existing Markdown note's title and/or content (e.g. to embed extracted PDF images or refine a study guide).",
            new JsonObject
            {
                ["noteId"] = new JsonObject { ["type"] = "string", ["description"] = "Note GUID." },
                ["title"] = new JsonObject { ["type"] = "string", ["description"] = "Optional new title." },
                ["content"] = new JsonObject { ["type"] = "string", ["description"] = "Optional new Markdown content." },
            },
            ["noteId"]));

        arr.Add(BuildTool(
            "extract_pdf_images",
            "Extract embedded figures/diagrams from a PDF artifact, save them as inline images on a target note, and return Markdown image embeds ![caption](/api/attachments/id).",
            new JsonObject
            {
                ["attachmentId"] = new JsonObject { ["type"] = "string", ["description"] = "Source PDF artifact GUID." },
                ["noteId"] = new JsonObject { ["type"] = "string", ["description"] = "Target note GUID." },
                ["startPage"] = new JsonObject { ["type"] = "integer" },
                ["endPage"] = new JsonObject { ["type"] = "integer" },
                ["maxImages"] = new JsonObject { ["type"] = "integer", ["description"] = "Max images to extract (1-6)." },
            },
            ["attachmentId", "noteId"]));

        arr.Add(BuildTool(
            "create_activity_item",
            "Create a new lecture, lab, assignment, or exam item under a university course activity.",
            new JsonObject
            {
                ["activityId"] = new JsonObject { ["type"] = "string", ["description"] = "Activity GUID." },
                ["title"] = new JsonObject { ["type"] = "string", ["description"] = "Item title." },
                ["theme"] = new JsonObject { ["type"] = "string" },
                ["notes"] = new JsonObject { ["type"] = "string" },
                ["sequenceNumber"] = new JsonObject { ["type"] = "integer" },
                ["startAt"] = new JsonObject { ["type"] = "string", ["description"] = "Optional ISO-8601 start timestamp." },
                ["endAt"] = new JsonObject { ["type"] = "string", ["description"] = "Optional ISO-8601 end timestamp." },
                ["isAllDay"] = new JsonObject { ["type"] = "boolean" },
                ["location"] = new JsonObject { ["type"] = "string" },
            },
            ["activityId", "title"]));

        if (!courseScopedOnly)
        {
            arr.Add(BuildTool(
                "create_checklist_item",
                "Add a task to the user's daily checklist.",
                new JsonObject
                {
                    ["title"] = new JsonObject { ["type"] = "string" },
                    ["date"] = new JsonObject { ["type"] = "string", ["description"] = "Optional yyyy-MM-dd date." },
                    ["dueTime"] = new JsonObject { ["type"] = "string", ["description"] = "Optional 24h HH:mm time." },
                },
                ["title"]));

            arr.Add(BuildTool(
                "toggle_checklist_item",
                "Mark a daily checklist item completed or incomplete.",
                new JsonObject
                {
                    ["itemId"] = new JsonObject { ["type"] = "string" },
                    ["isCompleted"] = new JsonObject { ["type"] = "boolean" },
                },
                ["itemId", "isCompleted"]));

            arr.Add(BuildTool(
                "create_event",
                "Create a new calendar event.",
                new JsonObject
                {
                    ["title"] = new JsonObject { ["type"] = "string" },
                    ["startAt"] = new JsonObject { ["type"] = "string", ["description"] = "ISO-8601 start timestamp." },
                    ["endAt"] = new JsonObject { ["type"] = "string", ["description"] = "ISO-8601 end timestamp." },
                    ["calendarId"] = new JsonObject { ["type"] = "string" },
                    ["isAllDay"] = new JsonObject { ["type"] = "boolean" },
                    ["location"] = new JsonObject { ["type"] = "string" },
                    ["description"] = new JsonObject { ["type"] = "string" },
                    ["recurrenceRule"] = new JsonObject { ["type"] = "string" },
                },
                ["title", "startAt", "endAt"]));

            arr.Add(BuildTool(
                "create_course",
                "Create a new university course.",
                new JsonObject
                {
                    ["name"] = new JsonObject { ["type"] = "string" },
                    ["code"] = new JsonObject { ["type"] = "string" },
                    ["semester"] = new JsonObject { ["type"] = "string" },
                    ["instructor"] = new JsonObject { ["type"] = "string" },
                    ["credits"] = new JsonObject { ["type"] = "number" },
                    ["color"] = new JsonObject { ["type"] = "string" },
                    ["description"] = new JsonObject { ["type"] = "string" },
                },
                ["name"]));

            arr.Add(BuildTool(
                "create_activity",
                "Create an activity group (lecture, practice, laboratory, homework_assignment, semester_assignment, midterm, exam) inside a university course.",
                new JsonObject
                {
                    ["courseId"] = new JsonObject { ["type"] = "string" },
                    ["activityType"] = new JsonObject { ["type"] = "string" },
                    ["title"] = new JsonObject { ["type"] = "string" },
                    ["defaultLocation"] = new JsonObject { ["type"] = "string" },
                    ["minAttendanceCount"] = new JsonObject { ["type"] = "integer" },
                    ["minAttendancePercent"] = new JsonObject { ["type"] = "number" },
                    ["plannedTotalCount"] = new JsonObject { ["type"] = "integer" },
                },
                ["courseId", "activityType"]));
        }

        return arr;
    }

    private static JsonObject BuildTool(string name, string description, JsonObject properties, string[]? required = null)
    {
        var parameters = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
        };
        if (required is { Length: > 0 })
        {
            var reqArr = new JsonArray();
            foreach (var r in required) reqArr.Add(r);
            parameters["required"] = reqArr;
        }

        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["parameters"] = parameters,
            },
        };
    }

    private static string ResolveCompletionsUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return trimmed + "/chat/completions";
    }

    private static string DescribeToolStatus(string toolName, string? courseName) => toolName switch
    {
        "get_day" => "Checking today's checklist & note…",
        "list_events" => "Checking calendar events…",
        "list_courses" => "Checking university courses…",
        "get_course" => courseName is not null ? $"Reading {courseName} details…" : "Reading course details…",
        "list_notes_tree" => "Browsing notes…",
        "read_note" => "Reading note…",
        "list_artifacts" => "Listing attached files…",
        "read_artifact_text" => "Reading file contents…",
        "search_knowledge" => courseName is not null ? $"Searching {courseName}…" : "Searching knowledge base…",
        "create_note" => "Creating Markdown note…",
        "update_note" => "Updating Markdown note…",
        "extract_pdf_images" => "Extracting diagrams from PDF…",
        "create_checklist_item" => "Adding checklist task…",
        "toggle_checklist_item" => "Updating checklist task…",
        "create_event" => "Creating calendar event…",
        "create_course" => "Creating university course…",
        "create_activity" => "Adding course activity…",
        "create_activity_item" => "Adding lecture/item…",
        _ => "Looking up information…",
    };

    private static string SerializeResult<T>(KnowledgeResult<T> result) =>
        result.IsSuccess
            ? JsonSerializer.Serialize(result.Value, JsonOptions)
            : JsonSerializer.Serialize(new { error = result.Error ?? "Request failed." }, JsonOptions);

    private static string? GetOptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static Guid? GetOptionalGuid(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String && Guid.TryParse(el.GetString(), out var g)
            ? g
            : null;

    private static int GetOptionalInt(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out var el) && el.TryGetInt32(out var v) ? v : fallback;

    private static bool GetOptionalBool(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out var el) && (el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False)
            ? el.GetBoolean()
            : fallback;

    private static DateTimeOffset? GetOptionalDateTimeOffset(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el)
        && el.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(
            el.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var dt)
            ? dt
            : null;

    private static string? ExtractReasoningString(JsonElement obj)
    {
        if (obj.TryGetProperty("reasoning_content", out var rcEl) && rcEl.ValueKind == JsonValueKind.String)
        {
            return rcEl.GetString();
        }

        if (obj.TryGetProperty("reasoning", out var rEl) && rEl.ValueKind == JsonValueKind.String)
        {
            return rEl.GetString();
        }

        return null;
    }

    private sealed class ThinkTagSplitter
    {
        private const string OpenTag = "<think>";
        private const string CloseTag = "</think>";
        private readonly StringBuilder pending = new();
        private bool inThink;

        public List<(bool IsReasoning, string Text)> Feed(string chunk)
        {
            pending.Append(chunk);
            return Drain(final: false);
        }

        public List<(bool IsReasoning, string Text)> Flush() => Drain(final: true);

        private List<(bool IsReasoning, string Text)> Drain(bool final)
        {
            var output = new List<(bool, string)>();
            while (pending.Length > 0)
            {
                var buf = pending.ToString();
                var targetTag = inThink ? CloseTag : OpenTag;
                var idx = buf.IndexOf(targetTag, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    if (idx > 0)
                    {
                        output.Add((inThink, buf[..idx]));
                    }

                    pending.Remove(0, idx + targetTag.Length);
                    inThink = !inThink;
                    continue;
                }

                if (final)
                {
                    output.Add((inThink, buf));
                    pending.Clear();
                    break;
                }

                // Only hold back a potential partial tag if '<' appears near the end of the buffer
                var lastLt = buf.LastIndexOf('<');
                if (lastLt >= 0 && buf.Length - lastLt < targetTag.Length)
                {
                    if (lastLt > 0)
                    {
                        output.Add((inThink, buf[..lastLt]));
                        pending.Remove(0, lastLt);
                    }
                }
                else
                {
                    output.Add((inThink, buf));
                    pending.Clear();
                }

                break;
            }

            return output;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "OpenAI-compatible inference HTTP request failed.")]
    private static partial void LogInferenceRequestFailed(ILogger logger, Exception exception);
}
