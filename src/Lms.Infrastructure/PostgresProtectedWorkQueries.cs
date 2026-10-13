using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

// Isolated read composition. No legacy Actor, writes, private storage or file authorization bypass.
public sealed class PostgresProtectedWorkQueries(LmsDbContext db, ComponentAuthorization authorization,
    IScopedAuthorityReader resources, ICurrentProcessingGate processing, bool photoReads = false) : IProtectedWorkQueries
{
    private static void Stage(ApplicationDeadline d)
    { if (!d.Check(ApplicationAction.StartStage).Allowed) throw new OperationCanceledException(); }
    private async Task<bool> Gate(TokenAuthority token, WorkReadGate gate, ApplicationDeadline d, CancellationToken ct)
    {
        Stage(d);
        if (!await authorization.Allows(token, gate.Resource, ScopeAction.Read, ct)) return false;
        if (gate.Subject is not { } subject) return true;
        // Embedded history authors are affected subjects too; the shared gate otherwise
        // checks only the requesting actor and work student. Never narrow its restriction predicate.
        Stage(d);
        var owner = await resources.ResolveResource(gate.Resource, ct);
        return owner != null && await db.Set<OrganizationMembership>().AsNoTracking().AnyAsync(m =>
            m.OrganizationId == owner.OrganizationId && m.ActorId == subject, ct) &&
            await processing.Allows(owner, subject, ScopeAction.Read, ct);
    }
    private async Task<bool> Parent(WorkSelection s, Guid id, CancellationToken ct) => await (
        from w in db.Works.AsNoTracking() join a in db.Assignments.AsNoTracking() on w.ProblemAssignmentId equals a.Id
        where w.Id == id && w.StudentId == a.StudentId && a.CourseId == s.CourseId &&
            a.Content.CourseVersionId == s.VersionId && a.Content.LessonId == s.LessonId &&
            (s.ProblemId == null || a.Content.ProblemId == s.ProblemId)
        select w.Id).AnyAsync(ct);
    public async Task<bool> CanReturn(TokenAuthority token, ProtectedWorkRead read, ApplicationDeadline d, CancellationToken ct)
    {
        foreach (var gate in read.Gates)
        {
            if (!await Gate(token, gate, d, ct)) return false;
            if (gate.Resource.Kind == ResourceKind.Work && !await Parent(read.Selection, gate.Resource.Id, ct)) return false;
            Stage(d);
            if (gate.WorkVersion is { } version && !await db.Works.AsNoTracking().AnyAsync(w => w.Id == gate.Resource.Id && w.Version == version, ct)) return false;
        }
        return true;
    }
    private static string Root(WorkSelection s) => $"/api/v1/courses/{Uri.EscapeDataString(s.CourseId)}/versions/{s.VersionId}/lessons/{Uri.EscapeDataString(s.LessonId)}";
    public async Task<ProtectedWorkRead> Read(TokenAuthority token, WorkSelection s, ApplicationDeadline d, CancellationToken ct)
    {
        var gates = new List<WorkReadGate>();
        async Task Require(WorkReadGate gate)
        { if (!await Gate(token, gate, d, ct)) throw new UseCaseException("Forbidden"); gates.Add(gate); }
        await Require(new(new(ResourceKind.Course, CourseId: s.CourseId)));
        Stage(d);
        // Verify immutable content parent independently; course permission never grants work access.
        var (_, content) = await new PostgresContentResolver(db).Version(s.CourseId, s.VersionId, ct);
        var lesson = content.Lessons.SingleOrDefault(l => l.Id == s.LessonId) ?? throw new UseCaseException("Forbidden");
        if (s.ProblemId != null && !lesson.Problems.Any(p => p.Id == s.ProblemId)) throw new UseCaseException("Forbidden");
        if (s.WorkId == null)
        {
            Stage(d);
            var candidates = await (from w in db.Works.AsNoTracking()
                join a in db.Assignments.AsNoTracking() on w.ProblemAssignmentId equals a.Id
                where a.CourseId == s.CourseId && a.Content.CourseVersionId == s.VersionId && a.Content.LessonId == s.LessonId &&
                    (s.ProblemId == null || a.Content.ProblemId == s.ProblemId)
                select w.Id).ToListAsync(ct);
            var items = new List<WorkLinkDto>();
            foreach (var id in candidates)
            {
                var gate = new WorkReadGate(new(ResourceKind.Work, id, s.CourseId));
                if (!await Gate(token, gate, d, ct) || !await Parent(s, id, ct)) continue;
                Stage(d);
                var row = await (from w in db.Works.AsNoTracking() join a in db.Assignments.AsNoTracking() on w.ProblemAssignmentId equals a.Id
                    where w.Id == id select new { w.Status, w.Version, a.Content.ProblemId }).SingleAsync(ct);
                if (!lesson.Problems.Any(p => p.Id == row.ProblemId)) throw new UseCaseException("Forbidden");
                gates.Add(gate with { WorkVersion = row.Version });
                items.Add(new(id, row.ProblemId, row.Status.ToString(), Root(s) + "/works/" + id));
            }
            // No pagination, counts, profiles, student IDs or unauthorized sorting inputs.
            return new(new WorkListDto(items.OrderBy(i => i.Id).ToArray()), s, gates);
        }
        var workId = s.WorkId.Value;
        var workRef = new ResourceReference(ResourceKind.Work, workId, s.CourseId);
        await Require(new(workRef));
        if (!await Parent(s, workId, ct)) throw new UseCaseException("Forbidden");
        Stage(d);
        var work = await (from w in db.Works.AsNoTracking() join a in db.Assignments.AsNoTracking() on w.ProblemAssignmentId equals a.Id
            where w.Id == workId select new { w.Status, w.Version, w.StudentId, a.Content.ProblemId }).SingleAsync(ct);
        if (!lesson.Problems.Any(p => p.Id == work.ProblemId)) throw new UseCaseException("Forbidden");
        gates.Add(new(workRef, WorkVersion: work.Version));
        Stage(d);
        // Only identity/parent selectors before per-resource authorization, no body/feedback.
        var submissions = await db.Set<Submission>().AsNoTracking().Where(x => x.ProblemWorkId == workId).Select(x => x.Id).ToListAsync(ct);
        foreach (var id in submissions) await Require(new(new(ResourceKind.Submission, id, s.CourseId)));
        Stage(d);
        var reviews = await db.Set<Review>().AsNoTracking().Where(x => x.ProblemWorkId == workId)
            .Select(x => new { x.Id, x.SubmissionId, x.TeacherId, x.MessageId }).ToListAsync(ct);
        foreach (var r in reviews)
        {
            if (!submissions.Contains(r.SubmissionId)) throw new UseCaseException("Forbidden");
            await Require(new(new(ResourceKind.Review, r.Id, s.CourseId), r.TeacherId));
        }
        Stage(d);
        var messages = await db.Set<Message>().AsNoTracking().Where(x => x.ProblemWorkId == workId)
            .Select(x => new { x.Id, x.AuthorId, x.AuthorRole, x.ReplyToMessageId }).ToListAsync(ct);
        foreach (var m in messages)
        {
            if (!Enum.IsDefined(m.AuthorRole) || m.AuthorRole == AuthorRole.Student && m.AuthorId != work.StudentId ||
                m.ReplyToMessageId != null && !messages.Any(p => p.Id == m.ReplyToMessageId)) throw new UseCaseException("Forbidden");
            await Require(new(workRef, m.AuthorId));
        }
        foreach (var r in reviews)
            if (r.MessageId is { } id && !messages.Any(m => m.Id == id && m.AuthorId == r.TeacherId && m.AuthorRole == AuthorRole.Teacher))
                throw new UseCaseException("Forbidden");
        Stage(d);
        // Capture the authorized ID set: a concurrent append cannot enter this response un-gated.
        var messageIds = messages.Select(x => x.Id).ToArray();
        var bodies = await db.Set<Message>().AsNoTracking().Where(x => messageIds.Contains(x.Id))
            .Select(x => new WorkMessageDto(x.Id, x.AuthorRole.ToString(), x.BodyMarkdown, x.CreatedAt, x.AttachmentIds.Count)).ToListAsync(ct);
        var reviewIds = reviews.Select(x => x.Id).ToArray();
        Stage(d);
        var reviewBodies = await db.Set<Review>().AsNoTracking().Where(x => reviewIds.Contains(x.Id))
            .Select(x => new { x.Id, x.SubmissionId, x.Decision, x.MessageId, x.CreatedAt }).ToListAsync(ct);
        Stage(d);
        var attempts = await db.Set<Submission>().AsNoTracking().Where(x => submissions.Contains(x.Id))
            .Select(x => new { x.Id, x.AttemptNumber, x.BodyMarkdown, x.SubmittedAt, PhotoCount = x.AttachmentIds.Count }).ToListAsync(ct);
        return new(new ProtectedWorkDto(workId, work.Status.ToString(), Root(s) + "/problems/" + Uri.EscapeDataString(work.ProblemId),
            attempts.OrderBy(x => x.AttemptNumber).Select(x => new WorkAttemptDto(x.Id, x.AttemptNumber, x.BodyMarkdown, x.SubmittedAt,
                x.PhotoCount, photoReads ? "protected" : "unavailable", reviewBodies.Where(r => r.SubmissionId == x.Id).OrderBy(r => r.CreatedAt).Select(r =>
                    new WorkReviewDto(r.Id, r.Decision.ToString(), r.MessageId is { } mid ? bodies.Single(m => m.Id == mid).BodyMarkdown : null, r.CreatedAt)).ToArray())).ToArray(),
            bodies.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToArray()), s, gates);
    }
}
