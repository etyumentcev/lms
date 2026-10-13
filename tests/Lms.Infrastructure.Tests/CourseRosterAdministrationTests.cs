using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Tests;

public sealed class CourseRosterAdministrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly OrganizationBootstrapOptions Options = new()
    { ChallengeKey = Enumerable.Range(70, 32).Select(x => (byte)x).ToArray(), KeyVersion = 11 };
    private readonly TestClock clock = new();

    private async Task<(Guid Org, Guid Admin)> Admin(string cs, string suffix)
    {
        await using var db = PostgresFixture.Open(cs);
        var created = await new PostgresOrganizationBootstrap(db, clock, Options).Create(new(Guid.NewGuid(),
            "Синтетическая организация " + suffix, "admin-" + suffix, new string('A', 64), "m1-05"));
        return (created.OrganizationId, created.AdminActorId);
    }

    private async Task<ProvisionAccountResult> Participant(string cs, Guid org, Guid admin, int index, AccountRole role)
    {
        await using var db = PostgresFixture.Open(cs);
        return await new PostgresAccountAdministration(db, clock, Options).Provision(new(org, admin,
            $"{role.ToString().ToLowerInvariant()}-{index:00}", $"Синтетический {role} {index:00}",
            $"m105-{role.ToString().ToLowerInvariant()}-{index:00}-{Guid.NewGuid():N}", role,
            Guid.NewGuid().ToString(), "m1-05"));
    }

    [Fact]
    public async Task Course_groups_roster_role_catalogs_move_remove_and_history_are_scoped()
    {
        var cs = await fixture.CreateDatabase(); var own = await Admin(cs, "own"); var foreign = await Admin(cs, "foreign");
        var students = new List<ProvisionAccountResult>();
        for (var i = 1; i <= 13; i++) students.Add(await Participant(cs, own.Org, own.Admin, i, AccountRole.Student));
        var teacher = await Participant(cs, own.Org, own.Admin, 1, AccountRole.Teacher);
        var unassigned = await Participant(cs, own.Org, own.Admin, 2, AccountRole.Teacher);

        CourseCreated course; GroupCreated first; GroupCreated second;
        await using (var db = PostgresFixture.Open(cs))
        {
            var service = new PostgresCourseRosterAdministration(db, clock);
            var key = Guid.NewGuid().ToString();
            course = await service.CreateCourse(new(own.Org, own.Admin, "Подготовка к олимпиаде Эйлера",
                "Синтетический черновой курс", key, "m1-05"));
            Assert.True(course.Created);
        }
        await using (var replayDb = PostgresFixture.Open(cs))
        {
            var service = new PostgresCourseRosterAdministration(replayDb, clock);
            var same = await service.CreateCourse(new(own.Org, own.Admin, "Второй курс", "", "different", "m1-05"));
            Assert.NotEqual(course.CourseId, same.CourseId);
        }
        await using (var db = PostgresFixture.Open(cs))
        {
            var service = new PostgresCourseRosterAdministration(db, clock);
            first = await service.CreateGroup(new(own.Org, own.Admin, course.CourseId, "Эйлер — основная", "12 учеников", Guid.NewGuid().ToString(), "m1-05"));
            second = await service.CreateGroup(new(own.Org, own.Admin, course.CourseId, "Эйлер — резерв", "Переводы", Guid.NewGuid().ToString(), "m1-05"));
            var enrolled = await service.Enroll(new(own.Org, own.Admin, course.CourseId, first.GroupId,
                students.Take(12).Select(x => x.ActorId).ToArray(), Guid.NewGuid().ToString(), "m1-05"));
            Assert.True(enrolled.Changed); Assert.Equal(12, enrolled.Affected);
            var duplicate = await service.Enroll(new(own.Org, own.Admin, course.CourseId, first.GroupId,
                students.Take(12).Select(x => x.ActorId).ToArray(), Guid.NewGuid().ToString(), "m1-05"));
            Assert.False(duplicate.Changed); Assert.Equal(0, duplicate.Affected);
            var assigned = await service.Assign(new(own.Org, own.Admin, course.CourseId, first.GroupId,
                teacher.ActorId, Guid.NewGuid().ToString(), "m1-05"));
            Assert.True(assigned.Changed); Assert.NotNull(assigned.GrantId);
            var repeated = await service.Assign(new(own.Org, own.Admin, course.CourseId, first.GroupId,
                teacher.ActorId, Guid.NewGuid().ToString(), "m1-05"));
            Assert.False(repeated.Changed); Assert.Equal(assigned.GrantId, repeated.GrantId);
        }

        Guid historicalAssignment; long originalEnrollmentVersion;
        await using (var db = PostgresFixture.Open(cs))
        {
            var enrollment = await db.Set<EnrollmentScope>().SingleAsync(x => x.StudentId == students[0].ActorId && x.CourseId == course.CourseId);
            originalEnrollmentVersion = enrollment.Version;
            historicalAssignment = Guid.NewGuid();
            db.Add(new ProblemAssignment(historicalAssignment, students[0].ActorId, course.CourseId,
                new ProblemContentReference(Guid.NewGuid(), "synthetic-lesson", "synthetic-problem")));
            await db.SaveChangesAsync();
            Assert.Equal(first.GroupId, (await db.Set<AssignmentScope>().SingleAsync(x => x.AssignmentId == historicalAssignment)).GroupId);
            var service = new PostgresCourseRosterAdministration(db, clock);
            var moved = await service.Move(new(own.Org, own.Admin, course.CourseId, students[0].ActorId,
                second.GroupId, originalEnrollmentVersion, Guid.NewGuid().ToString(), "m1-05"));
            Assert.True(moved.Changed);
        }
        await using (var staleDb = PostgresFixture.Open(cs))
        {
            var service = new PostgresCourseRosterAdministration(staleDb, clock);
            var stale = await Assert.ThrowsAsync<UseCaseException>(() => service.Move(new(own.Org, own.Admin,
                course.CourseId, students[0].ActorId, first.GroupId, originalEnrollmentVersion,
                Guid.NewGuid().ToString(), "m1-05")));
            Assert.Equal("ConcurrencyConflict", stale.Message);
        }

        await using (var db = PostgresFixture.Open(cs))
        {
            var immutable = await db.Set<AssignmentScope>().SingleAsync(x => x.AssignmentId == historicalAssignment);
            Assert.Equal(first.GroupId, immutable.GroupId);
            var current = await db.Set<EnrollmentScope>().SingleAsync(x => x.StudentId == students[0].ActorId && x.CourseId == course.CourseId);
            Assert.Equal(second.GroupId, current.GroupId);
            var service = new PostgresCourseRosterAdministration(db, clock);
            var detail = await service.AdminCourse(own.Org, own.Admin, course.CourseId);
            Assert.NotNull(detail); Assert.Equal(12, detail.Students.Count); Assert.Equal(2, detail.Groups.Count);
            var denied = await Assert.ThrowsAsync<UseCaseException>(() =>
                service.AdminCourse(own.Org, foreign.Admin, course.CourseId));
            Assert.Equal("Forbidden", denied.Message);
        }

        TokenAuthority teacherToken; TokenAuthority studentToken; TokenAuthority unassignedToken;
        await using (var db = PostgresFixture.Open(cs))
        {
            teacherToken = await Token(db, own.Org, teacher.ActorId);
            studentToken = await Token(db, own.Org, students[0].ActorId);
            unassignedToken = await Token(db, own.Org, unassigned.ActorId);
            var service = new PostgresCourseRosterAdministration(db, clock);
            Assert.Single(await service.LearningCatalog(teacherToken));
            Assert.Single(await service.LearningCatalog(studentToken));
            Assert.Empty(await service.LearningCatalog(unassignedToken));
            var teacherView = await service.TeacherCourse(teacherToken, course.CourseId);
            Assert.NotNull(teacherView); Assert.Equal(11, teacherView.Students.Count);
            Assert.Null(await service.TeacherCourse(unassignedToken, course.CourseId));

            var teacherAssignment = await db.Set<TeacherAssignmentScope>().SingleAsync(x =>
                x.OrganizationId == own.Org && x.CourseId == course.CourseId &&
                x.GroupId == first.GroupId && x.TeacherId == teacher.ActorId && x.State == "Active");
            var unassignedResult = await service.Unassign(new(own.Org, own.Admin, course.CourseId,
                first.GroupId, teacher.ActorId, teacherAssignment.GrantId, teacherAssignment.Version,
                Guid.NewGuid().ToString(), "m1-05"));
            Assert.True(unassignedResult.Changed);

            var current = await db.Set<EnrollmentScope>().SingleAsync(x => x.StudentId == students[1].ActorId && x.CourseId == course.CourseId);
            var removed = await service.Remove(new(own.Org, own.Admin, course.CourseId, students[1].ActorId,
                current.Version, Guid.NewGuid().ToString(), "m1-05"));
            Assert.True(removed.Changed);
        }
        await using (var check = PostgresFixture.Open(cs))
        {
            var revokedTeacherToken = await Token(check, own.Org, teacher.ActorId);
            Assert.Empty(await new PostgresCourseRosterAdministration(check, clock).LearningCatalog(revokedTeacherToken));
            Assert.Equal("Revoked", (await check.Set<EnrollmentScope>().SingleAsync(x =>
                x.StudentId == students[1].ActorId && x.CourseId == course.CourseId)).State);
            Assert.Equal(first.GroupId, (await check.Set<AssignmentScope>().SingleAsync(x => x.AssignmentId == historicalAssignment)).GroupId);
            Assert.Equal(2, await check.Set<CourseGroupCatalogRecord>().CountAsync(x => x.CourseId == course.CourseId));
        }
    }

    private static async Task<TokenAuthority> Token(LmsDbContext db, Guid org, Guid actor)
    {
        var membership = await db.Set<OrganizationMembership>().AsNoTracking().SingleAsync(x => x.OrganizationId == org && x.ActorId == actor);
        var grants = membership.EducationalRole == "Student"
            ? await db.Set<EnrollmentScope>().AsNoTracking().Where(x => x.OrganizationId == org && x.StudentId == actor && x.State == "Active")
                .Select(x => new EducationalGrant(AuthorityKind.Enrollment, null, x.CourseId, x.GroupId, x.MembershipGeneration, x.EnrollmentGeneration, x.Version)).ToListAsync()
            : await db.Set<TeacherAssignmentScope>().AsNoTracking().Where(x => x.OrganizationId == org && x.TeacherId == actor && x.State == "Active")
                .Select(x => new EducationalGrant(AuthorityKind.Teacher, x.GrantId, x.CourseId, x.GroupId, x.MembershipGeneration, x.Generation, x.Version)).ToListAsync();
        var role = membership.EducationalRole == "Student" ? ScopeRole.Student : ScopeRole.Teacher;
        return new(new(Guid.NewGuid(), actor, Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)),
            new(new(org, actor, membership.Generation, membership.Version, role, 0), grants),
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10));
    }
}
