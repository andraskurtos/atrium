using System.Net.Http.Headers;
using System.Text.Json;
using Atrium.Api.Auth;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atrium.Api.Chat;

public sealed class AiConfigService(
    AppDbContext db,
    SecretProtector secretProtector,
    IHttpClientFactory httpClientFactory)
{
    public async Task<AiConfigResponse> GetConfigAsync(Guid userId, CancellationToken ct)
    {
        await EnsureLegacyBackfillAsync(userId, ct);

        var prefs = await db.UserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        var providers = (await db.AiProviders
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync(ct))
            .OrderBy(p => p.CreatedAt)
            .ToList();

        var providerNames = providers.ToDictionary(p => p.Id, p => p.Name);

        var models = (await db.AiModels
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .ToListAsync(ct))
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.CreatedAt)
            .ToList();

        var providerDtos = providers.Select(ToProviderDto).ToList();
        var modelDtos = models
            .Select(m => ToModelDto(m, providerNames.GetValueOrDefault(m.ProviderId, string.Empty)))
            .ToList();

        var webProvider = string.IsNullOrWhiteSpace(prefs?.WebSearchProvider)
            ? WebSearchProviders.DuckDuckGo
            : prefs.WebSearchProvider;

        var webSearch = new WebSearchConfigDto(
            webProvider,
            prefs?.WebSearchUrl,
            !string.IsNullOrWhiteSpace(prefs?.WebSearchApiKeyEncrypted));

        return new AiConfigResponse(
            providerDtos,
            modelDtos,
            prefs?.DefaultAiModelId,
            prefs?.DefaultVisionModelId,
            webSearch);
    }

    public async Task<ResolvedWebSearchSettings> GetWebSearchSettingsAsync(Guid userId, CancellationToken ct)
    {
        var prefs = await db.UserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        var provider = string.IsNullOrWhiteSpace(prefs?.WebSearchProvider)
            ? WebSearchProviders.DuckDuckGo
            : prefs.WebSearchProvider.Trim().ToLowerInvariant();

        string? apiKey = null;
        if (!string.IsNullOrWhiteSpace(prefs?.WebSearchApiKeyEncrypted))
        {
            apiKey = secretProtector.Unprotect(prefs.WebSearchApiKeyEncrypted);
        }

        return new ResolvedWebSearchSettings(provider, prefs?.WebSearchUrl, apiKey);
    }

    public async Task<ResolvedChatModelResult> ResolveModelForChatAsync(
        Guid userId,
        Guid? requestedModelId,
        bool turnHasImages,
        CancellationToken ct)
    {
        await EnsureLegacyBackfillAsync(userId, ct);

        var prefs = await db.UserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        var models = (await db.AiModels
            .AsNoTracking()
            .Include(m => m.Provider)
            .Where(m => m.UserId == userId)
            .ToListAsync(ct))
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.CreatedAt)
            .ToList();

        AiModel? selected = null;
        if (requestedModelId.HasValue)
        {
            selected = models.FirstOrDefault(m => m.Id == requestedModelId.Value);
            if (selected is null)
            {
                return new ResolvedChatModelResult(
                    false,
                    "Selected AI model was not found.",
                    null,
                    false,
                    null);
            }
        }
        else if (prefs?.DefaultAiModelId is Guid defaultId)
        {
            selected = models.FirstOrDefault(m => m.Id == defaultId);
        }

        selected ??= models.FirstOrDefault();

        if (selected is null)
        {
            return new ResolvedChatModelResult(
                false,
                "AI Assistant is not configured yet. Add an AI provider and model in Account → AI Assistant.",
                null,
                false,
                null);
        }

        var userSelectedModelId = selected.Id;
        var active = selected;
        var switchedToVision = false;

        if (turnHasImages && !selected.SupportsVision)
        {
            var visionModel = prefs?.DefaultVisionModelId is Guid visionId
                ? models.FirstOrDefault(m => m.Id == visionId && m.SupportsVision)
                : null;

            if (visionModel is null)
            {
                return new ResolvedChatModelResult(
                    false,
                    "This message includes images, but neither the selected model nor a Default Vision Model supports vision. Configure a vision-capable model in Account → AI Assistant.",
                    null,
                    false,
                    userSelectedModelId);
            }

            active = visionModel;
            switchedToVision = true;
        }

        string? apiKey = null;
        if (!string.IsNullOrWhiteSpace(active.Provider.ApiKeyEncrypted))
        {
            apiKey = secretProtector.Unprotect(active.Provider.ApiKeyEncrypted);
            if (apiKey is null)
            {
                return new ResolvedChatModelResult(
                    false,
                    $"Stored API key for provider '{active.Provider.Name}' could not be decrypted. Please re-enter it in Account → AI Assistant.",
                    null,
                    false,
                    userSelectedModelId);
            }
        }

        var resolved = new ResolvedChatModel(
            active.Id,
            active.Provider.BaseUrl,
            active.ModelId,
            active.DisplayName,
            apiKey,
            active.SupportsVision);

        return new ResolvedChatModelResult(true, null, resolved, switchedToVision, userSelectedModelId);
    }

    public async Task<DiscoverProviderModelsResponse> DiscoverModelsAsync(
        Guid userId,
        Guid providerId,
        CancellationToken ct)
    {
        var provider = await db.AiProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == providerId && p.UserId == userId, ct);
        if (provider is null)
        {
            return new DiscoverProviderModelsResponse([], "Provider not found.");
        }

        var modelsUrl = ResolveModelsUrl(provider.BaseUrl);
        string? apiKey = null;
        if (!string.IsNullOrWhiteSpace(provider.ApiKeyEncrypted))
        {
            apiKey = secretProtector.Unprotect(provider.ApiKeyEncrypted);
        }

        try
        {
            var client = httpClientFactory.CreateClient(OpenAiChatService.HttpClientName);
            using var req = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return new DiscoverProviderModelsResponse(
                    [],
                    $"Provider returned HTTP {(int)resp.StatusCode} from {modelsUrl}. You can still add model IDs manually.");
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var ids = new List<string>();
            if (doc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataEl.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    {
                        var id = idEl.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            ids.Add(id);
                        }
                    }
                }
            }

            var distinct = ids.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(200).ToList();
            return new DiscoverProviderModelsResponse(distinct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DiscoverProviderModelsResponse(
                [],
                $"Could not fetch models from {modelsUrl}: {ex.Message}");
        }
    }

    public async Task EnsureLegacyBackfillAsync(Guid userId, CancellationToken ct)
    {
        var hasProviders = await db.AiProviders.AnyAsync(p => p.UserId == userId, ct);
        if (hasProviders) return;

        var prefs = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (prefs is null
            || string.IsNullOrWhiteSpace(prefs.AiInferenceUrl)
            || string.IsNullOrWhiteSpace(prefs.AiModel))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var provider = new AiProvider
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Default",
            BaseUrl = prefs.AiInferenceUrl.Trim(),
            ApiKeyEncrypted = prefs.AiApiKeyEncrypted,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var model = new AiModel
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProviderId = provider.Id,
            ModelId = prefs.AiModel.Trim(),
            DisplayName = prefs.AiModel.Trim(),
            SupportsVision = false,
            SortOrder = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.AiProviders.Add(provider);
        db.AiModels.Add(model);
        prefs.DefaultAiModelId = model.Id;
        prefs.AiInferenceUrl = null;
        prefs.AiModel = null;
        prefs.AiApiKeyEncrypted = null;
        await db.SaveChangesAsync(ct);
    }

    public static AiProviderDto ToProviderDto(AiProvider p) =>
        new(p.Id, p.Name, p.BaseUrl, !string.IsNullOrWhiteSpace(p.ApiKeyEncrypted), p.CreatedAt, p.UpdatedAt);

    public static AiModelDto ToModelDto(AiModel m, string providerName) =>
        new(m.Id, m.ProviderId, providerName, m.ModelId, m.DisplayName, m.SupportsVision, m.SortOrder, m.CreatedAt, m.UpdatedAt);

    public static bool IsValidHttpUrl(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var trimmed = raw.Trim();
        if (trimmed.Length > 500) return false;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        normalized = trimmed;
        return true;
    }

    private static string ResolveModelsUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^"/chat/completions".Length];
        }

        return trimmed.EndsWith("/models", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}/models";
    }
}
