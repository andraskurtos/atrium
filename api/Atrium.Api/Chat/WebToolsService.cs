using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Atrium.Api.Calendar;
using Atrium.Api.Knowledge;

namespace Atrium.Api.Chat;

public sealed record WebSearchResultItem(
    string Title,
    string Url,
    string Snippet);

public sealed partial class WebToolsService(
    SafeHttpFetcher fetcher,
    AiConfigService aiConfigService,
    IArtifactTextExtractor textExtractor)
{
    private const int MaxHtmlBytes = 2 * 1024 * 1024;
    private const int MaxBinaryBytes = 10 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [GeneratedRegex(@"<a\b[^>]*class\s*=\s*[""'][^""']*result__a[^""']*[""'][^>]*href\s*=\s*[""'](?<href>[^""']+)[""'][^>]*>(?<title>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex DdgLinkRegex();

    [GeneratedRegex(@"<(a|div)\b[^>]*class\s*=\s*[""'][^""']*result__snippet[^""']*[""'][^>]*>(?<snip>.*?)</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex DdgSnippetRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex StripTagRegex();

    public async Task<string> FetchUrlAsync(string url, int maxChars, CancellationToken ct)
    {
        var clampedMax = Math.Clamp(maxChars <= 0 ? 16_000 : maxChars, 1_000, 24_000);
        var isLikelyBinary = url.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".ics", StringComparison.OrdinalIgnoreCase);

        var fetched = await fetcher.FetchAsync(
            url,
            isLikelyBinary ? MaxBinaryBytes : MaxHtmlBytes,
            allowPrivateNetworks: false,
            ct: ct);

        if (!fetched.Success)
        {
            return JsonSerializer.Serialize(new { error = fetched.Error ?? "Failed to fetch URL.", url }, JsonOptions);
        }

        var contentType = (fetched.ContentType ?? string.Empty).ToLowerInvariant();
        var finalUrl = fetched.FinalUrl ?? url;
        string? title = null;
        string content;

        if (contentType.Contains("calendar", StringComparison.Ordinal)
            || finalUrl.EndsWith(".ics", StringComparison.OrdinalIgnoreCase))
        {
            var rawIcs = Encoding.UTF8.GetString(fetched.BodyBytes);
            try
            {
                var parsed = IcsParser.Parse(rawIcs, "UTC");
                title = parsed.CalendarName ?? "ICS Calendar";
                var summaryObj = new
                {
                    totalEvents = parsed.Events.Count,
                    timezone = parsed.Timezone,
                    events = parsed.Events.OrderBy(e => e.StartAt).Take(50).Select(e => new
                    {
                        summary = e.Title,
                        startAt = e.StartAt.ToString("O", CultureInfo.InvariantCulture),
                        endAt = e.EndAt.ToString("O", CultureInfo.InvariantCulture),
                        location = e.Location,
                    }),
                };
                content = JsonSerializer.Serialize(summaryObj, JsonOptions);
            }
            catch (FormatException ex)
            {
                return JsonSerializer.Serialize(new { error = $"Failed to parse remote ICS calendar: {ex.Message}", url }, JsonOptions);
            }
        }
        else if (contentType.Contains("pdf", StringComparison.Ordinal)
            || finalUrl.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            await using var ms = new MemoryStream(fetched.BodyBytes);
            var extracted = textExtractor.Extract(ms, "remote.pdf", "application/pdf", ct);
            content = string.Join("\n\n", extracted.Chunks.Select(c => $"[{c.PageLabel}]\n{c.Content}"));
        }
        else if (contentType.Contains("html", StringComparison.Ordinal)
            || contentType.Contains("xhtml", StringComparison.Ordinal))
        {
            var html = Encoding.UTF8.GetString(fetched.BodyBytes);
            (title, content) = HtmlMarkdownConverter.Convert(html);
        }
        else
        {
            content = Encoding.UTF8.GetString(fetched.BodyBytes).Trim();
        }

        var truncated = content.Length > clampedMax;
        if (truncated)
        {
            content = content[..clampedMax] + "\n\n[Truncated]";
        }

        return JsonSerializer.Serialize(
            new
            {
                url,
                finalUrl,
                title,
                contentType = fetched.ContentType,
                truncated,
                content,
            },
            JsonOptions);
    }

    public async Task<string> WebSearchAsync(Guid userId, string query, int maxResults, CancellationToken ct)
    {
        var settings = await aiConfigService.GetWebSearchSettingsAsync(userId, ct);
        return await ExecuteWebSearchAsync(fetcher, settings, query, maxResults, ct);
    }

    public static async Task<string> ExecuteWebSearchAsync(
        SafeHttpFetcher fetcher,
        ResolvedWebSearchSettings settings,
        string query,
        int maxResults,
        CancellationToken ct)
    {
        var q = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(q))
        {
            return JsonSerializer.Serialize(new { error = "Search query must not be empty." }, JsonOptions);
        }

        var limit = Math.Clamp(maxResults <= 0 ? 5 : maxResults, 1, 10);
        var provider = string.IsNullOrWhiteSpace(settings.Provider)
            ? WebSearchProviders.DuckDuckGo
            : settings.Provider.Trim().ToLowerInvariant();

        return provider switch
        {
            WebSearchProviders.Tavily => await SearchTavilyAsync(fetcher, settings.ApiKey, q, limit, ct),
            WebSearchProviders.Brave => await SearchBraveAsync(fetcher, settings.ApiKey, q, limit, ct),
            WebSearchProviders.SearXng => await SearchSearXngAsync(fetcher, settings.Url, settings.ApiKey, q, limit, ct),
            _ => await SearchDuckDuckGoAsync(fetcher, q, limit, ct),
        };
    }

    private static async Task<string> SearchTavilyAsync(
        SafeHttpFetcher fetcher,
        string? apiKey,
        string query,
        int limit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return JsonSerializer.Serialize(new { error = "Tavily web search is selected, but no Tavily API key is configured in Account → AI Assistant." }, JsonOptions);
        }

        var payload = JsonSerializer.Serialize(new
        {
            api_key = apiKey,
            query,
            max_results = limit,
            search_depth = "basic",
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var res = await fetcher.FetchAsync(
            "https://api.tavily.com/search",
            MaxHtmlBytes,
            allowPrivateNetworks: false,
            method: HttpMethod.Post,
            body: content,
            ct: ct);

        if (!res.Success)
        {
            return JsonSerializer.Serialize(new { error = res.Error ?? "Tavily search failed." }, JsonOptions);
        }

        var items = new List<WebSearchResultItem>();
        try
        {
            using var doc = JsonDocument.Parse(res.BodyBytes);
            if (doc.RootElement.TryGetProperty("results", out var resultsEl) && resultsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in resultsEl.EnumerateArray().Take(limit))
                {
                    var title = r.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                    var url = r.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                    var snippet = r.TryGetProperty("content", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        items.Add(new WebSearchResultItem(title, url, snippet));
                    }
                }
            }
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { error = "Tavily returned an invalid JSON response." }, JsonOptions);
        }

        return JsonSerializer.Serialize(new { provider = "tavily", query, results = items }, JsonOptions);
    }

    private static async Task<string> SearchBraveAsync(
        SafeHttpFetcher fetcher,
        string? apiKey,
        string query,
        int limit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return JsonSerializer.Serialize(new { error = "Brave Search is selected, but no Brave API key is configured in Account → AI Assistant." }, JsonOptions);
        }

        var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={limit}";
        var headers = new Dictionary<string, string>
        {
            ["X-Subscription-Token"] = apiKey,
            ["Accept"] = "application/json",
        };
        var res = await fetcher.FetchAsync(url, MaxHtmlBytes, allowPrivateNetworks: false, headers: headers, ct: ct);
        if (!res.Success)
        {
            return JsonSerializer.Serialize(new { error = res.Error ?? "Brave Search failed." }, JsonOptions);
        }

        var items = new List<WebSearchResultItem>();
        try
        {
            using var doc = JsonDocument.Parse(res.BodyBytes);
            if (doc.RootElement.TryGetProperty("web", out var webEl)
                && webEl.TryGetProperty("results", out var resultsEl)
                && resultsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in resultsEl.EnumerateArray().Take(limit))
                {
                    var title = r.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                    var hitUrl = r.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                    var snippet = r.TryGetProperty("description", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                    if (!string.IsNullOrWhiteSpace(hitUrl))
                    {
                        items.Add(new WebSearchResultItem(title, hitUrl, snippet));
                    }
                }
            }
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { error = "Brave Search returned an invalid JSON response." }, JsonOptions);
        }

        return JsonSerializer.Serialize(new { provider = "brave", query, results = items }, JsonOptions);
    }

    private static async Task<string> SearchSearXngAsync(
        SafeHttpFetcher fetcher,
        string? baseUrl,
        string? apiKey,
        string query,
        int limit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return JsonSerializer.Serialize(new { error = "SearXNG is selected, but no SearXNG URL is configured in Account → AI Assistant." }, JsonOptions);
        }

        var trimmed = baseUrl.Trim().TrimEnd('/');
        var searchUrl = $"{trimmed}/search?q={Uri.EscapeDataString(query)}&format=json";
        Dictionary<string, string>? headers = null;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {apiKey}" };
        }

        var res = await fetcher.FetchAsync(searchUrl, MaxHtmlBytes, allowPrivateNetworks: true, headers: headers, ct: ct);
        if (!res.Success)
        {
            return JsonSerializer.Serialize(new { error = res.Error ?? "SearXNG search failed." }, JsonOptions);
        }

        var items = new List<WebSearchResultItem>();
        try
        {
            using var doc = JsonDocument.Parse(res.BodyBytes);
            if (doc.RootElement.TryGetProperty("results", out var resultsEl) && resultsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in resultsEl.EnumerateArray().Take(limit))
                {
                    var title = r.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                    var hitUrl = r.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                    var snippet = r.TryGetProperty("content", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                    if (!string.IsNullOrWhiteSpace(hitUrl))
                    {
                        items.Add(new WebSearchResultItem(title, hitUrl, snippet));
                    }
                }
            }
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { error = "SearXNG returned an invalid JSON response (ensure JSON format is enabled on your instance)." }, JsonOptions);
        }

        return JsonSerializer.Serialize(new { provider = "searxng", query, results = items }, JsonOptions);
    }

    private static async Task<string> SearchDuckDuckGoAsync(
        SafeHttpFetcher fetcher,
        string query,
        int limit,
        CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent([new KeyValuePair<string, string>("q", query)]);
        var res = await fetcher.FetchAsync(
            "https://html.duckduckgo.com/html/",
            MaxHtmlBytes,
            allowPrivateNetworks: false,
            method: HttpMethod.Post,
            body: form,
            ct: ct);

        if (!res.Success)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = $"DuckDuckGo search failed ({res.Error}). You can configure Tavily, Brave Search, or SearXNG in Account → AI Assistant for higher reliability.",
                },
                JsonOptions);
        }

        var html = Encoding.UTF8.GetString(res.BodyBytes);
        var linkMatches = DdgLinkRegex().Matches(html);
        var snippetMatches = DdgSnippetRegex().Matches(html);

        var items = new List<WebSearchResultItem>();
        for (var i = 0; i < linkMatches.Count && items.Count < limit; i++)
        {
            var rawHref = WebUtility.HtmlDecode(linkMatches[i].Groups["href"].Value).Trim();
            var resolvedUrl = UnwrapDuckDuckGoUrl(rawHref);
            var title = CleanText(linkMatches[i].Groups["title"].Value);
            var snippet = i < snippetMatches.Count ? CleanText(snippetMatches[i].Groups["snip"].Value) : string.Empty;
            if (!string.IsNullOrWhiteSpace(resolvedUrl))
            {
                items.Add(new WebSearchResultItem(title, resolvedUrl, snippet));
            }
        }

        return JsonSerializer.Serialize(new { provider = "duckduckgo", query, results = items }, JsonOptions);
    }

    private static string UnwrapDuckDuckGoUrl(string rawHref)
    {
        var normalized = rawHref.StartsWith("//", StringComparison.Ordinal) ? "https:" + rawHref : rawHref;
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            && uri.Host.Contains("duckduckgo.com", StringComparison.OrdinalIgnoreCase))
        {
            var query = uri.Query.TrimStart('?');
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq > 0 && pair[..eq].Equals("uddg", StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(pair[(eq + 1)..]);
                }
            }
        }

        return normalized;
    }

    private static string CleanText(string html) =>
        WebUtility.HtmlDecode(StripTagRegex().Replace(html, " ")).Trim();
}

