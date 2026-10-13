using System.Data;
using System.Text.Json;
using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

// Read-only M1-10 projections. Candidate IDs come from immutable server-owned scope rows;
// every candidate is authorized before its profile/content/status is materialized, and HTTP
// adapters repeat authorization immediately before returning the buffered response.
public sealed class PostgresProgressHistoryQueries(
    IDbContextFactory<LmsDbContext> contexts,
    IAuthorityClock clock,
    TimeProvider time) : IProgressHistoryQueries
{
    private sealed record ScopePair(Guid OrganizationId, string CourseId, string? GroupId);
    private sealed record RosterRow(Guid StudentId, string? DisplayName, string CourseId,
        string? CourseTitle, string? GroupId, string? GroupTitle);
    private sealed record AssignmentRow(ProblemAssignment Assignment, AssignmentScope Scope,
        ProblemWork? Work, ProgressAssignmentDto Projection);

    private sealed class AuthorizationContext : IAsyncDisposable
    {
        private readonly LmsDbContext resources;
        private readonly LmsDbContext processing;
        public ComponentAuthorization Authorization { get; }

        private AuthorizationContext(LmsDbContext resources, LmsDbContext processing,
            IAuthorityClock clock, TimeProvider time)
        {
            this.resources = resources;
            this.processing = processing;
            Authorization = new(new PostgresScopedAuthorityReader(resources, time),
                new PostgresCurrentProcessingGate(processing, clock), clock);
        }

        public static async Task<AuthorizationContext> Create(IDbContextFactory<LmsDbContext> factory,
            IAuthorityClock clock, TimeProvider time, CancellationToken ct)
            => new(await factory.CreateDbContextAsync(ct), await factory.CreateDbContextAsync(ct), clock, time);

        public async ValueTask DisposeAsync()
        {
            await resources.DisposeAsync();
            await processing.DisposeAsync();
        }
    }

    public async Task<TeacherProgressResponseDto> TeacherProgress(TokenAuthority token,
        ProgressHistoryQuery query, CancellationToken ct = default)
    {
        Validate(query);
        Require(token, ScopeRole.Teacher);
        var status = ParseStatus(query.Status);
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using var authorization = await AuthorizationContext.Create(contexts, clock, time, ct);
        var pairs = await TeacherPairs(db, authorization.Authorization, token, query.CourseId, query.GroupId, ct);

        var roster = new Dictionary<(string Course, string? Group, Guid Student), RosterRow>();
        var assignments = new List<AssignmentRow>();
        var seenAssignments = new HashSet<Guid>();
        foreach (var pair in pairs)
        {
            var groupTitle = pair.GroupId == null ? null : await db.Set<CourseGroupCatalogRecord>().AsNoTracking()
                .Where(x => x.CourseId == pair.CourseId && x.GroupId == pair.GroupId)
                .Select(x => x.Name).SingleOrDefaultAsync(ct);
            var courseTitle = await CourseTitle(db, pair.CourseId, ct);
            var rosterIds = await (from enrollment in db.Set<EnrollmentScope>().AsNoTracking()
                join membership in db.Set<OrganizationMembership>().AsNoTracking()
                    on new { enrollment.OrganizationId, ActorId = enrollment.StudentId }
                    equals new { membership.OrganizationId, membership.ActorId }
                where enrollment.OrganizationId == pair.OrganizationId &&
                    enrollment.CourseId == pair.CourseId && enrollment.GroupId == pair.GroupId &&
                    enrollment.State == "Active" && enrollment.RevocationRequestedAt == null &&
                    enrollment.AccessValidUntil == null && membership.State == "Active" &&
                    membership.RevocationRequestedAt == null && membership.AccessValidUntil == null &&
                    membership.Generation == enrollment.MembershipGeneration &&
                    (query.StudentId == null || enrollment.StudentId == query.StudentId)
                select enrollment.StudentId).Distinct().ToArrayAsync(ct);
            var profiles = await db.Set<OrganizationParticipantProfile>().AsNoTracking()
                .Where(x => x.OrganizationId == pair.OrganizationId && rosterIds.Contains(x.ActorId))
                .ToDictionaryAsync(x => x.ActorId, x => x.DisplayName, ct);
            foreach (var studentId in rosterIds)
                roster[(pair.CourseId, pair.GroupId, studentId)] = new(studentId,
                    profiles.GetValueOrDefault(studentId), pair.CourseId, courseTitle,
                    pair.GroupId, groupTitle);

            var candidates = await (from scope in db.Set<AssignmentScope>().AsNoTracking()
                join assignment in db.Assignments.AsNoTracking() on scope.AssignmentId equals assignment.Id
                where scope.OrganizationId == pair.OrganizationId && scope.CourseId == pair.CourseId &&
                    scope.GroupId == pair.GroupId && assignment.Status == AssignmentStatus.Active &&
                    (query.StudentId == null || scope.StudentId == query.StudentId)
                select scope.AssignmentId).ToArrayAsync(ct);
            foreach (var assignmentId in candidates)
            {
                if (!seenAssignments.Add(assignmentId) ||
                    !await authorization.Authorization.Allows(token,
                        new(ResourceKind.Assignment, assignmentId), ScopeAction.Read, ct)) continue;
                var assignment = await db.Assignments.AsNoTracking().SingleAsync(x => x.Id == assignmentId, ct);
                var scope = await db.Set<AssignmentScope>().AsNoTracking().SingleAsync(x => x.AssignmentId == assignmentId, ct);
                var work = await db.Works.AsNoTracking().AsSplitQuery()
                    .Include(x => x.Submissions).Include(x => x.Reviews)
                    .SingleOrDefaultAsync(x => x.ProblemAssignmentId == assignmentId, ct);
                var projection = await Project(db, assignment, work, ct);
                assignments.Add(new(assignment, scope, work, projection));
                var key = (scope.CourseId, scope.GroupId, scope.StudentId);
                if (!roster.ContainsKey(key))
                {
                    var name = await db.Set<OrganizationParticipantProfile>().AsNoTracking()
                        .Where(x => x.OrganizationId == scope.OrganizationId && x.ActorId == scope.StudentId)
                        .Select(x => x.DisplayName).SingleOrDefaultAsync(ct);
                    roster[key] = new(scope.StudentId, name, scope.CourseId, courseTitle,
                        scope.GroupId, groupTitle);
                }
            }
        }

        var baseAssignments = assignments.Select(x => x.Projection).ToArray();
        var rows = roster.Values
            .Select(person =>
            {
                var all = assignments.Where(x => x.Scope.StudentId == person.StudentId &&
                    x.Scope.CourseId == person.CourseId && x.Scope.GroupId == person.GroupId)
                    .Select(x => x.Projection).OrderBy(x => x.LessonId).ThenBy(x => x.ProblemId)
                    .ThenBy(x => x.AssignmentId).ToArray();
                var visible = status == null ? all : all.Where(x => x.Status == status).ToArray();
                return new StudentProgressRowDto(person.StudentId, person.DisplayName, person.CourseId,
                    person.CourseTitle, person.GroupId, person.GroupTitle, all.Length, visible);
            })
            .Where(x => status == null || x.Assignments.Count > 0)
            .OrderBy(x => x.StudentDisplayName).ThenBy(x => x.StudentId).ThenBy(x => x.CourseId)
            .ThenBy(x => x.GroupId).ToArray();
        var total = rows.Length;
        var page = rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray();
        return new(page, query.Page, query.PageSize, total,
            Summary(baseAssignments, roster.Values.Select(x => x.StudentId).Distinct().Count()),
            assignments.Select(x => x.Assignment.Id).ToArray());
    }

    public async Task<StudentProgressResponseDto> StudentProgress(TokenAuthority token,
        ProgressHistoryQuery query, CancellationToken ct = default)
    {
        Validate(query);
        Require(token, ScopeRole.Student);
        if (query.GroupId != null || query.StudentId is { } student && student != token.Identity.ActorId)
            throw new UseCaseException("Forbidden");
        var status = ParseStatus(query.Status);
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using var authorization = await AuthorizationContext.Create(contexts, clock, time, ct);
        var courses = token.Authority.Grants.Where(x => x.Kind == AuthorityKind.Enrollment)
            .Select(x => x.CourseId).Distinct().Where(x => query.CourseId == null || x == query.CourseId)
            .Order().ToArray();
        if (query.CourseId != null && !courses.Contains(query.CourseId))
            throw new UseCaseException("Forbidden");
        var result = new List<ProgressAssignmentDto>();
        foreach (var courseId in courses)
        {
            if (!await authorization.Authorization.Allows(token,
                new(ResourceKind.Course, CourseId: courseId), ScopeAction.Read, ct)) continue;
            var candidates = await (from scope in db.Set<AssignmentScope>().AsNoTracking()
                join assignment in db.Assignments.AsNoTracking() on scope.AssignmentId equals assignment.Id
                where scope.StudentId == token.Identity.ActorId && scope.CourseId == courseId &&
                    (assignment.Status == AssignmentStatus.Active ||
                     db.Works.Any(work => work.ProblemAssignmentId == assignment.Id))
                select scope.AssignmentId).ToArrayAsync(ct);
            foreach (var assignmentId in candidates)
            {
                if (!await authorization.Authorization.Allows(token,
                    new(ResourceKind.Assignment, assignmentId), ScopeAction.Read, ct)) continue;
                var assignment = await db.Assignments.AsNoTracking().SingleAsync(x => x.Id == assignmentId, ct);
                var work = await db.Works.AsNoTracking().AsSplitQuery()
                    .Include(x => x.Submissions).Include(x => x.Reviews)
                    .SingleOrDefaultAsync(x => x.ProblemAssignmentId == assignmentId, ct);
                result.Add(await Project(db, assignment, work, ct));
            }
        }
        var active = result.Where(x => x.IsActive).ToArray();
        var filtered = (status == null ? result : result.Where(x => x.Status == status))
            .OrderBy(x => x.CourseTitle).ThenBy(x => x.LessonId).ThenBy(x => x.ProblemId)
            .ThenBy(x => x.AssignmentId).ToArray();
        var total = filtered.Length;
        return new(filtered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray(),
            query.Page, query.PageSize, total, Summary(active, active.Length == 0 ? 0 : 1),
            result.Select(x => x.AssignmentId).ToArray());
    }

    public async Task<ReviewHistoryResponseDto> TeacherReviewHistory(TokenAuthority token,
        ReviewHistoryQuery query, CancellationToken ct = default)
    {
        Validate(query);
        Require(token, ScopeRole.Teacher);
        var decision = ParseDecision(query.Decision);
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using var authorization = await AuthorizationContext.Create(contexts, clock, time, ct);
        var pairs = await TeacherPairs(db, authorization.Authorization, token, query.CourseId, query.GroupId, ct);
        var reviewIds = new HashSet<Guid>();
        foreach (var pair in pairs)
        {
            var candidates = await (from scope in db.Set<AssignmentScope>().AsNoTracking()
                join work in db.Works.AsNoTracking() on scope.AssignmentId equals work.ProblemAssignmentId
                join review in db.Set<Review>().AsNoTracking() on work.Id equals review.ProblemWorkId
                where scope.OrganizationId == pair.OrganizationId && scope.CourseId == pair.CourseId &&
                    scope.GroupId == pair.GroupId && review.TeacherId == token.Identity.ActorId &&
                    (query.StudentId == null || scope.StudentId == query.StudentId)
                select review.Id).ToArrayAsync(ct);
            foreach (var reviewId in candidates)
                if (await authorization.Authorization.Allows(token,
                    new(ResourceKind.Review, reviewId), ScopeAction.Read, ct)) reviewIds.Add(reviewId);
        }

        var projections = new List<ReviewHistoryItemDto>();
        foreach (var reviewId in reviewIds)
        {
            var review = await db.Set<Review>().AsNoTracking().SingleAsync(x => x.Id == reviewId, ct);
            var work = await db.Works.AsNoTracking().SingleAsync(x => x.Id == review.ProblemWorkId, ct);
            var assignment = await db.Assignments.AsNoTracking().SingleAsync(x => x.Id == work.ProblemAssignmentId, ct);
            var scope = await db.Set<AssignmentScope>().AsNoTracking().SingleAsync(x => x.AssignmentId == assignment.Id, ct);
            var submission = await db.Set<Submission>().AsNoTracking().SingleAsync(x => x.Id == review.SubmissionId, ct);
            var content = await new PostgresContentResolver(db).Resolve(assignment.CourseId, assignment.Content, ct);
            var name = await db.Set<OrganizationParticipantProfile>().AsNoTracking()
                .Where(x => x.OrganizationId == scope.OrganizationId && x.ActorId == scope.StudentId)
                .Select(x => x.DisplayName).SingleOrDefaultAsync(ct);
            var group = scope.GroupId == null ? null : await db.Set<CourseGroupCatalogRecord>().AsNoTracking()
                .Where(x => x.CourseId == scope.CourseId && x.GroupId == scope.GroupId)
                .Select(x => x.Name).SingleOrDefaultAsync(ct);
            projections.Add(new(review.Id, work.Id, submission.Id, scope.StudentId, name,
                assignment.CourseId, content.Course.Title, assignment.Content.LessonId,
                content.Lesson.Title, scope.GroupId, group, assignment.Content.ProblemId,
                content.Problem.Title, assignment.Content.CourseVersionId, submission.AttemptNumber,
                submission.AttachmentIds.Count, review.Decision, work.Status, review.CreatedAt));
        }
        var summary = new ReviewHistorySummaryDto(projections.Count,
            projections.Count(x => x.Decision == ReviewDecision.NeedsRevision),
            projections.Count(x => x.Decision == ReviewDecision.Accepted));
        var filtered = (decision == null ? projections : projections.Where(x => x.Decision == decision))
            .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.ReviewId).ToArray();
        var total = filtered.Length;
        return new(filtered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray(),
            query.Page, query.PageSize, total, summary, reviewIds.ToArray());
    }

    public Task<bool> CanReturn(TokenAuthority token, TeacherProgressResponseDto response,
        CancellationToken ct = default) => AuthorizeResponse(token,
            (response.AuthorizationProof ?? response.Items.SelectMany(x => x.Assignments)
                .Select(x => x.AssignmentId)).Select(x =>
                new ResourceReference(ResourceKind.Assignment, x))
            .Concat(response.Items.Select(x => x.GroupId == null
                ? new ResourceReference(ResourceKind.Course, CourseId: x.CourseId)
                : new ResourceReference(ResourceKind.Group, CourseId: x.CourseId, GroupId: x.GroupId))), ct);

    public Task<bool> CanReturn(TokenAuthority token, StudentProgressResponseDto response,
        CancellationToken ct = default) => AuthorizeResponse(token,
            (response.AuthorizationProof ?? response.Items.Select(x => x.AssignmentId))
                .Select(x => new ResourceReference(ResourceKind.Assignment, x)), ct);

    public Task<bool> CanReturn(TokenAuthority token, ReviewHistoryResponseDto response,
        CancellationToken ct = default) => AuthorizeResponse(token,
            (response.AuthorizationProof ?? response.Items.Select(x => x.ReviewId))
                .Select(x => new ResourceReference(ResourceKind.Review, x)), ct);

    private async Task<bool> AuthorizeResponse(TokenAuthority token,
        IEnumerable<ResourceReference> resources, CancellationToken ct)
    {
        await using var authorization = await AuthorizationContext.Create(contexts, clock, time, ct);
        foreach (var resource in resources.Distinct())
            if (!await authorization.Authorization.Allows(token, resource, ScopeAction.Read, ct)) return false;
        return true;
    }

    private async Task<IReadOnlyList<ScopePair>> TeacherPairs(LmsDbContext db,
        ComponentAuthorization authorization, TokenAuthority token, string? courseFilter,
        string? groupFilter, CancellationToken ct)
    {
        var grants = token.Authority.Grants.Where(x => x.Kind == AuthorityKind.Teacher &&
            (courseFilter == null || x.CourseId == courseFilter)).ToArray();
        if (courseFilter != null && grants.Length == 0) throw new UseCaseException("Forbidden");
        var pairs = new HashSet<ScopePair>();
        foreach (var grant in grants)
        {
            if (grant.GroupId != null)
            {
                if (groupFilter != null && grant.GroupId != groupFilter) continue;
                var resource = new ResourceReference(ResourceKind.Group,
                    CourseId: grant.CourseId, GroupId: grant.GroupId);
                if (await authorization.Allows(token, resource, ScopeAction.Read, ct))
                    pairs.Add(new(token.Authority.Membership.OrganizationId, grant.CourseId, grant.GroupId));
                continue;
            }
            if (groupFilter != null)
            {
                var resource = new ResourceReference(ResourceKind.Group,
                    CourseId: grant.CourseId, GroupId: groupFilter);
                if (await authorization.Allows(token, resource, ScopeAction.Read, ct))
                    pairs.Add(new(token.Authority.Membership.OrganizationId, grant.CourseId, groupFilter));
                continue;
            }
            if (!await authorization.Allows(token,
                new(ResourceKind.Course, CourseId: grant.CourseId), ScopeAction.Read, ct)) continue;
            var groups = await db.Set<CourseGroupCatalogRecord>().AsNoTracking()
                .Where(x => x.CourseId == grant.CourseId).Select(x => x.GroupId).ToArrayAsync(ct);
            if (groups.Length == 0)
                pairs.Add(new(token.Authority.Membership.OrganizationId, grant.CourseId, null));
            foreach (var group in groups)
                if (await authorization.Allows(token, new(ResourceKind.Group,
                    CourseId: grant.CourseId, GroupId: group), ScopeAction.Read, ct))
                    pairs.Add(new(token.Authority.Membership.OrganizationId, grant.CourseId, group));
        }
        if ((courseFilter != null || groupFilter != null) && pairs.Count == 0)
            throw new UseCaseException("Forbidden");
        return pairs.OrderBy(x => x.CourseId).ThenBy(x => x.GroupId).ToArray();
    }

    private static async Task<ProgressAssignmentDto> Project(LmsDbContext db,
        ProblemAssignment assignment, ProblemWork? work, CancellationToken ct)
    {
        var content = await new PostgresContentResolver(db).Resolve(assignment.CourseId, assignment.Content, ct);
        var activity = work?.Submissions.Select(x => (DateTimeOffset?)x.SubmittedAt)
            .Concat(work.Reviews.Select(x => (DateTimeOffset?)x.CreatedAt)).Max();
        return new(assignment.Id, work?.Id, assignment.Content.CourseVersionId, assignment.CourseId,
            content.Course.Title, assignment.Content.LessonId, content.Lesson.Title,
            assignment.Content.ProblemId, content.Problem.Title,
            work?.Status ?? ProblemWorkStatus.NotStarted, work?.AttemptCount ?? 0,
            assignment.Status == AssignmentStatus.Active, activity);
    }

    private static async Task<string?> CourseTitle(LmsDbContext db, string courseId, CancellationToken ct)
    {
        var catalog = await db.Set<CourseCatalogRecord>().AsNoTracking().Where(x => x.CourseId == courseId)
            .Select(x => x.Title).SingleOrDefaultAsync(ct);
        if (catalog != null) return catalog;
        var json = await db.ContentVersions.AsNoTracking().Where(x => x.CourseId == courseId)
            .OrderByDescending(x => x.VersionNumber).Select(x => x.ContentJson).FirstOrDefaultAsync(ct);
        return json == null ? null : JsonSerializer.Deserialize<PublishedCourseContent>(
            json, PostgresContentResolver.Json)?.Title;
    }

    private static ProgressSummaryDto Summary(IEnumerable<ProgressAssignmentDto> source, int students)
    {
        var rows = source.ToArray();
        return new(students, rows.Length,
            rows.Count(x => x.Status == ProblemWorkStatus.NotStarted),
            rows.Count(x => x.Status == ProblemWorkStatus.Submitted),
            rows.Count(x => x.Status == ProblemWorkStatus.NeedsRevision),
            rows.Count(x => x.Status == ProblemWorkStatus.Accepted));
    }

    private static void Require(TokenAuthority token, ScopeRole role)
    {
        if (token.Identity.ActorId != token.Authority.Membership.ActorId ||
            token.Authority.Membership.Role != role) throw new UseCaseException("Forbidden");
    }

    private static void Validate(ProgressHistoryQuery query)
    {
        Validate(query.CourseId, query.GroupId, query.Page, query.PageSize);
        if (query.StudentId == Guid.Empty) throw new UseCaseException("ValidationFailed");
    }

    private static void Validate(ReviewHistoryQuery query)
    {
        Validate(query.CourseId, query.GroupId, query.Page, query.PageSize);
        if (query.StudentId == Guid.Empty) throw new UseCaseException("ValidationFailed");
    }

    private static void Validate(string? courseId, string? groupId, int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 200 || (long)(page - 1) * pageSize > int.MaxValue ||
            courseId?.Length > 160 || groupId?.Length > 160)
            throw new UseCaseException("ValidationFailed");
    }

    private static ProblemWorkStatus? ParseStatus(string? value)
    {
        if (value == null) return null;
        if (!Enum.TryParse<ProblemWorkStatus>(value, false, out var result) || !Enum.IsDefined(result))
            throw new UseCaseException("ValidationFailed");
        return result;
    }

    private static ReviewDecision? ParseDecision(string? value)
    {
        if (value == null) return null;
        if (!Enum.TryParse<ReviewDecision>(value, false, out var result) || !Enum.IsDefined(result))
            throw new UseCaseException("ValidationFailed");
        return result;
    }
}
