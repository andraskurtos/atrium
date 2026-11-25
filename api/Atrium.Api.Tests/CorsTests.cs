using System.Net;
using Atrium.Api.Health;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Atrium.Api.Tests;

public class CorsTests
{
    private sealed class HealthyDbCheck : IDatabaseHealthCheck
    {
        public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private static WebApplicationFactory<Program> CreateFactory(string? corsOrigins = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Host=127.0.0.1;Port=5432;Database=atrium_test;Username=atrium;Password=test");
            builder.UseSetting("Atrium:CorsOrigins", corsOrigins ?? string.Empty);

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDatabaseHealthCheck>();
                services.AddScoped<IDatabaseHealthCheck, HealthyDbCheck>();
            });
        });
    }

    [Fact]
    public async Task HealthEndpoint_AllowsConfiguredOrigin_AndNormalizesTrailingSlash()
    {
        using var factory = CreateFactory("https://andraskurtos.github.io/, https://atrium.example.com");
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("Origin", "https://andraskurtos.github.io");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Contains("https://andraskurtos.github.io", origins);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Credentials", out var credentials));
        Assert.Contains("true", credentials);
    }

    [Fact]
    public async Task HealthEndpoint_DoesNotReturnCorsHeader_ForUnlistedOrigin()
    {
        using var factory = CreateFactory("https://andraskurtos.github.io");
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("Origin", "https://evil.example.com");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task PreflightOptions_Succeeds_ForConfiguredOrigin()
    {
        using var factory = CreateFactory("https://andraskurtos.github.io");
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/health");
        request.Headers.Add("Origin", "https://andraskurtos.github.io");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Contains("https://andraskurtos.github.io", origins);
    }

    [Fact]
    public async Task Cors_IsDisabledByDefault_WhenUnconfigured()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("Origin", "https://andraskurtos.github.io");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
