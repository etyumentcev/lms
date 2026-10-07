using System.Data;
using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

// Unmapped SQL projection: no queue persistence or mutable progress tables.
public sealed class QueueRow
{
    public Guid ProblemWorkId { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid StudentId { get; set; }
    public string CourseId { get; set; } = null!;
    public string? CourseTitle { get; set; }
    public string LessonId { get; set; } = null!;
    public string? LessonTitle { get; set; }
    public string? GroupId { get; set; }
    public string ProblemId { get; set; } = null!;
    public string? ProblemTitle { get; set; }
    public int AttemptNumber { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public long Version { get; set; }
}

public sealed partial class PostgresWorkspaceQueries
{
    public async Task<ReviewQueueResponseDto> ReviewQueue(Actor actor, ReviewQueueQuery query, CancellationToken ct)
    {
        if (actor.Role != AuthorRole.Teacher) throw new UseCaseException("Forbidden");
        if (query.Page < 1 || query.PageSize is < 1 or > 200 || (long)(query.Page - 1) * query.PageSize > int.MaxValue ||
            query.AttemptKind is not ("all" or "first" or "revision") || query.Search?.Length > 200)
            throw new UseCaseException("ValidationFailed");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        // Exact version join only. LEFT JOIN keeps submitted work visible even if provisioning is incomplete.
        var rows = db.Database.SqlQuery<QueueRow>($"""
            SELECT w."Id" AS "ProblemWorkId", s."Id" AS "SubmissionId", a."StudentId", a."CourseId",
                v."ContentJson"->>'title' AS "CourseTitle", a."LessonId", lesson.data->>'title' AS "LessonTitle",
                e."GroupId", a."ProblemId", problem.data->>'title' AS "ProblemTitle",
                s."AttemptNumber", s."SubmittedAt", w."Version"
            FROM "ProblemWorks" w
            JOIN "ProblemAssignments" a ON a."Id" = w."ProblemAssignmentId"
            JOIN "Submissions" s ON s."ProblemWorkId" = w."Id"
                AND s."AttemptNumber" = (SELECT MAX(s2."AttemptNumber") FROM "Submissions" s2 WHERE s2."ProblemWorkId" = w."Id")
            LEFT JOIN "AccessEnrollments" e ON e."StudentId" = a."StudentId" AND e."CourseId" = a."CourseId"
            LEFT JOIN "PublishedContentVersions" v ON v."Id" = a."CourseVersionId" AND v."CourseId" = a."CourseId"
            LEFT JOIN LATERAL jsonb_array_elements(v."ContentJson"->'lessons') lesson(data) ON lesson.data->>'id' = a."LessonId"
            LEFT JOIN LATERAL jsonb_array_elements(lesson.data->'problems') problem(data) ON problem.data->>'id' = a."ProblemId"
            WHERE w."Status" = 'Submitted' AND EXISTS (
                SELECT 1 FROM "AccessTeachers" g WHERE g."TeacherId" = {actor.Id} AND g."CourseId" = a."CourseId"
                AND (g."GroupId" IS NULL OR (e."IsActive" AND e."GroupId" = g."GroupId")))
            """);
        if (query.CourseId is not null) rows = rows.Where(r => r.CourseId == query.CourseId);
        if (query.GroupId is not null) rows = rows.Where(r => r.GroupId == query.GroupId);
        if (query.StudentId is not null) rows = rows.Where(r => r.StudentId == query.StudentId);
        if (query.AttemptKind == "first") rows = rows.Where(r => r.AttemptNumber == 1);
        if (query.AttemptKind == "revision") rows = rows.Where(r => r.AttemptNumber > 1);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Literal substring search; escape wildcard syntax. Profile names are unavailable in C2.
            var search = "%" + query.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            rows = rows.Where(r => EF.Functions.ILike(r.CourseId, search, "\\") ||
                EF.Functions.ILike(r.CourseTitle!, search, "\\") || EF.Functions.ILike(r.LessonId, search, "\\") ||
                EF.Functions.ILike(r.LessonTitle!, search, "\\") || EF.Functions.ILike(r.ProblemId, search, "\\") ||
                EF.Functions.ILike(r.ProblemTitle!, search, "\\") || EF.Functions.ILike(r.StudentId.ToString(), search, "\\"));
        }
        var now = time.GetUtcNow();
        var total = await rows.CountAsync(ct);
        var first = await rows.CountAsync(r => r.AttemptNumber == 1, ct);
        var average = await rows.Select(r => (double?)(now - r.SubmittedAt).TotalSeconds).AverageAsync(ct);
        var byCourse = await rows.GroupBy(r => r.CourseId).OrderBy(g => g.Key)
            .Select(g => new CourseQueueCountDto(g.Key, g.Min(r => r.CourseTitle), g.Count())).ToArrayAsync(ct);
        var page = await rows.OrderBy(r => r.SubmittedAt).ThenBy(r => r.ProblemWorkId)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(ct);
        return new(page.Select(r => new ReviewQueueItemDto(r.ProblemWorkId, r.SubmissionId, r.StudentId, null,
                r.CourseId, r.CourseTitle, r.LessonId, r.LessonTitle, r.GroupId, null, r.ProblemId, r.ProblemTitle,
                r.AttemptNumber, r.SubmittedAt, Math.Max(0, (now - r.SubmittedAt).TotalSeconds), r.Version)).ToArray(),
            query.Page, query.PageSize, total, new(total, first, total - first, average, byCourse));
    }
}
