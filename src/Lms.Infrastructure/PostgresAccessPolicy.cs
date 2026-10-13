using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

public sealed class PostgresAccessPolicy(LmsDbContext db, TimeProvider time) : IAccessPolicy
{
    public async Task EnsureCanSubmit(Actor actor, ProblemAssignment assignment, CancellationToken cancellationToken)
    {
        if (actor.Role != AuthorRole.Student) throw new UseCaseException("Forbidden");
        assignment.EnsureCanSubmit(actor.Id);
        if (!await db.Enrollments.AnyAsync(x => x.StudentId == actor.Id && x.CourseId == assignment.CourseId && x.IsActive, cancellationToken))
            throw new UseCaseException("Forbidden");
        var now = time.GetUtcNow();
        if (!await db.Lessons.AnyAsync(x => x.CourseId == assignment.CourseId &&
            x.CourseVersionId == assignment.Content.CourseVersionId && x.LessonId == assignment.Content.LessonId &&
            x.IsPublished && (x.AvailableFrom == null || x.AvailableFrom <= now), cancellationToken))
            throw new UseCaseException("Forbidden");
    }

    public async Task EnsureCanReview(Actor actor, ProblemAssignment assignment, CancellationToken cancellationToken)
    {
        if (actor.Role != AuthorRole.Teacher) throw new UseCaseException("Forbidden");
        var scoped = await db.Set<AssignmentScope>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.AssignmentId == assignment.Id, cancellationToken);
        var allowed = scoped == null
            ? await db.TeacherGrants.AnyAsync(g => g.TeacherId == actor.Id && g.CourseId == assignment.CourseId &&
                (g.GroupId == null || db.Enrollments.Any(e => e.StudentId == assignment.StudentId &&
                    e.CourseId == assignment.CourseId && e.IsActive && e.GroupId == g.GroupId)), cancellationToken)
            : await (from grant in db.Set<TeacherAssignmentScope>().AsNoTracking()
                join legacy in db.TeacherGrants.AsNoTracking() on grant.GrantId equals legacy.Id
                join membership in db.Set<OrganizationMembership>().AsNoTracking()
                    on new { grant.OrganizationId, ActorId = grant.TeacherId }
                    equals new { membership.OrganizationId, membership.ActorId }
                join course in db.Set<CourseRegistration>().AsNoTracking() on grant.CourseId equals course.CourseId
                where grant.TeacherId == actor.Id && grant.CourseId == scoped.CourseId &&
                    grant.OrganizationId == scoped.OrganizationId && grant.State == "Active" &&
                    grant.RevocationRequestedAt == null && grant.AccessValidUntil == null &&
                    grant.MembershipGeneration == membership.Generation && membership.State == "Active" &&
                    membership.RevocationRequestedAt == null && membership.AccessValidUntil == null &&
                    legacy.TeacherId == actor.Id && legacy.CourseId == scoped.CourseId && legacy.GroupId == grant.GroupId &&
                    course.OrganizationId == scoped.OrganizationId && course.MappingState == "Verified" &&
                    (grant.GroupId == null || grant.GroupId == scoped.GroupId)
                select grant).AnyAsync(cancellationToken);
        if (!allowed)
            throw new UseCaseException("Forbidden");
    }

    public Task EnsureCanPostMessage(Actor actor, ProblemAssignment assignment, CancellationToken cancellationToken)
        => actor.Role == AuthorRole.Student
            ? EnsureCanSubmit(actor, assignment, cancellationToken)
            : EnsureCanReview(actor, assignment, cancellationToken);

    public async Task EnsureCanUseAttachments(Actor actor, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return;
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Count)
            throw new UseCaseException("ValidationFailed");
        var now = time.GetUtcNow();
        if (await db.AttachmentMetadata.CountAsync(x => ids.Contains(x.Id) && x.OwnerId == actor.Id &&
            x.IsEligible && x.ExpiresAt > now && db.StoredAttachments.Any(s => s.Id == x.Id && s.OwnerId == actor.Id && !s.Deleted), cancellationToken) != ids.Count)
            throw new UseCaseException("AttachmentNotOwned");
        if (await db.StoredAttachments.AnyAsync(x => ids.Contains(x.Id) && (x.SubmissionId != null || x.MessageId != null), cancellationToken))
            throw new UseCaseException("AttachmentAlreadyAttached");
    }
}
