using System.Data;
using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

public sealed partial class PostgresWorkspaceQueries(LmsDbContext db, IAccessPolicy access,
    IImmutableContentResolver content, TimeProvider time) : IWorkspaceQueries
{
    private IQueryable<TeacherAssignmentScope> ActiveTeacherScopes(Guid teacherId, string courseId) =>
        from grant in db.Set<TeacherAssignmentScope>().AsNoTracking()
        join legacy in db.TeacherGrants.AsNoTracking() on grant.GrantId equals legacy.Id
        join membership in db.Set<OrganizationMembership>().AsNoTracking()
            on new { grant.OrganizationId, ActorId = grant.TeacherId }
            equals new { membership.OrganizationId, membership.ActorId }
        join course in db.Set<CourseRegistration>().AsNoTracking() on grant.CourseId equals course.CourseId
        where grant.TeacherId == teacherId && grant.CourseId == courseId && grant.State == "Active" &&
            grant.RevocationRequestedAt == null && grant.AccessValidUntil == null &&
            grant.MembershipGeneration == membership.Generation && membership.State == "Active" &&
            membership.RevocationRequestedAt == null && membership.AccessValidUntil == null &&
            legacy.TeacherId == teacherId && legacy.CourseId == courseId && legacy.GroupId == grant.GroupId &&
            course.OrganizationId == grant.OrganizationId && course.MappingState == "Verified"
        select grant;

    private IQueryable<ProblemAssignment> VisibleAssignments(Actor actor, string courseId)
    {
        var query = db.Assignments.AsNoTracking().Where(a => a.CourseId == courseId && a.Status == AssignmentStatus.Active);
        return actor.Role == AuthorRole.Student ? query.Where(a => a.StudentId == actor.Id) : query.Where(a =>
            db.Set<AssignmentScope>().Any(s => s.AssignmentId == a.Id)
                ? db.Set<AssignmentScope>().Any(scope => scope.AssignmentId == a.Id &&
                    db.Set<TeacherAssignmentScope>().Any(grant => grant.TeacherId == actor.Id &&
                        grant.CourseId == a.CourseId && grant.OrganizationId == scope.OrganizationId &&
                        grant.State == "Active" && grant.RevocationRequestedAt == null && grant.AccessValidUntil == null &&
                        (grant.GroupId == null || grant.GroupId == scope.GroupId) &&
                        db.TeacherGrants.Any(legacy => legacy.Id == grant.GrantId && legacy.TeacherId == actor.Id &&
                            legacy.CourseId == grant.CourseId && legacy.GroupId == grant.GroupId) &&
                        db.Set<OrganizationMembership>().Any(membership => membership.OrganizationId == grant.OrganizationId &&
                            membership.ActorId == actor.Id && membership.Generation == grant.MembershipGeneration &&
                            membership.State == "Active" && membership.RevocationRequestedAt == null && membership.AccessValidUntil == null) &&
                        db.Set<CourseRegistration>().Any(registry => registry.CourseId == grant.CourseId &&
                            registry.OrganizationId == grant.OrganizationId && registry.MappingState == "Verified")))
                : db.TeacherGrants.Any(g => g.TeacherId == actor.Id && g.CourseId == a.CourseId &&
                    (g.GroupId == null || db.Enrollments.Any(e => e.StudentId == a.StudentId && e.CourseId == a.CourseId && e.IsActive && e.GroupId == g.GroupId))));
    }

    private async Task<EnrollmentAccess?> CourseAccess(Actor actor, string courseId, CancellationToken ct)
    {
        if (actor.Role == AuthorRole.Student)
            return await db.Enrollments.AsNoTracking().SingleOrDefaultAsync(e => e.StudentId == actor.Id && e.CourseId == courseId && e.IsActive, ct)
                ?? throw new UseCaseException("Forbidden");
        if (actor.Role != AuthorRole.Teacher) throw new UseCaseException("Forbidden");
        var scoped = await db.Set<CourseRegistration>().AsNoTracking().AnyAsync(x => x.CourseId == courseId &&
            x.MappingState == "Verified" && x.OrganizationId != null, ct);
        var allowed = scoped ? await ActiveTeacherScopes(actor.Id, courseId).AnyAsync(ct)
            : await db.TeacherGrants.AnyAsync(g => g.TeacherId == actor.Id && g.CourseId == courseId, ct);
        if (!allowed)
            throw new UseCaseException("Forbidden");
        return null;
    }

    private Task WorkAccess(Actor actor, ProblemAssignment assignment, CancellationToken ct)
        => actor.Role == AuthorRole.Student ? access.EnsureCanSubmit(actor, assignment, ct) : access.EnsureCanReview(actor, assignment, ct);

    private AvailabilityDto Availability(LessonAccess lesson)
        => !lesson.IsPublished ? new(false, lesson.AvailableFrom, "NotPublished")
        : lesson.AvailableFrom > time.GetUtcNow() ? new(false, lesson.AvailableFrom, "Scheduled")
        : new(true, lesson.AvailableFrom, "Available");

    private async Task<AvailabilityDto> AssignmentAvailability(ProblemAssignment assignment, CancellationToken ct)
    {
        var lesson = await db.Lessons.AsNoTracking().SingleOrDefaultAsync(l => l.CourseId == assignment.CourseId &&
            l.CourseVersionId == assignment.Content.CourseVersionId && l.LessonId == assignment.Content.LessonId, ct);
        return lesson is null ? new(false, null, "NotPublished") : Availability(lesson);
    }

    private static HistoryDto History(ProblemWork? work)
        => new(work?.Submissions.OrderByDescending(s => s.AttemptNumber).Select(s =>
                new SubmissionDto(s.Id, s.AttemptNumber, s.BodyMarkdown, s.SubmittedAt, s.AttachmentIds)).ToArray() ?? [],
            work?.Reviews.OrderByDescending(r => work.Submissions.Single(s => s.Id == r.SubmissionId).AttemptNumber)
                .Select(r => new ReviewDto(r.Id, r.SubmissionId, r.TeacherId, r.Decision, r.MessageId, r.CreatedAt, r.Annotations)).ToArray() ?? [],
            work?.Messages.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).Select(m =>
                new MessageDto(m.Id, new(m.AuthorId), m.AuthorRole, m.BodyMarkdown, m.ReplyToMessageId, m.CreatedAt, m.AttachmentIds)).ToArray() ?? []);

    private static WorkSummaryDto Summary(ProblemWork? work, HistoryDto history)
    {
        var review = history.Reviews.FirstOrDefault();
        return new(work?.Id, work?.Status ?? ProblemWorkStatus.NotStarted, work?.AttemptCount ?? 0, work?.Version,
            history.Submissions.FirstOrDefault(), review, history.Messages.SingleOrDefault(m => m.Id == review?.MessageId));
    }

    // Access was checked before deriving status-dependent capabilities. Commands still reauthorize.
    private static PermissionsDto Permissions(Actor actor, ProblemWork? work, bool available = true)
    {
        var student = actor.Role == AuthorRole.Student;
        var review = actor.Role == AuthorRole.Teacher && work?.Status == ProblemWorkStatus.Submitted;
        return new(student && available && (work is null || work.Status is ProblemWorkStatus.NotStarted or ProblemWorkStatus.NeedsRevision),
            work is not null && (!student || available), review, review, actor.Role == AuthorRole.Teacher);
    }

    public async Task<StudentProblemWorkspaceDto> StudentProblem(Actor actor, Guid assignmentId, CancellationToken ct)
    {
        if (actor.Role != AuthorRole.Student) throw new UseCaseException("Forbidden");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var assignment = await db.Assignments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == assignmentId, ct)
            ?? throw new UseCaseException("NotFound");
        await WorkAccess(actor, assignment, ct);
        var resolved = await content.Resolve(assignment.CourseId, assignment.Content, ct);
        var work = await new PostgresWorkStore(db).FindByAssignment(assignmentId, ct);
        var history = History(work);
        return new(new(assignment.Id, true), resolved.Course, resolved.Lesson, resolved.Problem,
            await AssignmentAvailability(assignment, ct), Summary(work, history), [], history, [], Permissions(actor, work));
    }

    public async Task<ProblemWorkDetailsDto> ProblemWork(Actor actor, Guid workId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var work = await new PostgresWorkStore(db).FindWork(workId, ct) ?? throw new UseCaseException("NotFound");
        var assignment = await db.Assignments.AsNoTracking().SingleAsync(a => a.Id == work.ProblemAssignmentId, ct);
        await WorkAccess(actor, assignment, ct);
        var resolved = await content.Resolve(assignment.CourseId, assignment.Content, ct);
        var history = History(work);
        return new(work.Id, assignment.Id, work.Status, work.Version, work.AttemptCount, new(work.StudentId),
            resolved.Course, resolved.Lesson, resolved.Problem, history.Submissions.FirstOrDefault(),
            history.Submissions, history.Reviews, history.Messages, Permissions(actor, work));
    }

    private sealed record ProgressRow(Guid AssignmentId, Guid CourseVersionId, string LessonId, ProblemWorkStatus Status);
    private async Task<List<ProgressRow>> ProgressRows(Actor actor, string courseId, CancellationToken ct)
        => await (from a in VisibleAssignments(actor, courseId)
                  join w in db.Works.AsNoTracking() on a.Id equals w.ProblemAssignmentId into works
                  from w in works.DefaultIfEmpty()
                  select new ProgressRow(a.Id, a.Content.CourseVersionId, a.Content.LessonId,
                      w == null ? ProblemWorkStatus.NotStarted : w.Status)).ToListAsync(ct);

    private static decimal Percent(int numerator, int denominator) => denominator == 0 ? 0 : Math.Round(100m * numerator / denominator, 2);
    private static LessonProgressDto Progress(IEnumerable<ProgressRow> source)
    {
        var rows = source.ToArray();
        var accepted = rows.Count(r => r.Status == ProblemWorkStatus.Accepted);
        var status = rows.Length > 0 && accepted == rows.Length ? "Completed"
            : rows.All(r => r.Status == ProblemWorkStatus.NotStarted) ? "NotStarted" : "InProgress";
        return new(status, accepted, rows.Length, Percent(accepted, rows.Length));
    }

    private async Task<(CourseDto Course, LessonDto Lesson)> LessonContent(LessonAccess lesson, CancellationToken ct)
    {
        var resolver = new PostgresContentResolver(db);
        var (_, snapshot) = await resolver.Version(lesson.CourseId, lesson.CourseVersionId, ct);
        var data = snapshot.Lessons.SingleOrDefault(l => l.Id == lesson.LessonId) ?? throw new UseCaseException("ContentUnavailable");
        return (new(lesson.CourseId, snapshot.Title, lesson.CourseVersionId, snapshot.Description),
            new(data.Id, lesson.CourseVersionId, data.Order, data.Title, data.Summary));
    }

    public async Task<CourseWorkspaceDto> Course(Actor actor, string courseId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var enrollment = await CourseAccess(actor, courseId, ct);
        var lessons = await db.Lessons.AsNoTracking().Where(l => l.CourseId == courseId).OrderBy(l => db.ContentVersions.Where(v => v.Id == l.CourseVersionId).Select(v => v.VersionNumber).FirstOrDefault()).ThenBy(l => l.LessonId).ToListAsync(ct);
        var progress = await ProgressRows(actor, courseId, ct);
        var rows = new List<CourseLessonRowDto>();
        CourseDto? course = null;
        foreach (var lesson in lessons)
        {
            var metadata = await LessonContent(lesson, ct);
            course ??= metadata.Course;
            var availability = Availability(lesson);
            // Only title/order/version/availability are disclosed for locked lessons.
            rows.Add(new(metadata.Lesson with { Summary = actor.Role == AuthorRole.Student && !availability.IsAvailable ? null : metadata.Lesson.Summary },
                availability, Progress(progress.Where(r => r.CourseVersionId == lesson.CourseVersionId && r.LessonId == lesson.LessonId)), [lesson.CourseVersionId]));
        }
        if (course is null)
        {
            var version = await db.ContentVersions.AsNoTracking().Where(v => v.CourseId == courseId).OrderBy(v => v.VersionNumber).FirstOrDefaultAsync(ct);
            if (version is null) throw new UseCaseException("NotFound");
            var (_, snapshot) = await new PostgresContentResolver(db).Version(courseId, version.Id, ct);
            course = new(courseId, snapshot.Title, version.Id, snapshot.Description);
        }
        // A mixed-version workspace cannot claim to represent one published course version.
        if (lessons.Select(l => l.CourseVersionId).Distinct().Count() > 1) course = course with { PublishedVersionId = null };
        // Stable lesson identity determines course progress, not the number of publications.
        rows = rows.GroupBy(r => r.Lesson.Id).Select(group =>
        {
            var open = group.Where(r => r.Availability.IsAvailable).ToArray();
            var primary = open.FirstOrDefault() ?? group.First();
            var visibleVersions = (actor.Role == AuthorRole.Teacher ? group.AsEnumerable() : open).Select(r => r.Lesson.CourseVersionId).ToArray();
            var lessonProgress = Progress(progress.Where(r => r.LessonId == group.Key && visibleVersions.Contains(r.CourseVersionId)));
            return new CourseLessonRowDto(primary.Lesson, primary.Availability, lessonProgress,
                group.Select(r => r.Lesson.CourseVersionId).ToArray());
        }).ToList();
        var available = rows.Where(r => r.Availability.IsAvailable).ToArray();
        var completed = available.Count(r => r.Progress.Status == "Completed");
        var grants = actor.Role == AuthorRole.Teacher
            ? await db.TeacherGrants.AsNoTracking().Where(g => g.TeacherId == actor.Id && g.CourseId == courseId).Select(g => g.GroupId).Distinct().ToArrayAsync(ct) : null;
        return new(course, enrollment is null ? null : new(null, enrollment.StudentId, enrollment.CourseId, enrollment.GroupId, "Active"),
            grants is null ? null : new(actor.Id, true, grants), new(completed, available.Length, rows.Count, Percent(completed, available.Length)),
            rows.OrderBy(r => r.Lesson.Order).ThenBy(r => r.Lesson.Id).ThenBy(r => r.Lesson.CourseVersionId).ToArray(), []);
    }

    public async Task<LessonWorkspaceDto> Lesson(Actor actor, string courseId, string lessonId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await CourseAccess(actor, courseId, ct);
        var lessons = await db.Lessons.AsNoTracking().Where(l => l.CourseId == courseId && l.LessonId == lessonId)
            .OrderBy(l => db.ContentVersions.Where(v => v.Id == l.CourseVersionId).Select(v => v.VersionNumber).FirstOrDefault()).ToListAsync(ct);
        if (lessons.Count == 0) throw new UseCaseException("NotFound");
        // For students omit locked versions when other versions of the lesson are available.
        var visible = actor.Role == AuthorRole.Student ? lessons.Where(l => Availability(l).IsAvailable).ToList() : lessons;
        if (visible.Count == 0) throw new UseCaseException("Forbidden");
        var ids = visible.Select(l => l.CourseVersionId).ToArray();
        var assignments = await VisibleAssignments(actor, courseId).Where(a => a.Content.LessonId == lessonId && ids.Contains(a.Content.CourseVersionId)).ToListAsync(ct);
        var assignmentIds = assignments.Select(a => a.Id).ToArray();
        var works = await db.Works.AsNoTracking().Include(w => w.Submissions).Include(w => w.Reviews).Include(w => w.Messages)
            .AsSplitQuery().Where(w => assignmentIds.Contains(w.ProblemAssignmentId)).ToDictionaryAsync(w => w.ProblemAssignmentId, ct);
        var problems = new List<LessonProblemRowDto>();
        foreach (var assignment in assignments)
        {
            var resolved = await content.Resolve(courseId, assignment.Content, ct);
            works.TryGetValue(assignment.Id, out var work);
            var availability = await AssignmentAvailability(assignment, ct);
            problems.Add(new(assignment.Id, assignment.StudentId, resolved.Problem, availability, Summary(work, History(work)), Permissions(actor, work, availability.IsAvailable)));
        }
        var metadata = new List<LessonDto>();
        CourseDto? course = null;
        foreach (var lesson in visible)
        {
            var item = await LessonContent(lesson, ct);
            course ??= item.Course;
            metadata.Add(item.Lesson);
        }
        if (ids.Length > 1) course = course! with { PublishedVersionId = null };
        var primary = visible.FirstOrDefault(l => Availability(l).IsAvailable) ?? visible[0];
        return new(course!, metadata.Single(l => l.CourseVersionId == primary.CourseVersionId), metadata, Availability(primary),
            Progress((await ProgressRows(actor, courseId, ct)).Where(r => r.LessonId == lessonId && ids.Contains(r.CourseVersionId))),
            problems.OrderBy(p => p.Problem.Order).ThenBy(p => p.ProblemAssignmentId).ToArray(), []);
    }
}
