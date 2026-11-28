using System.Security.Claims;
using Atrium.Api.Auth;
using Atrium.Api.Knowledge;

namespace Atrium.Api.Endpoints;

public static class KnowledgeEndpoints
{
    public static void MapKnowledgeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/attachments/{id:guid}/text", async (
            Guid id,
            int? startChunk,
            int? maxChunks,
            ClaimsPrincipal principal,
            KnowledgeReadService knowledge,
            CancellationToken ct) =>
        {
            var result = await knowledge.ReadArtifactTextAsync(
                principal,
                enforceAgentPrivacy: false,
                id,
                startChunk ?? 0,
                maxChunks ?? KnowledgeReadService.DefaultArtifactMaxChunks,
                ct);

            return ToHttpResult(result);
        }).RequireAuthorization(AuthConstants.HumanOrAgentPolicy);

        app.MapGet("/api/knowledge/search", async (
            string? q,
            string? scope,
            Guid? courseId,
            int? limit,
            ClaimsPrincipal principal,
            KnowledgeReadService knowledge,
            CancellationToken ct) =>
        {
            var result = await knowledge.SearchKnowledgeAsync(
                principal,
                enforceAgentPrivacy: false,
                q,
                scope,
                courseId,
                limit ?? KnowledgeReadService.DefaultSearchLimit,
                ct);

            return ToHttpResult(result);
        }).RequireAuthorization(AuthConstants.HumanOrAgentPolicy);
    }

    private static IResult ToHttpResult<T>(KnowledgeResult<T> result) => result.StatusCode switch
    {
        StatusCodes.Status200OK => Results.Ok(result.Value),
        StatusCodes.Status400BadRequest => Results.BadRequest(new { error = result.Error }),
        StatusCodes.Status401Unauthorized => Results.Unauthorized(),
        StatusCodes.Status403Forbidden => Results.Forbid(),
        StatusCodes.Status404NotFound => Results.NotFound(new { error = result.Error }),
        _ => Results.Json(new { error = result.Error ?? "Unexpected error." }, statusCode: result.StatusCode),
    };
}

