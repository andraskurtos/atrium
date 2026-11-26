using System.Net;
using System.Net.Http.Json;
using Atrium.Api.Health;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Atrium.Api.Tests;

public class HealthEndpointTests
{
    private sealed class FakeDatabaseHealthCheck(Func<CancellationToken, Task<bool>> probe) : IDatabaseHealthCheck
    {
        public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default) => probe(cancellationToken);
    }

    private sealed record HealthResponse(string Status, string Database, DateTimeOffset Timestamp);

    private static WebApplicationFactory<Program> CreateFactory(Func<CancellationToken, Task<bool>> probe)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Atrium:Database:AutoMigrate", "false");
            builder.UseSetting("Atrium:Auth:JwtSigningKey", TestAppFactory.TestJwtSigningKey);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=5432;Database=atrium_test;Username=atrium;Password=test"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDatabaseHealthCheck>();
                services.AddScoped<IDatabaseHealthCheck>(_ => new FakeDatabaseHealthCheck(probe));
            });
        });
    }

    [Fact]
    public async Task HealthEndpoint_Returns200_WhenDatabaseIsReachable()
    {
        using var factory = CreateFactory(_ => Task.FromResult(true));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(payload);
        Assert.Equal("ok", payload.Status);
        Assert.Equal("connected", payload.Database);
        Assert.True(payload.Timestamp > DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task HealthEndpoint_Returns503_WhenDatabaseIsUnreachable()
    {
        using var factory = CreateFactory(_ => Task.FromResult(false));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(payload);
        Assert.Equal("degraded", payload.Status);
        Assert.Equal("unreachable", payload.Database);
    }

    [Fact]
    public async Task HealthEndpoint_Returns503_WhenDatabaseCheckThrows()
    {
        using var factory = CreateFactory(_ => Task.FromException<bool>(new InvalidOperationException("Connection refused")));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(payload);
        Assert.Equal("degraded", payload.Status);
        Assert.Equal("unreachable", payload.Database);
    }

    [Fact]
    public void Startup_ThrowsClearError_WhenConnectionStringIsMissing()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = ""
                });
            });
        });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("DefaultConnection", ex.Message);
    }
}
