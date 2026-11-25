using Atrium.Api.Data;
using Atrium.Api.Health;
using Microsoft.EntityFrameworkCore;

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

var app = builder.Build();

app.MapGet("/api/health", async (IDatabaseHealthCheck dbHealth, CancellationToken ct) =>
{
    var timestamp = DateTimeOffset.UtcNow;
    bool canConnect;
    try
    {
        canConnect = await dbHealth.CanConnectAsync(ct);
    }
    catch
    {
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

app.Run();

public partial class Program;
