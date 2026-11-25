using Atrium.Api.Data;
using Atrium.Api.Health;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Atrium.Api.Tests;

public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    public const string TestJwtSigningKey = "atrium-unit-test-signing-key-with-at-least-32-bytes-0123456789";

    private readonly SqliteConnection _sqliteConnection;

    public TestAppFactory()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Production");
        builder.UseSetting(
            "ConnectionStrings:DefaultConnection",
            "Host=127.0.0.1;Port=5433;Database=atrium_test;Username=atrium;Password=test");
        builder.UseSetting("Atrium:Database:AutoMigrate", "false");
        builder.UseSetting("Atrium:Auth:JwtSigningKey", TestJwtSigningKey);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_sqliteConnection));

            services.RemoveAll<IDatabaseHealthCheck>();
            services.AddScoped<IDatabaseHealthCheck, AlwaysHealthyDatabaseCheck>();

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _sqliteConnection.Dispose();
        }
    }

    private sealed class AlwaysHealthyDatabaseCheck : IDatabaseHealthCheck
    {
        public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
