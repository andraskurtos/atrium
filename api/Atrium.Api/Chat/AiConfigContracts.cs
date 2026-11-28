namespace Atrium.Api.Chat;

public static class WebSearchProviders
{
    public const string DuckDuckGo = "duckduckgo";
    public const string Tavily = "tavily";
    public const string Brave = "brave";
    public const string SearXng = "searxng";

    public static readonly IReadOnlySet<string> Valid = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        DuckDuckGo,
        Tavily,
        Brave,
        SearXng,
    };
}

public sealed record AiProviderDto(
    Guid Id,
    string Name,
    string BaseUrl,
    bool HasApiKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AiModelDto(
    Guid Id,
    Guid ProviderId,
    string ProviderName,
    string ModelId,
    string DisplayName,
    bool SupportsVision,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record WebSearchConfigDto(
    string Provider,
    string? Url,
    bool HasApiKey);

public sealed record AiConfigResponse(
    IReadOnlyList<AiProviderDto> Providers,
    IReadOnlyList<AiModelDto> Models,
    Guid? DefaultModelId,
    Guid? DefaultVisionModelId,
    WebSearchConfigDto WebSearch);

public sealed record CreateAiProviderRequest(
    string Name,
    string BaseUrl,
    string? ApiKey = null);

public sealed record UpdateAiProviderRequest(
    string Name,
    string BaseUrl,
    string? ApiKey = null,
    bool ClearApiKey = false);

public sealed record CreateAiModelRequest(
    Guid ProviderId,
    string ModelId,
    string? DisplayName = null,
    bool SupportsVision = false,
    int SortOrder = 0);

public sealed record UpdateAiModelRequest(
    string ModelId,
    string? DisplayName = null,
    bool SupportsVision = false,
    int SortOrder = 0);

public sealed record UpdateAiDefaultsRequest(
    Guid? DefaultModelId,
    Guid? DefaultVisionModelId,
    string? WebSearchProvider,
    string? WebSearchUrl,
    string? WebSearchApiKey = null,
    bool ClearWebSearchApiKey = false);

public sealed record DiscoverProviderModelsResponse(
    IReadOnlyList<string> Models,
    string? Error = null);

public sealed record ResolvedChatModel(
    Guid? ModelEntityId,
    string BaseUrl,
    string ModelId,
    string DisplayName,
    string? ApiKey,
    bool SupportsVision);

public sealed record ResolvedChatModelResult(
    bool Success,
    string? ErrorMessage,
    ResolvedChatModel? Model,
    bool SwitchedToVision,
    Guid? UserSelectedModelId);

public sealed record ResolvedWebSearchSettings(
    string Provider,
    string? Url,
    string? ApiKey);

