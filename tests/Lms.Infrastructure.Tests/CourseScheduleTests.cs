using Lms.Application;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Tests;

public sealed partial class ScopedAuthorityTests
{
    private static readonly Guid ScheduleAdmin = Guid.Parse("00000000-0000-0000-0000-000000000019");
    [Fact]
    public async Task Course_schedule_workload_generation_exceptions_and_access_are_scoped()
    {
        var cs = await Seed();
        await using (var setup = PostgresFixture.Open(cs))
        {
            setup.Add(new ActorRecord { ActorId = ScheduleAdmin });
            setup.Add(new CourseCatalogRecord { CourseId = "a", OrganizationId = A,
                Title = "Олимпиадная математика", Description = "Synthetic", State = "Published",
                Version = 1, CreatedAt = At });
            setup.Add(new CourseCatalogRecord { CourseId = "a2", OrganizationId = A,
                Title = "Курс без processing binding", Description = "Synthetic", State = "Draft",
                Version = 1, CreatedAt = At });
            setup.Add(new CourseContextBinding { CourseId = "a", OrganizationId = A,
                ContextId = Context, ContextVersion = 1, EffectiveFrom = At.AddHours(-1) });
            await setup.SaveChangesAsync();
        }
        await Change(cs, new(AuthorityKind.Membership, AuthorityOperation.Grant, A,
            ScheduleAdmin, 0, Permissions: (int)OrganizationPermission.ManageOrganization));
        await Change(cs, new(AuthorityKind.Membership, AuthorityOperation.Grant, B,
            ScheduleAdmin, 0, Permissions: (int)OrganizationPermission.ManageOrganization));
        var authority = (await Current(cs, S))!;
        var student = new TokenAuthority(new(Guid.NewGuid(), S, Guid.NewGuid(), At.AddHours(1)),
            authority, At.AddMinutes(-1), At.AddMinutes(15));
        Guid monday; Guid saturday;
        await using (var db = PostgresFixture.Open(cs))
        {
            var service = new PostgresCourseScheduleService(db, clock, new ComponentClock());
            var workload = await service.SetWorkload(new(A, ScheduleAdmin, "a", 72, 48, 24, 45, 0,
                "workload-1", "schedule-test"));
            Assert.Equal(1, workload.Version);
            var replay = await service.SetWorkload(new(A, ScheduleAdmin, "a", 72, 48, 24, 45, 0,
                "workload-1", "schedule-test"));
            Assert.False(replay.Changed);
            Assert.Equal("ValidationFailed", (await Assert.ThrowsAsync<UseCaseException>(() =>
                service.SaveRule(Rule(2, "16:00", "18:00", 2, "Forged lesson", "rule-forged") with
                    { LessonId = "lesson-from-another-or-missing-course" }))).Code);
            monday = (await service.SaveRule(Rule(1, "17:00", "19:00", 2, "Занятие", "rule-mon"))).RuleId!.Value;
            saturday = (await service.SaveRule(Rule(6, "10:00", "13:00", 3, "Комбинаторика", "rule-sat"))).RuleId!.Value;
            Assert.Equal("ConcurrencyConflict", (await Assert.ThrowsAsync<UseCaseException>(() =>
                service.SetWorkload(new(A, ScheduleAdmin, "a", 72, 48, 24, 45, 0,
                    "workload-stale", "schedule-test")))).Code);
            Assert.Equal("ScheduleOverlap", (await Assert.ThrowsAsync<UseCaseException>(() =>
                service.SaveRule(Rule(1, "17:00", "20:00", 2, "Дубликат", "rule-overlap")))).Code);
            Assert.Equal("NonexistentLocalTime", (await Assert.ThrowsAsync<UseCaseException>(() =>
                service.SaveRule(new(A, ScheduleAdmin, "a", null, 7, "02:30", "04:00", 2,
                    new(2026, 3, 8), new(2026, 3, 8), "America/New_York",
                    CourseDeliveryMode.InPerson, "Кабинет", null, CourseEventKind.ExtraLesson,
                    "DST", null, null, true, 0, "rule-dst", "schedule-test")))).Code);
        }
        await using (var db = PostgresFixture.Open(cs))
        {
            var service = new PostgresCourseScheduleService(db, clock, new ComponentClock());
            var items = await service.Events(student, "a", new(2026, 10, 5), new(2026, 10, 11));
            Assert.Equal(2, items.Count); Assert.Equal([2, 3], items.Select(x => x.AcademicHours));
            Assert.Equal("ScheduleRangeInvalid", (await Assert.ThrowsAsync<UseCaseException>(() =>
                service.Events(student, "a", new(2026, 1, 1), new(2026, 5, 1)))).Code);
            await service.ChangeOccurrence(new(A, ScheduleAdmin, "a", monday, new(2026, 10, 5),
                CourseOccurrenceStatus.Moved, new(2026, 10, 6), "18:00", "20:00", null,
                null, null, null, 0, "move-one", "schedule-test"));
            await service.ChangeOccurrence(new(A, ScheduleAdmin, "a", saturday, new(2026, 10, 10),
                CourseOccurrenceStatus.Cancelled, null, null, null, null, null, null, null, 0,
                "cancel-one", "schedule-test"));
        }
        await using (var db = PostgresFixture.Open(cs))
        {
            var service = new PostgresCourseScheduleService(db, clock, new ComponentClock());
            var items = await service.Events(student, "a", new(2026, 10, 5), new(2026, 10, 11));
            Assert.Equal(2, items.Count);
            var moved = Assert.Single(items, x => x.Status == CourseOccurrenceStatus.Moved);
            Assert.Equal(new DateOnly(2026, 10, 5), moved.OriginalDate);
            Assert.Equal(18, TimeZoneInfo.ConvertTime(moved.StartsAt,
                TimeZoneInfo.FindSystemTimeZoneById("Asia/Omsk")).Hour);
            Assert.Single(items, x => x.Status == CourseOccurrenceStatus.Cancelled);
            var workload = await service.Workload(student, "a");
            Assert.NotNull(workload); Assert.Null(workload.ConductedAcademicHours);
            Assert.True(workload.CalendarPlanMismatch);
            var forged = student with { Authority = student.Authority with
                { Grants = [student.Authority.Grants[0] with { CourseId = "b" }] } };
            Assert.Equal("Forbidden", (await Assert.ThrowsAsync<UseCaseException>(() =>
                service.Events(forged, "a", new(2026, 10, 5), new(2026, 10, 11)))).Code);
            Assert.Equal("NotFound", (await Assert.ThrowsAsync<UseCaseException>(() =>
                service.SetWorkload(new(B, ScheduleAdmin, "a", 72, 48, 24, 45, 0,
                    "foreign-write", "schedule-test")))).Code);
            Assert.Equal("Forbidden", (await Assert.ThrowsAsync<UseCaseException>(() =>
                service.AdminSchedule(A, ScheduleAdmin, "a2"))).Code);
        }
        await using (var restrict = PostgresFixture.Open(cs))
        {
            restrict.Add(new ProcessingRestriction { RestrictionId = Guid.NewGuid(), OrganizationId = A,
                ContextId = Context, OriginVersion = 1, SubjectActorId = S, Operations = 1,
                State = "Active", Version = 1 });
            await restrict.SaveChangesAsync();
        }
        await using (var db = PostgresFixture.Open(cs))
        {
            var service = new PostgresCourseScheduleService(db, clock, new ComponentClock());
            Assert.Equal("Forbidden", (await Assert.ThrowsAsync<UseCaseException>(() =>
                service.Events(student, "a", new(2026, 10, 5), new(2026, 10, 11)))).Code);
            Assert.Empty(await db.Set<CourseScheduleRule>().Where(x => x.CourseId == "b").ToListAsync());
        }
        await using (var direct = PostgresFixture.Open(cs))
        {
            direct.Add(new CourseWorkload { CourseId = "a", OrganizationId = B,
                TotalAcademicHours = 1, ScheduledAcademicHours = 1, IndependentAcademicHours = 0,
                AcademicHourMinutes = 45, Version = 1, UpdatedAt = At, UpdatedBy = ScheduleAdmin });
            await Assert.ThrowsAsync<DbUpdateException>(() => direct.SaveChangesAsync());
        }
    }

    private static SaveCourseScheduleRuleCommand Rule(int day, string start, string end,
        int hours, string title, string key) => new(A, ScheduleAdmin, "a", null, day, start, end, hours,
        new(2026, 10, 1), new(2027, 3, 31), "Asia/Omsk", CourseDeliveryMode.InPerson,
        "Кабинет 305", null, CourseEventKind.Lesson, title, "Synthetic", null, true, 0,
        key, "schedule-test");
}
