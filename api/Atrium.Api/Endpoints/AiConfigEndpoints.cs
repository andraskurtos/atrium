using System.Security.Claims;
using Atrium.Api.Auth;
using Atrium.Api.Chat;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atrium.Api.Endpoints;

public static class AiConfigEndpoints
{
    public static void MapAiConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai")
            .RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapGet("/config", async (
            ClaimsPrincipal principal,
            AiConfigService service,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();
            var config = await service.GetConfigAsync(userId.Value, ct);
            return Results.Ok(config);
        });

        group.MapPost("/providers", async (
            CreateAiProviderRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            SecretProtector secretProtector,
            AiConfigService service,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            await service.EnsureLegacyBackfillAsync(userId.Value, ct);

            var name = request.Name?.Trim() ?? string.Empty;
            if (name.Length is 0 or > 80)
            {
                return Results.BadRequest(new { error = "Provider name must be 1–80 characters." });
            }

            if (!AiConfigService.IsValidHttpUrl(request.BaseUrl, out var normalizedUrl))
            {
                return Results.BadRequest(new { error = "Base URL must be a valid http(s) URL (max 500 characters)." });
            }

            var now = DateTimeOffset.UtcNow;
            var provider = new AiProvider
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                Name = name,
                BaseUrl = normalizedUrl,
                ApiKeyEncrypted = !string.IsNullOrWhiteSpace(request.ApiKey)
                    ? secretProtector.Protect(request.ApiKey.Trim())
                    : null,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.AiProviders.Add(provider);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/ai/providers/{provider.Id}", AiConfigService.ToProviderDto(provider));
        });

        group.MapPut("/providers/{id:guid}", async (
            Guid id,
            UpdateAiProviderRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            SecretProtector secretProtector,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var provider = await db.AiProviders
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId.Value, ct);
            if (provider is null) return Results.NotFound();

            var name = request.Name?.Trim() ?? string.Empty;
            if (name.Length is 0 or > 80)
            {
                return Results.BadRequest(new { error = "Provider name must be 1–80 characters." });
            }

            if (!AiConfigService.IsValidHttpUrl(request.BaseUrl, out var normalizedUrl))
            {
                return Results.BadRequest(new { error = "Base URL must be a valid http(s) URL (max 500 characters)." });
            }

            provider.Name = name;
            provider.BaseUrl = normalizedUrl;
            if (request.ClearApiKey)
            {
                provider.ApiKeyEncrypted = null;
            }
            else if (!string.IsNullOrWhiteSpace(request.ApiKey))
            {
                provider.ApiKeyEncrypted = secretProtector.Protect(request.ApiKey.Trim());
            }

            provider.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(AiConfigService.ToProviderDto(provider));
        });

        group.MapDelete("/providers/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var provider = await db.AiProviders
                .Include(p => p.Models)
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId.Value, ct);
            if (provider is null) return Results.NotFound();

            var removedModelIds = provider.Models.Select(m => m.Id).ToHashSet();
            if (removedModelIds.Count > 0)
            {
                var prefs = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId.Value, ct);
                if (prefs is not null)
                {
                    if (prefs.DefaultAiModelId.HasValue && removedModelIds.Contains(prefs.DefaultAiModelId.Value))
                    {
                        prefs.DefaultAiModelId = null;
                    }

                    if (prefs.DefaultVisionModelId.HasValue && removedModelIds.Contains(prefs.DefaultVisionModelId.Value))
                    {
                        prefs.DefaultVisionModelId = null;
                    }
                }
            }

            db.AiProviders.Remove(provider);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/providers/{id:guid}/discover-models", async (
            Guid id,
            ClaimsPrincipal principal,
            AiConfigService service,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var result = await service.DiscoverModelsAsync(userId.Value, id, ct);
            return Results.Ok(result);
        });

        group.MapPost("/models", async (
            CreateAiModelRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var provider = await db.AiProviders
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == request.ProviderId && p.UserId == userId.Value, ct);
            if (provider is null) return Results.NotFound(new { error = "Provider not found." });

            var modelId = request.ModelId?.Trim() ?? string.Empty;
            if (modelId.Length is 0 or > 160)
            {
                return Results.BadRequest(new { error = "Model ID must be 1–160 characters." });
            }

            var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? modelId : request.DisplayName.Trim();
            if (displayName.Length > 120)
            {
                displayName = displayName[..120];
            }

            var now = DateTimeOffset.UtcNow;
            var model = new AiModel
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                ProviderId = provider.Id,
                ModelId = modelId,
                DisplayName = displayName,
                SupportsVision = request.SupportsVision,
                SortOrder = request.SortOrder,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.AiModels.Add(model);

            var prefs = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId.Value, ct);
            if (prefs is not null)
            {
                prefs.DefaultAiModelId ??= model.Id;
                if (model.SupportsVision && prefs.DefaultVisionModelId is null)
                {
                    prefs.DefaultVisionModelId = model.Id;
                }
            }

            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/ai/models/{model.Id}", AiConfigService.ToModelDto(model, provider.Name));
        });

        group.MapPut("/models/{id:guid}", async (
            Guid id,
            UpdateAiModelRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var model = await db.AiModels
                .Include(m => m.Provider)
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId.Value, ct);
            if (model is null) return Results.NotFound();

            var modelId = request.ModelId?.Trim() ?? string.Empty;
            if (modelId.Length is 0 or > 160)
            {
                return Results.BadRequest(new { error = "Model ID must be 1–160 characters." });
            }

            var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? modelId : request.DisplayName.Trim();
            if (displayName.Length > 120)
            {
                displayName = displayName[..120];
            }

            model.ModelId = modelId;
            model.DisplayName = displayName;
            model.SupportsVision = request.SupportsVision;
            model.SortOrder = request.SortOrder;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            if (!model.SupportsVision)
            {
                var prefs = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId.Value, ct);
                if (prefs is not null && prefs.DefaultVisionModelId == model.Id)
                {
                    prefs.DefaultVisionModelId = null;
                }
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(AiConfigService.ToModelDto(model, model.Provider.Name));
        });

        group.MapDelete("/models/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var model = await db.AiModels
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId.Value, ct);
            if (model is null) return Results.NotFound();

            var prefs = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId.Value, ct);
            if (prefs is not null)
            {
                if (prefs.DefaultAiModelId == model.Id) prefs.DefaultAiModelId = null;
                if (prefs.DefaultVisionModelId == model.Id) prefs.DefaultVisionModelId = null;
            }

            db.AiModels.Remove(model);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapPut("/defaults", async (
            UpdateAiDefaultsRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            SecretProtector secretProtector,
            AiConfigService service,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var prefs = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId.Value, ct);
            if (prefs is null) return Results.NotFound();

            if (request.DefaultModelId.HasValue)
            {
                var exists = await db.AiModels.AnyAsync(
                    m => m.Id == request.DefaultModelId.Value && m.UserId == userId.Value,
                    ct);
                if (!exists)
                {
                    return Results.BadRequest(new { error = "Default model not found." });
                }
            }

            if (request.DefaultVisionModelId.HasValue)
            {
                var visionModel = await db.AiModels
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        m => m.Id == request.DefaultVisionModelId.Value && m.UserId == userId.Value,
                        ct);
                if (visionModel is null)
                {
                    return Results.BadRequest(new { error = "Default vision model not found." });
                }

                if (!visionModel.SupportsVision)
                {
                    return Results.BadRequest(new { error = "Default vision model must have vision enabled." });
                }
            }

            var webProvider = string.IsNullOrWhiteSpace(request.WebSearchProvider)
                ? WebSearchProviders.DuckDuckGo
                : request.WebSearchProvider.Trim().ToLowerInvariant();
            if (!WebSearchProviders.Valid.Contains(webProvider))
            {
                return Results.BadRequest(new { error = "Unsupported web search provider." });
            }

            string? normalizedWebUrl = null;
            if (!string.IsNullOrWhiteSpace(request.WebSearchUrl))
            {
                if (!AiConfigService.IsValidHttpUrl(request.WebSearchUrl, out var validUrl))
                {
                    return Results.BadRequest(new { error = "Web search URL must be a valid http(s) URL." });
                }

                normalizedWebUrl = validUrl;
            }
            else if (webProvider == WebSearchProviders.SearXng)
            {
                return Results.BadRequest(new { error = "SearXNG requires a base URL." });
            }

            prefs.DefaultAiModelId = request.DefaultModelId;
            prefs.DefaultVisionModelId = request.DefaultVisionModelId;
            prefs.WebSearchProvider = webProvider;
            prefs.WebSearchUrl = normalizedWebUrl;

            if (request.ClearWebSearchApiKey)
            {
                prefs.WebSearchApiKeyEncrypted = null;
            }
            else if (!string.IsNullOrWhiteSpace(request.WebSearchApiKey))
            {
                prefs.WebSearchApiKeyEncrypted = secretProtector.Protect(request.WebSearchApiKey.Trim());
            }

            await db.SaveChangesAsync(ct);
            var updated = await service.GetConfigAsync(userId.Value, ct);
            return Results.Ok(updated);
        });
    }
}

