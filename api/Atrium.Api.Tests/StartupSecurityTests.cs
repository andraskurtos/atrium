using Atrium.Api.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class StartupSecurityTests
{
    [Theory]
    [InlineData("")]
    [InlineData("too-short-key")]
    [InlineData(AuthOptions.DevJwtSigningKeySentinel)]
    public void ProductionStartup_ThrowsClearError_WhenJwtSigningKeyIsInvalid(string invalidKey)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Host=127.0.0.1;Port=5433;Database=atrium_test;Username=atrium;Password=test");
            builder.UseSetting("Atrium:Database:AutoMigrate", "false");
            builder.UseSetting("Atrium:Auth:JwtSigningKey", invalidKey);
        });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("JwtSigningKey", ex.Message, StringComparison.Ordinal);
    }
}
