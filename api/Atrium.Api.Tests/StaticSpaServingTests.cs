using System.Net;
using Atrium.Api.Health;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Atrium.Api.Tests;

public class StaticSpaServingTests : IDisposable
{
    private readonly string _tempDistDir;

    public StaticSpaServingTests()
    {
        _tempDistDir = Path.Combine(Path.GetTempPath(), $"atrium-dist-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDistDir);
        File.WriteAllText(
            Path.Combine(_tempDistDir, "index.html"),
            "<!doctype html><html><body><h1>Atrium SPA</h1></body></html>");
        File.WriteAllText(
            Path.Combine(_tempDistDir, "manifest.webmanifest"),
            """{"name":"Atrium"}""");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDistDir))
        {
            Directory.Delete(_tempDistDir, recursive: true);
        }
    }

    private sealed class HealthyDbCheck : IDatabaseHealthCheck
    {
        public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private WebApplicationFactory<Program> CreateProductionFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Host=127.0.0.1;Port=5432;Database=atrium_test;Username=atrium;Password=test");
            builder.UseSetting("Atrium:WebDistPath", _tempDistDir);

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDatabaseHealthCheck>();
                services.AddScoped<IDatabaseHealthCheck, HealthyDbCheck>();
            });
        });
    }

    [Fact]
    public async Task ProductionRoot_ServesBuiltIndexHtml()
    {
        using var factory = CreateProductionFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Atrium SPA", html);
    }

    [Fact]
    public async Task ProductionStaticAsset_ServesManifest()
    {
        using var factory = CreateProductionFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/manifest.webmanifest");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"Atrium\"", body);
    }

    [Fact]
    public async Task ProductionClientRoute_FallsBackToIndexHtml()
    {
        using var factory = CreateProductionFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/calendar/2026");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Atrium SPA", html);
    }

    [Fact]
    public async Task ProductionUnknownApiRoute_Returns404NotIndexHtml()
    {
        using var factory = CreateProductionFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
