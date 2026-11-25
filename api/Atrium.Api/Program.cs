using Atrium.Api.Data;
using Atrium.Api.Health;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Missing required database connection string 'ConnectionStrings:DefaultConnection'.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IDatabaseHealthCheck, EfDatabaseHealthCheck>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

app.MapGet("/api/health", async (IDatabaseHealthCheck dbHealth, ILogger<Program> logger, CancellationToken ct) =>
{
    var timestamp = DateTimeOffset.UtcNow;
    bool canConnect;
    try
    {
        canConnect = await dbHealth.CanConnectAsync(ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
        logger.LogWarning(ex, "Database health check failed.");
        canConnect = false;
    }

    if (!canConnect)
    {
        return Results.Json(
            new { status = "degraded", database = "unreachable", timestamp },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new { status = "ok", database = "connected", timestamp });
});

var configuredDistPath = builder.Configuration["Atrium:WebDistPath"];
var resolvedDistPath = !string.IsNullOrWhiteSpace(configuredDistPath)
    ? Path.GetFullPath(configuredDistPath, app.Environment.ContentRootPath)
    : (!app.Environment.IsDevelopment()
        ? Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "..", "web", "dist"))
        : null);

if (!string.IsNullOrWhiteSpace(resolvedDistPath) && Directory.Exists(resolvedDistPath))
{
    var fileProvider = new PhysicalFileProvider(resolvedDistPath);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });

    var indexHtmlPath = Path.Combine(resolvedDistPath, "index.html");
    if (File.Exists(indexHtmlPath))
    {
        app.MapFallback(async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(indexHtmlPath, context.RequestAborted);
        });
    }
}

app.Run();

public partial class Program;
