using System.Text;
using System.Threading.RateLimiting;
using Atrium.Api.Auth;
using Atrium.Api.Chat;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Atrium.Api.Endpoints;
using Atrium.Api.Health;
using Atrium.Api.Knowledge;
using Atrium.Api.Mcp;
using Atrium.Api.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IDatabaseHealthCheck, EfDatabaseHealthCheck>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<SecretProtector>();
builder.Services.AddSingleton<IAttachmentStorage, LocalDiskAttachmentStorage>();
builder.Services.AddSingleton<IArtifactTextExtractor, DefaultArtifactTextExtractor>();
builder.Services.AddScoped<ArtifactTextService>();
builder.Services.AddScoped<KnowledgeReadService>();
builder.Services.AddScoped<KnowledgeWriteService>();
builder.Services.AddScoped<OpenAiChatService>();
builder.Services.AddHttpClient(OpenAiChatService.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddHostedService<ArtifactTextBackfillService>();
builder.Services.AddHttpContextAccessor();
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<AtriumMcpTools>();

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
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                var userId = principal?.GetUserId();
                var sessionId = principal?.GetSessionId();
                if (userId is null || sessionId is null)
                {
                    context.Fail("Missing required session claims.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var now = DateTimeOffset.UtcNow;
                var session = await db.RefreshSessions
                    .AsNoTracking()
                    .Include(s => s.User)
                    .FirstOrDefaultAsync(
                        s => s.Id == sessionId.Value && s.UserId == userId.Value,
                        context.HttpContext.RequestAborted);

                if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now || !session.User.IsActive)
                {
                    context.Fail("Session has been revoked or account is inactive.");
                }
            },
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
    })
    .AddPolicy(AuthConstants.AgentPatOnlyPolicy, policy =>
    {
        policy.AddAuthenticationSchemes(AuthConstants.PatScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(AuthConstants.AuthTypeClaim, AuthConstants.PatAuthType);
    });

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthConstants.AuthRateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: TokenCrypto.GetClientIp(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    if (builder.Configuration.GetValue("Atrium:TrustForwardedHeaders", false))
    {
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }
});

var app = builder.Build();

if (args.Contains("--migration-script", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var migrator = db.Database.GetInfrastructure().GetRequiredService<IMigrator>();
    var sql = migrator.GenerateScript(
        fromMigration: null,
        toMigration: null,
        options: MigrationsSqlGenerationOptions.Idempotent);
    Console.WriteLine(sql);
    return;
}

if (args.Contains("--migrate", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    Console.WriteLine("Atrium database migrations applied successfully.");
    return;
}

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
app.MapCalendarEndpoints();
app.MapUniversityEndpoints();
app.MapDayEndpoints();
app.MapNoteEndpoints();
app.MapAttachmentEndpoints();
app.MapKnowledgeEndpoints();
app.MapChatEndpoints();
app.MapMcp("/mcp").RequireAuthorization(AuthConstants.AgentPatOnlyPolicy);

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
            if (context.Request.Path.StartsWithSegments("/api")
                || context.Request.Path.StartsWithSegments("/mcp"))
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
