using System.Text;
using System.Threading.RateLimiting;
using Atrium.Api.Auth;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Atrium.Api.Endpoints;
using Atrium.Api.Health;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Missing required database connection string 'ConnectionStrings:DefaultConnection'.");
}

var authOptions = new AuthOptions();
builder.Configuration.GetSection(AuthOptions.SectionName).Bind(authOptions);
authOptions.ValidateForEnvironment(builder.Environment.IsDevelopment());
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IDatabaseHealthCheck, EfDatabaseHealthCheck>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<JwtTokenService>();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = AuthConstants.CompositeScheme;
        options.DefaultChallengeScheme = AuthConstants.CompositeScheme;
    })
    .AddPolicyScheme(AuthConstants.CompositeScheme, "JWT or Atrium PAT", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var authHeader = context.Request.Headers.Authorization.ToString();
            if (authHeader.StartsWith("Bearer " + AuthConstants.PatPrefixTag, StringComparison.OrdinalIgnoreCase))
            {
                return AuthConstants.PatScheme;
            }

            return AuthConstants.JwtScheme;
        };
    })
    .AddJwtBearer(AuthConstants.JwtScheme, options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = authOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.JwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    })
    .AddScheme<AuthenticationSchemeOptions, PatAuthenticationHandler>(AuthConstants.PatScheme, null);

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthConstants.HumanJwtOnlyPolicy, policy =>
    {
        policy.AddAuthenticationSchemes(AuthConstants.JwtScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(AuthConstants.AuthTypeClaim, AuthConstants.JwtAuthType);
    })
    .AddPolicy(AuthConstants.OwnerOnlyPolicy, policy =>
    {
        policy.AddAuthenticationSchemes(AuthConstants.JwtScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(AuthConstants.AuthTypeClaim, AuthConstants.JwtAuthType);
        policy.RequireRole(AuthConstants.OwnerRole);
    })
    .AddPolicy(AuthConstants.HumanOrAgentPolicy, policy =>
    {
        policy.AddAuthenticationSchemes(AuthConstants.CompositeScheme);
        policy.RequireAuthenticatedUser();
    });

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter(AuthConstants.AuthRateLimitPolicy, limiter =>
    {
        limiter.PermitLimit = 60;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

if (builder.Configuration.GetValue("Atrium:Database:AutoMigrate", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

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
        LogDatabaseHealthCheckFailed(logger, ex);
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

app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapTokenEndpoints();
app.MapHouseholdEndpoints();

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

public partial class Program
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Database health check failed.")]
    private static partial void LogDatabaseHealthCheckFailed(ILogger logger, Exception exception);
}
