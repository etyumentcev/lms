using System.Data;
using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lms.Infrastructure;

// These are fresh DB authority checks for internal use, not per-request JWT middleware.
// S4 must decide issuance in its security transaction with the organization lock, not sign
// a token later from a detached result. Resource affiliations are server-derived and immutable.
public sealed class PostgresScopedAuthorityReader(LmsDbContext db, TimeProvider clock) : IScopedAuthorityReader
{
    private async Task<IDbContextTransaction?> Snapshot(CancellationToken ct)
        => db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct) : null;
    public async Task<CurrentAuthority?> ResolveCurrent(Guid organizationId, Guid actorId, CancellationToken cancellationToken = default)
    {
        await using var tx = await Snapshot(cancellationToken);
        return await Current(organizationId, actorId, cancellationToken);
    }
    private async Task<CurrentAuthority?> Current(Guid org, Guid actor, CancellationToken ct)
    {
        if (!await db.Set<Organization>().AsNoTracking().AnyAsync(x => x.OrganizationId == org && x.State == "Enabled" && x.SyntheticOnly, ct)) return null;
        var m = await db.Set<OrganizationMembership>().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.ActorId == actor && x.State == "Active", ct);
        if (m == null || m.RevocationRequestedAt != null || m.AccessValidUntil != null) return null;
        ScopeRole? role = m.EducationalRole switch { "Student" => ScopeRole.Student, "Teacher" => ScopeRole.Teacher, _ => null };
        var membership = new MembershipAuthority(org, actor, m.Generation, m.Version, role, m.PermissionFlags);
        var grants = new List<EducationalGrant>();
        if (role == ScopeRole.Student)
        {
            var rows = await (from s in db.Set<EnrollmentScope>().AsNoTracking()
                join e in db.Enrollments.AsNoTracking() on new { s.StudentId, s.CourseId } equals new { e.StudentId, e.CourseId }
                join c in db.Set<CourseRegistration>().AsNoTracking() on s.CourseId equals c.CourseId
                where s.StudentId == actor && s.OrganizationId == org && s.State == "Active" && s.MembershipGeneration == m.Generation &&
                    s.RevocationRequestedAt == null && s.AccessValidUntil == null && e.IsActive && e.GroupId == s.GroupId &&
                    c.OrganizationId == org && c.MappingState == "Verified"
                select s).ToListAsync(ct);
            grants.AddRange(rows.Select(x => new EducationalGrant(AuthorityKind.Enrollment, null, x.CourseId, x.GroupId, x.MembershipGeneration, x.EnrollmentGeneration, x.Version)));
        }
        if (role == ScopeRole.Teacher)
        {
            var rows = await (from s in db.Set<TeacherAssignmentScope>().AsNoTracking()
                join t in db.TeacherGrants.AsNoTracking() on s.GrantId equals t.Id
                join c in db.Set<CourseRegistration>().AsNoTracking() on s.CourseId equals c.CourseId
                where s.TeacherId == actor && s.OrganizationId == org && s.State == "Active" && s.MembershipGeneration == m.Generation &&
                    s.RevocationRequestedAt == null && s.AccessValidUntil == null && t.TeacherId == actor && t.CourseId == s.CourseId && t.GroupId == s.GroupId &&
                    c.OrganizationId == org && c.MappingState == "Verified"
                select s).ToListAsync(ct);
            grants.AddRange(rows.Select(x => new EducationalGrant(AuthorityKind.Teacher, x.GrantId, x.CourseId, x.GroupId, x.MembershipGeneration, x.Generation, x.Version)));
        }
        return new(membership, grants.AsReadOnly());
    }
    public async Task<ResourceOwnership?> ResolveResource(ResourceReference reference, CancellationToken cancellationToken = default)
    {
        await using var tx = await Snapshot(cancellationToken);
        return await Resource(reference, cancellationToken);
    }
    private async Task<ResourceOwnership?> Course(string? course, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(course)) return null;
        return await (from c in db.Set<CourseRegistration>().AsNoTracking()
            join o in db.Set<Organization>().AsNoTracking() on c.OrganizationId equals o.OrganizationId
            where c.CourseId == course && c.MappingState == "Verified" && o.State == "Enabled" && o.SyntheticOnly
            select new ResourceOwnership(o.OrganizationId, c.CourseId, null, null, null, null, null, null, null, null, false, null, null)).SingleOrDefaultAsync(ct);
    }
    private async Task<ResourceOwnership?> Assignment(Guid id, CancellationToken ct)
    {
        var a = await (from s in db.Set<AssignmentScope>().AsNoTracking()
            join assignment in db.Assignments.AsNoTracking() on s.AssignmentId equals assignment.Id
            where s.AssignmentId == id && s.StudentId == assignment.StudentId && s.CourseId == assignment.CourseId
            select s).SingleOrDefaultAsync(ct);
        if (a == null) return null;
        var course = await Course(a.CourseId, ct);
        return course?.OrganizationId == a.OrganizationId ? course with { GroupId = a.GroupId, StudentId = a.StudentId, AssignmentId = id, MembershipGeneration = a.MembershipGeneration, EnrollmentGeneration = a.EnrollmentGeneration } : null;
    }
    private async Task<ResourceOwnership?> Work(Guid id, CancellationToken ct)
    {
        var w = await db.Works.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.ProblemAssignmentId, x.StudentId }).SingleOrDefaultAsync(ct);
        if (w == null) return null;
        var a = await Assignment(w.ProblemAssignmentId, ct);
        return a?.StudentId == w.StudentId ? a with { WorkId = id } : null;
    }
    private async Task<ResourceOwnership?> Submission(Guid id, CancellationToken ct)
    {
        var s = await db.Set<Submission>().AsNoTracking().Where(x => x.Id == id).Select(x => new { x.ProblemWorkId, x.SubmittedBy }).SingleOrDefaultAsync(ct);
        if (s == null) return null;
        var w = await Work(s.ProblemWorkId, ct);
        return w?.StudentId == s.SubmittedBy ? w with { SubmissionId = id } : null;
    }
    private async Task<ResourceOwnership?> Resource(ResourceReference r, CancellationToken ct)
    {
        if (!Enum.IsDefined(r.Kind)) return null;
        ResourceOwnership? result;
        switch (r.Kind)
        {
            case ResourceKind.Course: result = await Course(r.CourseId, ct); break;
            case ResourceKind.Group:
                result = !string.IsNullOrWhiteSpace(r.GroupId) && await db.Set<CourseGroup>().AsNoTracking().AnyAsync(x => x.CourseId == r.CourseId && x.GroupId == r.GroupId, ct)
                    ? (await Course(r.CourseId, ct)) is { } c ? c with { GroupId = r.GroupId } : null : null;
                break;
            case ResourceKind.Assignment: result = await Assignment(r.Id, ct); break;
            case ResourceKind.Work: result = await Work(r.Id, ct); break;
            case ResourceKind.Submission: result = await Submission(r.Id, ct); break;
            case ResourceKind.Review:
                var review = await db.Set<Review>().AsNoTracking().Where(x => x.Id == r.Id).Select(x => new { x.SubmissionId, x.ProblemWorkId }).SingleOrDefaultAsync(ct);
                var parent = review == null ? null : await Submission(review.SubmissionId, ct);
                result = parent?.WorkId == review?.ProblemWorkId && parent != null ? parent with { ReviewId = r.Id } : null;
                break;
            case ResourceKind.Attachment: result = await Attachment(r.Id, ct); break;
            default: return null;
        }
        // IDs in caller input are candidates only; reject contradictory routing hints.
        return result != null && (r.CourseId == null || r.CourseId == result.CourseId) && (r.GroupId == null || r.GroupId == result.GroupId) ? result : null;
    }
    private async Task<ResourceOwnership?> Attachment(Guid id, CancellationToken ct)
    {
        var s = await db.Set<AttachmentScope>().AsNoTracking().SingleOrDefaultAsync(x => x.AttachmentId == id && !x.Quarantined, ct);
        var stored = await db.StoredAttachments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
        if (s == null || stored == null || stored.OwnerId != s.UploaderActorId ||
            !await db.Set<ProcessingContextVersion>().AsNoTracking().AnyAsync(x => x.ContextId == s.ContextId && x.Version == s.ContextVersion &&
                x.OrganizationId == s.OrganizationId && x.SyntheticOnly && x.ApprovalState == "SyntheticOnly", ct)) return null;
        var course = await Course(s.CourseId, ct);
        if (course?.OrganizationId != s.OrganizationId) return null;
        ResourceOwnership? parent;
        if (stored.SubmissionId is { } submission)
        {
            var data = await db.Set<Submission>().AsNoTracking().Where(x => x.Id == submission).Select(x => new { x.AttachmentIds, x.SubmittedBy }).SingleOrDefaultAsync(ct);
            if (data == null || !data.AttachmentIds.Contains(id) || data.SubmittedBy != s.UploaderActorId) return null;
            parent = await Submission(submission, ct);
        }
        else if (stored.MessageId is { } message)
        {
            var data = await db.Set<Message>().AsNoTracking().Where(x => x.Id == message).Select(x => new { x.AttachmentIds, x.AuthorId, x.ProblemWorkId }).SingleOrDefaultAsync(ct);
            if (data == null || !data.AttachmentIds.Contains(id) || data.AuthorId != s.UploaderActorId) return null;
            parent = await Work(data.ProblemWorkId, ct);
        }
        else
        {
            var now = clock.GetUtcNow();
            return await db.AttachmentMetadata.AsNoTracking().AnyAsync(x => x.Id == id && x.OwnerId == s.UploaderActorId && x.IsEligible && x.ExpiresAt > now, ct)
                ? course with { AttachmentId = id, UploaderActorId = s.UploaderActorId, PendingAttachment = true } : null;
        }
        return parent?.CourseId == s.CourseId && parent.OrganizationId == s.OrganizationId
            ? parent with { AttachmentId = id, UploaderActorId = s.UploaderActorId } : null;
    }
    public async Task<bool> CanAccess(Guid organizationId, Guid actorId, ResourceReference reference, ScopeAction action, CancellationToken cancellationToken = default)
    {
        await using var tx = await Snapshot(cancellationToken);
        var authority = await Current(organizationId, actorId, cancellationToken);
        var resource = await Resource(reference, cancellationToken);
        if (authority == null || resource == null || !ScopePolicy.Allows(authority, resource, action, actorId)) return false;
        if (action == ScopeAction.Read) return true;
        if (action == ScopeAction.Submit && reference.Kind is ResourceKind.Assignment or ResourceKind.Work && resource.AssignmentId is { } assignmentId)
        {
            var a = await db.Assignments.AsNoTracking().SingleAsync(x => x.Id == assignmentId, cancellationToken);
            var now = clock.GetUtcNow();
            return a.Status == AssignmentStatus.Active &&
                !await db.Works.AnyAsync(x => x.ProblemAssignmentId == assignmentId && (x.Status == ProblemWorkStatus.Accepted || x.Status == ProblemWorkStatus.Submitted), cancellationToken) &&
                await db.Lessons.AnyAsync(x => x.CourseId == a.CourseId && x.CourseVersionId == a.Content.CourseVersionId && x.LessonId == a.Content.LessonId && x.IsPublished &&
                    (x.AvailableFrom == null || x.AvailableFrom <= now), cancellationToken);
        }
        return action == ScopeAction.Review && reference.Kind == ResourceKind.Submission &&
            await db.Works.AnyAsync(x => x.Id == resource.WorkId && x.Status == ProblemWorkStatus.Submitted, cancellationToken) &&
            !await db.Set<Review>().AnyAsync(x => x.SubmissionId == resource.SubmissionId, cancellationToken) &&
            resource.SubmissionId == await db.Set<Submission>().Where(x => x.ProblemWorkId == resource.WorkId).OrderByDescending(x => x.AttemptNumber).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
    }
}
