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
    public string? StudentDisplayName { get; set; }
    public string CourseId { get; set; } = null!;
    public string? CourseTitle { get; set; }
    public string LessonId { get; set; } = null!;
    public string? LessonTitle { get; set; }
    public string? GroupId { get; set; }
    public string ProblemId { get; set; } = null!;
    public string? ProblemTitle { get; set; }
    public int AttemptNumber { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public int AttachmentCount { get; set; }
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
            SELECT w."Id" AS "ProblemWorkId", s."Id" AS "SubmissionId", a."StudentId",
                (SELECT profile."DisplayName"
                 FROM "AssignmentScopes" profile_scope
                 JOIN "OrganizationParticipantProfiles" profile
                   ON profile."OrganizationId"=profile_scope."OrganizationId" AND profile."ActorId"=a."StudentId"
                 WHERE profile_scope."AssignmentId"=a."Id" LIMIT 1) AS "StudentDisplayName",
                a."CourseId",
                v."ContentJson"->>'title' AS "CourseTitle", a."LessonId", lesson.data->>'title' AS "LessonTitle",
                e."GroupId", a."ProblemId", problem.data->>'title' AS "ProblemTitle",
                s."AttemptNumber", s."SubmittedAt", cardinality(s."AttachmentIds") AS "AttachmentCount", w."Version"
            FROM "ProblemWorks" w
            JOIN "ProblemAssignments" a ON a."Id" = w."ProblemAssignmentId"
            JOIN "Submissions" s ON s."ProblemWorkId" = w."Id"
                AND s."AttemptNumber" = (SELECT MAX(s2."AttemptNumber") FROM "Submissions" s2 WHERE s2."ProblemWorkId" = w."Id")
            LEFT JOIN "AccessEnrollments" e ON e."StudentId" = a."StudentId" AND e."CourseId" = a."CourseId"
            LEFT JOIN "PublishedContentVersions" v ON v."Id" = a."CourseVersionId" AND v."CourseId" = a."CourseId"
            LEFT JOIN LATERAL jsonb_array_elements(v."ContentJson"->'lessons') lesson(data) ON lesson.data->>'id' = a."LessonId"
            LEFT JOIN LATERAL jsonb_array_elements(lesson.data->'problems') problem(data) ON problem.data->>'id' = a."ProblemId"
            WHERE w."Status" = 'Submitted' AND (
                (NOT EXISTS (SELECT 1 FROM "AssignmentScopes" scoped WHERE scoped."AssignmentId"=a."Id") AND EXISTS (
                    SELECT 1 FROM "AccessTeachers" g WHERE g."TeacherId" = {actor.Id} AND g."CourseId" = a."CourseId"
                    AND (g."GroupId" IS NULL OR (e."IsActive" AND e."GroupId" = g."GroupId"))))
                OR EXISTS (
                    SELECT 1 FROM "AssignmentScopes" scoped
                    JOIN "TeacherAssignmentScopes" teacher ON teacher."CourseId"=scoped."CourseId" AND teacher."OrganizationId"=scoped."OrganizationId"
                    JOIN "AccessTeachers" g ON g."Id"=teacher."GrantId" AND g."TeacherId"=teacher."TeacherId" AND g."CourseId"=teacher."CourseId"
                        AND g."GroupId" IS NOT DISTINCT FROM teacher."GroupId"
                    JOIN "OrganizationMemberships" membership ON membership."OrganizationId"=teacher."OrganizationId"
                        AND membership."ActorId"=teacher."TeacherId" AND membership."Generation"=teacher."MembershipGeneration"
                    JOIN "CourseRegistry" course ON course."CourseId"=teacher."CourseId" AND course."OrganizationId"=teacher."OrganizationId"
                    WHERE scoped."AssignmentId"=a."Id" AND teacher."TeacherId"={actor.Id} AND teacher."State"='Active'
                        AND teacher."RevocationRequestedAt" IS NULL AND teacher."AccessValidUntil" IS NULL
                        AND membership."State"='Active' AND membership."RevocationRequestedAt" IS NULL AND membership."AccessValidUntil" IS NULL
                        AND course."MappingState"='Verified' AND (teacher."GroupId" IS NULL OR teacher."GroupId"=scoped."GroupId")))
            """);
        if (query.CourseId is not null) rows = rows.Where(r => r.CourseId == query.CourseId);
        if (query.GroupId is not null) rows = rows.Where(r => r.GroupId == query.GroupId);
        if (query.StudentId is not null) rows = rows.Where(r => r.StudentId == query.StudentId);
        if (query.AttemptKind == "first") rows = rows.Where(r => r.AttemptNumber == 1);
        if (query.AttemptKind == "revision") rows = rows.Where(r => r.AttemptNumber > 1);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Literal substring search; escape wildcard syntax. Profile comes only from the assignment immutable organization scope.
            var search = "%" + query.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            rows = rows.Where(r => EF.Functions.ILike(r.CourseId, search, "\\") ||
                EF.Functions.ILike(r.StudentDisplayName!, search, "\\") ||
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
        return new(page.Select(r => new ReviewQueueItemDto(r.ProblemWorkId, r.SubmissionId, r.StudentId, r.StudentDisplayName,
                r.CourseId, r.CourseTitle, r.LessonId, r.LessonTitle, r.GroupId, null, r.ProblemId, r.ProblemTitle,
                r.AttemptNumber, r.SubmittedAt, r.AttachmentCount,
                Math.Max(0, (now - r.SubmittedAt).TotalSeconds), r.Version)).ToArray(),
            query.Page, query.PageSize, total, new(total, first, total - first, average, byCourse));
    }
}
