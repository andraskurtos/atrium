using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Atrium.Api.Auth;
using Atrium.Api.Data;
using Atrium.Api.Data.Entities;
using Atrium.Api.University;
using Microsoft.EntityFrameworkCore;

namespace Atrium.Api.Endpoints;

public static class QuizEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void MapQuizEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/university")
            .RequireAuthorization(AuthConstants.HumanJwtOnlyPolicy);

        group.MapGet("/courses/{courseId:guid}/quizzes", async (
            Guid courseId,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var courseExists = await db.UniversityCourses
                .AsNoTracking()
                .AnyAsync(c => c.Id == courseId && c.UserId == userId.Value, ct);
            if (!courseExists) return Results.NotFound();

            var quizzes = (await db.UniversityQuizzes
                .AsNoTracking()
                .Where(q => q.UserId == userId.Value && q.CourseId == courseId)
                .ToListAsync(ct))
                .OrderByDescending(q => q.CreatedAt)
                .Select(ToSummaryDto)
                .ToList();

            return Results.Ok(quizzes);
        });

        group.MapGet("/quizzes/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var quiz = await db.UniversityQuizzes
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == id && q.UserId == userId.Value, ct);
            if (quiz is null) return Results.NotFound();

            return Results.Ok(ToDetailDto(quiz));
        });

        group.MapPost("/quizzes/{id:guid}/attempt", async (
            Guid id,
            RecordQuizAttemptRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var quiz = await db.UniversityQuizzes
                .FirstOrDefaultAsync(q => q.Id == id && q.UserId == userId.Value, ct);
            if (quiz is null) return Results.NotFound();

            if (request.Score < 0 || request.Score > quiz.QuestionCount)
            {
                return Results.BadRequest(new { error = $"Score must be between 0 and {quiz.QuestionCount}." });
            }

            var now = DateTimeOffset.UtcNow;
            quiz.AttemptCount += 1;
            quiz.LastScore = request.Score;
            quiz.BestScore = quiz.BestScore.HasValue
                ? Math.Max(quiz.BestScore.Value, request.Score)
                : request.Score;
            quiz.LastPlayedAt = now;
            quiz.UpdatedAt = now;
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToSummaryDto(quiz));
        });

        group.MapDelete("/quizzes/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var quiz = await db.UniversityQuizzes
                .FirstOrDefaultAsync(q => q.Id == id && q.UserId == userId.Value, ct);
            if (quiz is null) return Results.NotFound();

            db.UniversityQuizzes.Remove(quiz);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    public static UniversityQuizSummaryDto ToSummaryDto(UniversityQuiz q) => new(
        q.Id,
        q.CourseId,
        q.UniversityActivityItemId,
        q.Title,
        q.Description,
        q.QuestionCount,
        q.AttemptCount,
        q.LastScore,
        q.BestScore,
        q.LastPlayedAt,
        q.CreatedAt,
        q.UpdatedAt);

    public static UniversityQuizDetailDto ToDetailDto(UniversityQuiz q)
    {
        List<QuizQuestionDto> questions;
        try
        {
            questions = JsonSerializer.Deserialize<List<QuizQuestionDto>>(q.QuestionsJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            questions = [];
        }

        return new UniversityQuizDetailDto(
            q.Id,
            q.CourseId,
            q.UniversityActivityItemId,
            q.Title,
            q.Description,
            q.QuestionCount,
            q.AttemptCount,
            q.LastScore,
            q.BestScore,
            q.LastPlayedAt,
            q.CreatedAt,
            q.UpdatedAt,
            questions);
    }
}

