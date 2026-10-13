using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Lms.Infrastructure;

public sealed class PostgresCourseRosterAdministration(LmsDbContext db, TimeProvider clock)
    : ICourseRosterAdministration
{
    public async Task<CourseCreated> CreateCourse(CreateCourseCommand command, CancellationToken ct = default)
    {
        var title = Required(command.Title, 2, 160);
        var description = Optional(command.Description, 500);
        var key = Required(command.IdempotencyKey, 1, 128);
        var digest = Digest(title + "\n" + description);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await Advisory($"m1-05:{command.OrganizationId}:{command.AdministratorActorId}:{key}", ct);
        var admin = await Admin(command.OrganizationId, command.AdministratorActorId, true, ct);
        var prior = await Receipt(command.AdministratorActorId, key, "CreateCourse", digest, ct);
        if (prior != null)
        {
            var saved = JsonSerializer.Deserialize<CourseCreated>(prior.ResultRef)! with { Created = false };
            await tx.CommitAsync(ct); return saved;
        }

        var courseId = "course-" + Guid.NewGuid().ToString("N");
        var now = clock.GetUtcNow();
        var result = new CourseCreated(courseId, title, description, "Draft", 1, true);
        db.AddRange(
            new CourseRegistration { CourseId = courseId, OrganizationId = command.OrganizationId, MappingState = "Verified" },
            new CourseCatalogRecord { CourseId = courseId, OrganizationId = command.OrganizationId,
                Title = title, Description = description, State = "Draft", Version = 1, CreatedAt = now },
            CommandReceipt(command.AdministratorActorId, key, command.OrganizationId, "CreateCourse", digest, result, now),
            Audit(command.OrganizationId, command.AdministratorActorId, "course_created", courseId, courseId,
                admin.Version, command.TraceId, now));
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (Exception error) when (IsConcurrency(error)) { throw new UseCaseException("ConcurrencyConflict"); }
        ct.ThrowIfCancellationRequested();
        return result;
    }

    public async Task<GroupCreated> CreateGroup(CreateGroupCommand command, CancellationToken ct = default)
    {
        var courseId = Required(command.CourseId, 1, 80);
        var name = Required(command.Name, 2, 120);
        var description = Optional(command.Description, 300);
        var key = Required(command.IdempotencyKey, 1, 128);
        var digest = Digest(courseId + "\n" + name + "\n" + description);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await Advisory($"m1-05:{command.OrganizationId}:{command.AdministratorActorId}:{key}", ct);
        var admin = await Admin(command.OrganizationId, command.AdministratorActorId, true, ct);
        var prior = await Receipt(command.AdministratorActorId, key, "CreateGroup", digest, ct);
        if (prior != null)
        {
            var saved = JsonSerializer.Deserialize<GroupCreated>(prior.ResultRef)! with { Created = false };
            await tx.CommitAsync(ct); return saved;
        }
        if (!await db.Set<CourseCatalogRecord>().AnyAsync(x => x.CourseId == courseId &&
            x.OrganizationId == command.OrganizationId, ct)) throw new UseCaseException("NotFound");
        if (await db.Set<CourseGroupCatalogRecord>().AnyAsync(x => x.CourseId == courseId && x.Name == name, ct))
            throw new UseCaseException("IdempotencyConflict");

        var groupId = "group-" + Guid.NewGuid().ToString("N");
        var now = clock.GetUtcNow();
        var result = new GroupCreated(courseId, groupId, name, description, 1, true);
        db.AddRange(
            new CourseGroup { CourseId = courseId, GroupId = groupId },
            new CourseGroupCatalogRecord { CourseId = courseId, GroupId = groupId, Name = name,
                Description = description, Version = 1, CreatedAt = now },
            CommandReceipt(command.AdministratorActorId, key, command.OrganizationId, "CreateGroup", digest, result, now),
            Audit(command.OrganizationId, command.AdministratorActorId, "course_group_created", groupId, courseId,
                admin.Version, command.TraceId, now));
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (Exception error) when (IsConcurrency(error)) { throw new UseCaseException("ConcurrencyConflict"); }
        ct.ThrowIfCancellationRequested();
        return result;
    }

    public async Task<IReadOnlyList<CourseCatalogItem>> AdminCatalog(Guid organizationId, Guid administratorActorId,
        CancellationToken ct = default)
    {
        await Admin(organizationId, administratorActorId, false, ct);
        return await db.Set<CourseCatalogRecord>().AsNoTracking().Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.Title).Select(x => new CourseCatalogItem(x.CourseId, x.Title, x.Description, x.State,
                x.Version,
                db.Set<CourseGroupCatalogRecord>().Count(g => g.CourseId == x.CourseId),
                db.Set<EnrollmentScope>().Count(e => e.OrganizationId == organizationId && e.CourseId == x.CourseId && e.State == "Active"),
                db.Set<TeacherAssignmentScope>().Count(t => t.OrganizationId == organizationId && t.CourseId == x.CourseId && t.State == "Active")))
            .ToListAsync(ct);
    }

    public async Task<CourseRosterDetail?> AdminCourse(Guid organizationId, Guid administratorActorId, string courseId,
        CancellationToken ct = default)
    {
        await Admin(organizationId, administratorActorId, false, ct);
        var course = await db.Set<CourseCatalogRecord>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.CourseId == courseId, ct);
        return course == null ? null : await Detail(course, null, includeEditors: true, canEditContent: false, ct);
    }

    public Task<RosterMutationResult> Enroll(EnrollmentBatchCommand command, CancellationToken ct = default)
        => Mutation(command.OrganizationId, command.AdministratorActorId, command.IdempotencyKey, "EnrollStudents",
            command.CourseId + "\n" + command.GroupId + "\n" + string.Join(',', command.StudentIds.Order()), command.TraceId,
            async token =>
            {
                var ids = command.StudentIds.Where(x => x != Guid.Empty).Distinct().Order().ToArray();
                if (ids.Length is < 1 or > 100) throw new UseCaseException("ValidationFailed");
                await Group(command.OrganizationId, command.CourseId, command.GroupId, token);
                var memberships = await db.Set<OrganizationMembership>().Where(x => x.OrganizationId == command.OrganizationId &&
                    ids.Contains(x.ActorId)).OrderBy(x => x.ActorId).ToListAsync(token);
                if (memberships.Count != ids.Length || memberships.Any(x => x.State != "Active" || x.EducationalRole != "Student"))
                    throw new UseCaseException("Forbidden");
                var changed = 0; long? version = null; long? generation = null;
                foreach (var membership in memberships)
                {
                    var existing = await db.Set<EnrollmentScope>().SingleOrDefaultAsync(x =>
                        x.StudentId == membership.ActorId && x.CourseId == command.CourseId, token);
                    if (existing is { State: "Active" } && existing.GroupId == command.GroupId) continue;
                    if (existing is { State: "Active" }) throw new UseCaseException("ConcurrencyConflict");
                    var scope = await Scope(new(AuthorityKind.Enrollment, AuthorityOperation.Grant, command.OrganizationId,
                        membership.ActorId, existing?.Version ?? 0, membership.Generation, command.CourseId, command.GroupId), token);
                    changed++; version = scope.Version; generation = scope.Generation;
                }
                return new RosterMutationResult(changed > 0, changed, version, generation);
            }, ct);

    public Task<RosterMutationResult> Move(MoveStudentCommand command, CancellationToken ct = default)
        => Mutation(command.OrganizationId, command.AdministratorActorId, command.IdempotencyKey, "MoveStudent",
            $"{command.CourseId}\n{command.StudentId}\n{command.TargetGroupId}\n{command.ExpectedVersion}", command.TraceId,
            async token =>
            {
                await Group(command.OrganizationId, command.CourseId, command.TargetGroupId, token);
                var membership = await Participant(command.OrganizationId, command.StudentId, "Student", token);
                var current = await db.Set<EnrollmentScope>().SingleOrDefaultAsync(x => x.OrganizationId == command.OrganizationId &&
                    x.CourseId == command.CourseId && x.StudentId == command.StudentId, token) ?? throw new UseCaseException("NotFound");
                if (current.State != "Active") throw new UseCaseException("Forbidden");
                if (current.Version != command.ExpectedVersion) throw new UseCaseException("ConcurrencyConflict");
                if (current.GroupId == command.TargetGroupId) return new(false, 0, current.Version, current.EnrollmentGeneration);
                var result = await Scope(new(AuthorityKind.Enrollment, AuthorityOperation.Move, command.OrganizationId,
                    command.StudentId, command.ExpectedVersion, membership.Generation, command.CourseId, command.TargetGroupId), token);
                return new(true, 1, result.Version, result.Generation);
            }, ct);

    public Task<RosterMutationResult> Remove(RemoveStudentCommand command, CancellationToken ct = default)
        => Mutation(command.OrganizationId, command.AdministratorActorId, command.IdempotencyKey, "RemoveStudent",
            $"{command.CourseId}\n{command.StudentId}\n{command.ExpectedVersion}", command.TraceId,
            async token =>
            {
                var current = await db.Set<EnrollmentScope>().SingleOrDefaultAsync(x => x.OrganizationId == command.OrganizationId &&
                    x.CourseId == command.CourseId && x.StudentId == command.StudentId, token) ?? throw new UseCaseException("NotFound");
                if (current.Version != command.ExpectedVersion) throw new UseCaseException("ConcurrencyConflict");
                if (current.State == "Revoked") return new(false, 0, current.Version, current.EnrollmentGeneration);
                var result = await Scope(new(AuthorityKind.Enrollment, AuthorityOperation.Revoke, command.OrganizationId,
                    command.StudentId, command.ExpectedVersion, current.MembershipGeneration, command.CourseId, current.GroupId), token);
                return new(true, 1, result.Version, result.Generation);
            }, ct);

    public Task<RosterMutationResult> Assign(AssignTeacherCommand command, CancellationToken ct = default)
        => Mutation(command.OrganizationId, command.AdministratorActorId, command.IdempotencyKey, "AssignTeacher",
            $"{command.CourseId}\n{command.GroupId}\n{command.TeacherId}", command.TraceId,
            async token =>
            {
                await Group(command.OrganizationId, command.CourseId, command.GroupId, token);
                var membership = await Participant(command.OrganizationId, command.TeacherId, "Teacher", token);
                var active = await db.Set<TeacherAssignmentScope>().SingleOrDefaultAsync(x => x.OrganizationId == command.OrganizationId &&
                    x.CourseId == command.CourseId && x.GroupId == command.GroupId && x.TeacherId == command.TeacherId && x.State == "Active", token);
                if (active != null) return new(false, 0, active.Version, active.Generation, active.GrantId);
                var previous = await db.Set<TeacherAssignmentScope>().Where(x => x.OrganizationId == command.OrganizationId &&
                    x.CourseId == command.CourseId && x.GroupId == command.GroupId && x.TeacherId == command.TeacherId)
                    .OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
                var grant = previous?.GrantId ?? Guid.NewGuid();
                var result = await Scope(new(AuthorityKind.Teacher, AuthorityOperation.Grant, command.OrganizationId,
                    command.TeacherId, previous?.Version ?? 0, membership.Generation, command.CourseId, command.GroupId, grant), token);
                return new(true, 1, result.Version, result.Generation, result.GrantId);
            }, ct);

    public Task<RosterMutationResult> Unassign(RemoveTeacherCommand command, CancellationToken ct = default)
        => Mutation(command.OrganizationId, command.AdministratorActorId, command.IdempotencyKey, "RemoveTeacher",
            $"{command.CourseId}\n{command.GroupId}\n{command.TeacherId}\n{command.GrantId}\n{command.ExpectedVersion}", command.TraceId,
            async token =>
            {
                var current = await db.Set<TeacherAssignmentScope>().SingleOrDefaultAsync(x => x.GrantId == command.GrantId &&
                    x.OrganizationId == command.OrganizationId && x.CourseId == command.CourseId &&
                    x.GroupId == command.GroupId && x.TeacherId == command.TeacherId, token) ?? throw new UseCaseException("NotFound");
                if (current.Version != command.ExpectedVersion) throw new UseCaseException("ConcurrencyConflict");
                if (current.State == "Revoked") return new(false, 0, current.Version, current.Generation, current.GrantId);
                var result = await Scope(new(AuthorityKind.Teacher, AuthorityOperation.Revoke, command.OrganizationId,
                    command.TeacherId, command.ExpectedVersion, current.MembershipGeneration, command.CourseId,
                    command.GroupId, command.GrantId), token);
                return new(true, 1, result.Version, result.Generation, result.GrantId);
            }, ct);

    public Task<RosterMutationResult> AssignEditor(AssignCourseEditorCommand command, CancellationToken ct = default)
        => Mutation(command.OrganizationId, command.AdministratorActorId, command.IdempotencyKey, "AssignCourseEditor",
            $"{command.CourseId}\n{command.EditorId}", command.TraceId, async token =>
            {
                await Course(command.OrganizationId, command.CourseId, token);
                var membership = await Participant(command.OrganizationId, command.EditorId, "Teacher", token);
                var current = await db.Set<CourseEditorScope>().FromSqlInterpolated(
                    $"SELECT * FROM \"CourseEditorScopes\" WHERE \"OrganizationId\"={command.OrganizationId} AND \"CourseId\"={command.CourseId} AND \"EditorId\"={command.EditorId} FOR UPDATE")
                    .SingleOrDefaultAsync(token);
                if (current is { State: "Active" } && current.MembershipGeneration == membership.Generation)
                    return new(false, 0, current.Version, current.Generation, current.GrantId);
                if (current == null)
                {
                    current = new CourseEditorScope { GrantId = Guid.NewGuid(), EditorId = command.EditorId,
                        CourseId = command.CourseId, OrganizationId = command.OrganizationId,
                        MembershipGeneration = membership.Generation, Generation = 1, State = "Active", Version = 1 };
                    db.Add(current);
                }
                else
                {
                    current.MembershipGeneration = membership.Generation; current.Generation++; current.Version++;
                    current.State = "Active"; current.RevocationRequestedAt = null; current.AccessValidUntil = null;
                }
                return new(true, 1, current.Version, current.Generation, current.GrantId);
            }, ct);

    public Task<RosterMutationResult> UnassignEditor(RemoveCourseEditorCommand command, CancellationToken ct = default)
        => Mutation(command.OrganizationId, command.AdministratorActorId, command.IdempotencyKey, "RemoveCourseEditor",
            $"{command.CourseId}\n{command.EditorId}\n{command.GrantId}\n{command.ExpectedVersion}", command.TraceId,
            async token =>
            {
                await Course(command.OrganizationId, command.CourseId, token);
                var current = await db.Set<CourseEditorScope>().FromSqlInterpolated(
                    $"SELECT * FROM \"CourseEditorScopes\" WHERE \"GrantId\"={command.GrantId} AND \"OrganizationId\"={command.OrganizationId} AND \"CourseId\"={command.CourseId} AND \"EditorId\"={command.EditorId} FOR UPDATE")
                    .SingleOrDefaultAsync(token) ?? throw new UseCaseException("NotFound");
                if (current.Version != command.ExpectedVersion) throw new UseCaseException("ConcurrencyConflict");
                if (current.State == "Revoked") return new(false, 0, current.Version, current.Generation, current.GrantId);
                var now = clock.GetUtcNow(); current.State = "Revoked"; current.Version++;
                current.RevocationRequestedAt = now; current.AccessValidUntil = now;
                return new(true, 1, current.Version, current.Generation, current.GrantId);
            }, ct);

    public async Task<IReadOnlyList<LearningCourseItem>> LearningCatalog(TokenAuthority token, CancellationToken ct = default)
    {
        var membership = token.Authority.Membership;
        if (membership.Role is null || membership.OrganizationId == Guid.Empty) throw new UseCaseException("Forbidden");
        var current = token.Authority.Grants.Where(x => x.MembershipGeneration == membership.Generation &&
            (membership.Role == ScopeRole.Student ? x.Kind == AuthorityKind.Enrollment :
                x.Kind is AuthorityKind.Teacher or AuthorityKind.CourseEditor));
        var grants = current.GroupBy(x => x.CourseId).ToDictionary(x => x.Key, x =>
        {
            var teaching = x.Where(g => g.Kind == AuthorityKind.Teacher).ToArray();
            return (WholeCourse: teaching.Any(g => g.GroupId == null),
                Groups: teaching.Select(g => g.GroupId).Where(g => g != null).Cast<string>().Distinct().ToArray(),
                StudentGroups: x.Where(g => g.Kind == AuthorityKind.Enrollment).Select(g => g.GroupId)
                    .Where(g => g != null).Cast<string>().Distinct().ToArray(),
                CanEditContent: x.Any(g => g.Kind == AuthorityKind.CourseEditor));
        });
        if (grants.Count == 0) return [];
        var ids = grants.Keys.ToArray();
        var courses = await db.Set<CourseCatalogRecord>().AsNoTracking().Where(x =>
            x.OrganizationId == membership.OrganizationId && ids.Contains(x.CourseId))
            .OrderBy(x => x.Title).ToListAsync(ct);
        var groups = await db.Set<CourseGroupCatalogRecord>().AsNoTracking().Where(x => ids.Contains(x.CourseId)).ToListAsync(ct);
        var result = new List<LearningCourseItem>();
        foreach (var course in courses)
        {
            var allowed = grants[course.CourseId];
            var visible = groups.Where(x => x.CourseId == course.CourseId &&
                (allowed.WholeCourse || allowed.Groups.Contains(x.GroupId) || allowed.StudentGroups.Contains(x.GroupId)))
                .OrderBy(x => x.Name).ToList();
            var items = new List<LearningGroupItem>();
            foreach (var group in visible)
            {
                var count = membership.Role == ScopeRole.Teacher
                    ? await db.Set<EnrollmentScope>().CountAsync(x => x.OrganizationId == membership.OrganizationId &&
                        x.CourseId == course.CourseId && x.GroupId == group.GroupId && x.State == "Active", ct) : 0;
                items.Add(new(group.GroupId, group.Name, count));
            }
            result.Add(new(course.CourseId, course.Title, course.Description, course.State,
                membership.Role.Value, items, allowed.CanEditContent));
        }
        return result;
    }

    public async Task<CourseRosterDetail?> TeacherCourse(TokenAuthority token, string courseId, CancellationToken ct = default)
    {
        var membership = token.Authority.Membership;
        if (membership.Role != ScopeRole.Teacher) throw new UseCaseException("Forbidden");
        var grants = token.Authority.Grants.Where(x => x.CourseId == courseId &&
            x.MembershipGeneration == membership.Generation).ToArray();
        var teaching = grants.Where(x => x.Kind == AuthorityKind.Teacher).ToArray();
        var canEdit = grants.Any(x => x.Kind == AuthorityKind.CourseEditor);
        if (teaching.Length == 0 && !canEdit) return null;
        var course = await db.Set<CourseCatalogRecord>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.OrganizationId == membership.OrganizationId && x.CourseId == courseId, ct);
        var groups = teaching.Any(x => x.GroupId == null) ? null :
            teaching.Select(x => x.GroupId!).Where(x => x != null).Distinct().ToArray();
        return course == null ? null : await Detail(course, groups, includeEditors: false, canEditContent: canEdit, ct);
    }

    private async Task<CourseRosterDetail> Detail(CourseCatalogRecord course, string[]? groupFilter,
        bool includeEditors, bool canEditContent, CancellationToken ct)
    {
        var groupRows = db.Set<CourseGroupCatalogRecord>().AsNoTracking().Where(x => x.CourseId == course.CourseId);
        if (groupFilter != null) groupRows = groupRows.Where(x => groupFilter.Contains(x.GroupId));
        var groups = await groupRows.OrderBy(x => x.Name).ToListAsync(ct);
        var groupIds = groups.Select(x => x.GroupId).ToArray();
        var students = await (from scope in db.Set<EnrollmentScope>().AsNoTracking()
            join profile in db.Set<OrganizationParticipantProfile>().AsNoTracking()
                on new { scope.OrganizationId, ActorId = scope.StudentId } equals new { profile.OrganizationId, profile.ActorId }
            join groupRow in db.Set<CourseGroupCatalogRecord>().AsNoTracking()
                on new { scope.CourseId, GroupId = scope.GroupId! } equals new { groupRow.CourseId, groupRow.GroupId }
            where scope.OrganizationId == course.OrganizationId && scope.CourseId == course.CourseId && scope.State == "Active" &&
                scope.GroupId != null && groupIds.Contains(scope.GroupId)
            orderby profile.DisplayName
            select new StudentRosterItem(profile.ActorId, profile.DisplayName, profile.ExternalRosterKey,
                groupRow.GroupId, groupRow.Name, scope.EnrollmentGeneration, scope.Version)).ToListAsync(ct);
        var teachers = await (from scope in db.Set<TeacherAssignmentScope>().AsNoTracking()
            join profile in db.Set<OrganizationParticipantProfile>().AsNoTracking()
                on new { scope.OrganizationId, ActorId = scope.TeacherId } equals new { profile.OrganizationId, profile.ActorId }
            join groupRow in db.Set<CourseGroupCatalogRecord>().AsNoTracking()
                on new { scope.CourseId, GroupId = scope.GroupId! } equals new { groupRow.CourseId, groupRow.GroupId }
            where scope.OrganizationId == course.OrganizationId && scope.CourseId == course.CourseId && scope.State == "Active" &&
                scope.GroupId != null && groupIds.Contains(scope.GroupId)
            orderby profile.DisplayName
            select new TeacherRosterItem(profile.ActorId, profile.DisplayName, profile.ExternalRosterKey,
                scope.GrantId, groupRow.GroupId, groupRow.Name, scope.Generation, scope.Version)).ToListAsync(ct);
        var editors = includeEditors
            ? await (from scope in db.Set<CourseEditorScope>().AsNoTracking()
                join profile in db.Set<OrganizationParticipantProfile>().AsNoTracking()
                    on new { scope.OrganizationId, ActorId = scope.EditorId } equals new { profile.OrganizationId, profile.ActorId }
                where scope.OrganizationId == course.OrganizationId && scope.CourseId == course.CourseId && scope.State == "Active"
                orderby profile.DisplayName
                select new CourseEditorRosterItem(profile.ActorId, profile.DisplayName, profile.ExternalRosterKey,
                    scope.GrantId, scope.Generation, scope.Version)).ToListAsync(ct)
            : [];
        var groupItems = groups.Select(groupRow => new GroupCatalogItem(groupRow.GroupId, groupRow.Name,
            groupRow.Description, groupRow.Version, students.Count(x => x.GroupId == groupRow.GroupId),
            teachers.Count(x => x.GroupId == groupRow.GroupId))).ToList();
        var item = new CourseCatalogItem(course.CourseId, course.Title, course.Description, course.State, course.Version,
            groupItems.Count, students.Count, teachers.Count);
        return new(item, groupItems, students, teachers, editors, canEditContent);
    }

    private async Task<RosterMutationResult> Mutation(Guid organizationId, Guid administratorActorId,
        string idempotencyKey, string kind, string payload, string traceId,
        Func<CancellationToken, Task<RosterMutationResult>> work, CancellationToken ct)
    {
        var key = Required(idempotencyKey, 1, 128); var digest = Digest(payload);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await Advisory($"m1-05:{organizationId}:{administratorActorId}:{key}", ct);
            var admin = await Admin(organizationId, administratorActorId, true, ct);
            var prior = await Receipt(administratorActorId, key, kind, digest, ct);
            if (prior != null)
            {
                var replay = JsonSerializer.Deserialize<RosterMutationResult>(prior.ResultRef)! with { Changed = false };
                await tx.CommitAsync(ct); return replay;
            }
            var result = await work(ct); var now = clock.GetUtcNow();
            db.AddRange(CommandReceipt(administratorActorId, key, organizationId, kind, digest, result, now),
                Audit(organizationId, administratorActorId, "roster_" + kind.ToLowerInvariant(), result.GrantId?.ToString() ?? payload.Split('\n')[0],
                    payload.Split('\n')[0], admin.Version, traceId, now));
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); ct.ThrowIfCancellationRequested();
            return result;
        }
        catch (PostgresException e) when (e.SqlState == "P0001") { throw new UseCaseException(e.MessageText); }
        catch (Exception e) when (IsConcurrency(e)) { throw new UseCaseException("ConcurrencyConflict"); }
    }

    private async Task<OrganizationMembership> Admin(Guid organizationId, Guid actorId, bool forUpdate, CancellationToken ct)
    {
        OrganizationMembership? membership;
        if (forUpdate)
            membership = await db.Set<OrganizationMembership>().FromSqlInterpolated(
                $"SELECT * FROM \"OrganizationMemberships\" WHERE \"OrganizationId\"={organizationId} AND \"ActorId\"={actorId} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
        else membership = await db.Set<OrganizationMembership>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.ActorId == actorId, ct);
        if (membership == null || membership.State != "Active" || !OrganizationPermissionPolicy.CanManageOrganization(
            new(membership.OrganizationId, membership.ActorId, membership.Generation, membership.Version,
                null, membership.PermissionFlags))) throw new UseCaseException("Forbidden");
        return membership;
    }

    private async Task<OrganizationMembership> Participant(Guid organizationId, Guid actorId, string role, CancellationToken ct)
    {
        var membership = await db.Set<OrganizationMembership>().SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.ActorId == actorId, ct);
        if (membership is not { State: "Active" } || membership.EducationalRole != role)
            throw new UseCaseException("Forbidden");
        return membership;
    }

    private async Task Course(Guid organizationId, string courseId, CancellationToken ct)
    {
        var course = await db.Set<CourseCatalogRecord>().FromSqlInterpolated(
            $"SELECT * FROM \"CourseCatalog\" WHERE \"CourseId\"={courseId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (course == null || course.OrganizationId != organizationId) throw new UseCaseException("NotFound");
    }

    private async Task Group(Guid organizationId, string courseId, string groupId, CancellationToken ct)
    {
        if (!await (from groupRow in db.Set<CourseGroupCatalogRecord>()
            join course in db.Set<CourseCatalogRecord>() on groupRow.CourseId equals course.CourseId
            where course.OrganizationId == organizationId && course.CourseId == courseId && groupRow.GroupId == groupId
            select groupRow.GroupId).AnyAsync(ct)) throw new UseCaseException("NotFound");
    }

    private async Task<CourseRosterCommandReceipt?> Receipt(Guid actorId, string key, string kind, string digest, CancellationToken ct)
    {
        var prior = await db.Set<CourseRosterCommandReceipt>().SingleOrDefaultAsync(x =>
            x.AdministratorActorId == actorId && x.IdempotencyKey == key, ct);
        if (prior != null && (prior.Kind != kind || prior.PayloadDigest != digest)) throw new UseCaseException("IdempotencyConflict");
        return prior;
    }

    private async Task<AuthorityChange> Scope(AuthorityCommand command, CancellationToken ct)
    {
        var parameters = new[] {
            P("kind", NpgsqlDbType.Text, command.Kind.ToString()), P("op", NpgsqlDbType.Text, command.Operation.ToString()),
            P("org", NpgsqlDbType.Uuid, command.OrganizationId), P("actor", NpgsqlDbType.Uuid, command.ActorId),
            P("expected", NpgsqlDbType.Bigint, command.ExpectedVersion), P("membergen", NpgsqlDbType.Bigint, command.MembershipGeneration),
            P("course", NpgsqlDbType.Text, command.CourseId), P("grp", NpgsqlDbType.Text, command.GroupId),
            P("grant", NpgsqlDbType.Uuid, command.GrantId), P("role", NpgsqlDbType.Text, command.Role?.ToString()),
            P("permissions", NpgsqlDbType.Integer, command.Permissions)
        };
        var json = await db.Database.SqlQueryRaw<string>(
            "SELECT public.s3_change_authority(@kind,@op,@org,@actor,@expected,@membergen,@course,@grp,@grant,@role,@permissions)::text AS \"Value\"", parameters)
            .SingleAsync(ct);
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        return new(root.GetProperty("generation").GetInt64(), root.GetProperty("version").GetInt64(),
            root.GetProperty("deadline").ValueKind == JsonValueKind.Null ? null : root.GetProperty("deadline").GetDateTimeOffset(),
            root.GetProperty("grant").ValueKind == JsonValueKind.Null ? null : root.GetProperty("grant").GetGuid());
    }

    private Task Advisory(string value, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT pg_advisory_xact_lock(hashtextextended({value}, 0))", ct);

    private static CourseRosterCommandReceipt CommandReceipt<T>(Guid actorId, string key, Guid organizationId,
        string kind, string digest, T result, DateTimeOffset now) => new()
        { AdministratorActorId = actorId, IdempotencyKey = key, OrganizationId = organizationId, Kind = kind,
            PayloadDigest = digest, ResultRef = JsonSerializer.Serialize(result), CreatedAt = now };

    private static SecurityAudit Audit(Guid organizationId, Guid actorId, string action, string subject,
        string resource, long version, string traceId, DateTimeOffset now) => new()
        { AuditId = Guid.NewGuid(), OrganizationId = organizationId, ActorId = actorId, Action = action,
            SubjectRef = subject, ResourceRef = resource, Result = "success", AuthorityVersion = version,
            TraceId = traceId, OccurredAt = now };

    private static NpgsqlParameter P(string name, NpgsqlDbType type, object? value)
        => new(name, type) { Value = value ?? DBNull.Value };
    private static bool IsConcurrency(Exception e) => e is DbUpdateConcurrencyException ||
        e is DbUpdateException { InnerException: PostgresException { SqlState: "23505" or "40001" or "40P01" } } ||
        e is PostgresException { SqlState: "23505" or "40001" or "40P01" };
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Required(string? value, int min, int max)
    {
        var result = value?.Trim();
        if (result == null || result.Length < min || result.Length > max) throw new UseCaseException("ValidationFailed");
        return result;
    }
    private static string Optional(string? value, int max)
    {
        var result = value?.Trim() ?? string.Empty;
        if (result.Length > max) throw new UseCaseException("ValidationFailed");
        return result;
    }
}
