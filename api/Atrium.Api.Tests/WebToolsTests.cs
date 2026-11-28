using System.Net;
using System.Text;
using Atrium.Api.Chat;
using Atrium.Api.Knowledge;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class WebToolsTests
{
    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<Uri?> RequestedUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri);
            return Task.FromResult(responder(request));
        }
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    [Fact]
    public async Task SafeHttpFetcher_BlocksLoopbackPrivateLinkLocalAndRedirectToPrivateIp()
    {
        var handler = new StubHttpHandler(req =>
        {
            if (req.RequestUri?.Host == "public.example.test")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                redirect.Headers.Location = new Uri("http://127.0.0.1/admin");
                return redirect;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("SECRET"),
            };
        });

        using var httpClient = new HttpClient(handler);
        var fetcher = new SafeHttpFetcher(
            new StubHttpClientFactory(httpClient),
            (host, _) => Task.FromResult<IPAddress[]>(host switch
            {
                "public.example.test" => [IPAddress.Parse("93.184.216.34")],
                "127.0.0.1" => [IPAddress.Loopback],
                "10.0.0.1" => [IPAddress.Parse("10.0.0.1")],
                "169.254.169.254" => [IPAddress.Parse("169.254.169.254")],
                _ => [IPAddress.Loopback],
            }));

        var r1 = await fetcher.FetchAsync("http://127.0.0.1/test", 100_000, allowPrivateNetworks: false, ct: CancellationToken.None);
        Assert.False(r1.Success);
        Assert.Empty(handler.RequestedUris);

        var r2 = await fetcher.FetchAsync("http://10.0.0.1/internal", 100_000, allowPrivateNetworks: false, ct: CancellationToken.None);
        Assert.False(r2.Success);
        Assert.Empty(handler.RequestedUris);

        var r3 = await fetcher.FetchAsync("http://169.254.169.254/latest", 100_000, allowPrivateNetworks: false, ct: CancellationToken.None);
        Assert.False(r3.Success);
        Assert.Empty(handler.RequestedUris);

        var r4 = await fetcher.FetchAsync("file:///etc/passwd", 100_000, allowPrivateNetworks: false, ct: CancellationToken.None);
        Assert.False(r4.Success);

        var rRedirect = await fetcher.FetchAsync("https://public.example.test/start", 100_000, allowPrivateNetworks: false, ct: CancellationToken.None);
        Assert.False(rRedirect.Success);
        Assert.Single(handler.RequestedUris);
        Assert.Equal("public.example.test", handler.RequestedUris[0]?.Host);
    }

    [Fact]
    public void HtmlMarkdownConverter_PreservesSyllabusHeadingsAndTables_StripsScriptsAndNav()
    {
        const string html = """
            <!doctype html>
            <html>
              <head>
                <title>BMEVITMMSMA001 - Artificial General Intelligence</title>
                <script>var secret = "do-not-show";</script>
                <style>.hidden { display: none; }</style>
              </head>
              <body>
                <nav><a href="/home">Home</a> | <a href="/login">Login</a></nav>
                <main>
                  <h1>Artificial General Intelligence</h1>
                  <p>Credits: <strong>5</strong></p>
                  <h2>Weekly Schedule</h2>
                  <table>
                    <thead><tr><th>Week</th><th>Topic</th><th>Assessment</th></tr></thead>
                    <tbody>
                      <tr><td>1</td><td>Introduction &amp; Agents</td><td>None</td></tr>
                      <tr><td>2</td><td>Search &amp; Planning</td><td>HW1</td></tr>
                    </tbody>
                  </table>
                </main>
                <footer>Copyright BME</footer>
              </body>
            </html>
            """;

        var (title, md) = HtmlMarkdownConverter.Convert(html);
        Assert.Equal("BMEVITMMSMA001 - Artificial General Intelligence", title);
        Assert.DoesNotContain("do-not-show", md, StringComparison.Ordinal);
        Assert.DoesNotContain("Copyright BME", md, StringComparison.Ordinal);
        Assert.Contains("# Artificial General Intelligence", md, StringComparison.Ordinal);
        Assert.Contains("## Weekly Schedule", md, StringComparison.Ordinal);
        Assert.Contains("| Week | Topic | Assessment |", md, StringComparison.Ordinal);
        Assert.Contains("| 1 | Introduction & Agents | None |", md, StringComparison.Ordinal);
        Assert.Contains("| 2 | Search & Planning | HW1 |", md, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WebToolsService_SearchProviders_ParseDuckDuckGoTavilyBraveAndSearxng()
    {
        var handler = new StubHttpHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? string.Empty;
            if (url.Contains("duckduckgo.com", StringComparison.OrdinalIgnoreCase))
            {
                const string ddgHtml = """
                    <html><body>
                      <div class="result">
                        <a class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Feduinfo.bme.hu%2Fsubjects%2FBME1">BME Syllabus</a>
                        <a class="result__snippet">Course overview and weekly lecture topics.</a>
                      </div>
                    </body></html>
                    """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ddgHtml, Encoding.UTF8, "text/html"),
                };
            }

            if (url.Contains("tavily.com", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"results":[{"title":"Tavily Hit","url":"https://tavily.test/1","content":"Tavily snippet"}]}""", Encoding.UTF8, "application/json"),
                };
            }

            if (url.Contains("search.brave.com", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"web":{"results":[{"title":"Brave Hit","url":"https://brave.test/1","description":"Brave snippet"}]}}""", Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"results":[{"title":"SearX Hit","url":"https://searx.test/1","content":"SearX snippet"}]}""", Encoding.UTF8, "application/json"),
            };
        });

        using var httpClient = new HttpClient(handler);
        var fetcher = new SafeHttpFetcher(
            new StubHttpClientFactory(httpClient),
            (_, _) => Task.FromResult<IPAddress[]>([IPAddress.Parse("93.184.216.34")]));
        var extractor = new DefaultArtifactTextExtractor();

        var ddgJson = await WebToolsService.ExecuteWebSearchAsync(
            fetcher,
            new ResolvedWebSearchSettings("duckduckgo", null, null),
            "BME syllabus",
            5,
            CancellationToken.None);
        Assert.Contains("https://eduinfo.bme.hu/subjects/BME1", ddgJson, StringComparison.Ordinal);
        Assert.Contains("BME Syllabus", ddgJson, StringComparison.Ordinal);

        var tavilyJson = await WebToolsService.ExecuteWebSearchAsync(
            fetcher,
            new ResolvedWebSearchSettings("tavily", null, "tvly-key"),
            "BME",
            5,
            CancellationToken.None);
        Assert.Contains("Tavily Hit", tavilyJson, StringComparison.Ordinal);

        var braveJson = await WebToolsService.ExecuteWebSearchAsync(
            fetcher,
            new ResolvedWebSearchSettings("brave", null, "brave-key"),
            "BME",
            5,
            CancellationToken.None);
        Assert.Contains("Brave Hit", braveJson, StringComparison.Ordinal);

        var searxJson = await WebToolsService.ExecuteWebSearchAsync(
            fetcher,
            new ResolvedWebSearchSettings("searxng", "http://127.0.0.1:8888", null),
            "BME",
            5,
            CancellationToken.None);
        Assert.Contains("SearX Hit", searxJson, StringComparison.Ordinal);
        Assert.NotNull(extractor);
    }

    [Fact]
    public async Task WebToolsService_MalformedJsonOrIcs_ReturnsErrorJsonInsteadOfThrowing()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not json</html>", Encoding.UTF8, "application/json"),
        });

        using var httpClient = new HttpClient(handler);
        var fetcher = new SafeHttpFetcher(
            new StubHttpClientFactory(httpClient),
            (_, _) => Task.FromResult<IPAddress[]>([IPAddress.Parse("93.184.216.34")]));

        var badJsonResult = await WebToolsService.ExecuteWebSearchAsync(
            fetcher,
            new ResolvedWebSearchSettings("searxng", "http://127.0.0.1:8888", null),
            "test",
            5,
            CancellationToken.None);
        Assert.Contains("\"error\"", badJsonResult, StringComparison.Ordinal);
    }
}
