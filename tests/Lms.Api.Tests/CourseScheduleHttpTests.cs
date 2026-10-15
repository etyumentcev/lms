using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Lms.Api.Tests;

public sealed partial class CourseRosterHttpTests
{
    [Fact]
    public async Task Course_schedule_http_routes_enforce_scope_csrf_and_idempotency()
    {
        await using var app = await fixture.CreateApp(organizationBootstrap: true);
        var own = await Bootstrap(app, "schedule-A");
        var foreign = await Bootstrap(app, "schedule-B");
        var student = await Account(app, own.Admin, own.Org, "schedule-student", "Student");
        var unassigned = await Account(app, own.Admin, own.Org, "schedule-teacher", "Teacher");

        var created = await own.Admin.PostAsJsonAsync("/api/v1/admin/courses", new
        {
            title = "Олимпиадная математика, 6 класс",
            description = "Synthetic schedule course",
            idempotencyKey = Guid.NewGuid().ToString()
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var courseId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("courseId").GetString()!;
        var contextId = Guid.NewGuid();
        var effectiveFrom = DateTimeOffset.UtcNow;
        (await own.Admin.PostAsJsonAsync("/api/v1/admin/processing-context/versions", new
        {
            operationId = contextId,
            expectedRevision = 0,
            expectedContextId = (Guid?)null,
            expectedContextVersion = (long?)null,
            effectiveFrom
        })).EnsureSuccessStatusCode();
        (await own.Admin.PostAsJsonAsync($"/api/v1/admin/courses/{courseId}/processing-context", new
        {
            operationId = Guid.NewGuid(),
            expectedRevision = 1,
            contextId,
            contextVersion = 1L,
            expectedBindingFrom = (DateTimeOffset?)null,
            effectiveFrom = (DateTimeOffset?)DateTimeOffset.UtcNow
        })).EnsureSuccessStatusCode();
        var group = await own.Admin.PostAsJsonAsync($"/api/v1/admin/courses/{courseId}/groups", new
        {
            name = "Группа А",
            description = "Synthetic schedule group",
            idempotencyKey = Guid.NewGuid().ToString()
        });
        group.EnsureSuccessStatusCode();
        var groupId = (await group.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("groupId").GetString()!;
        (await own.Admin.PostAsJsonAsync($"/api/v1/admin/courses/{courseId}/groups/{groupId}/students", new
        {
            studentIds = new[] { student.Actor },
            idempotencyKey = Guid.NewGuid().ToString()
        })).EnsureSuccessStatusCode();

        var workloadKey = Guid.NewGuid().ToString();
        var workloadBody = new
        {
            totalAcademicHours = 72,
            scheduledAcademicHours = 48,
            independentAcademicHours = 24,
            academicHourMinutes = 45,
            expectedVersion = 0,
            idempotencyKey = workloadKey
        };
        var workload = await own.Admin.PostAsJsonAsync(
            $"/api/v1/admin/courses/{courseId}/workload", workloadBody);
        workload.EnsureSuccessStatusCode();
        Assert.Equal(1, (await workload.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("version").GetInt64());
        var replay = await own.Admin.PostAsJsonAsync(
            $"/api/v1/admin/courses/{courseId}/workload", workloadBody);
        replay.EnsureSuccessStatusCode();
        Assert.False((await replay.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("changed").GetBoolean());

        var rule = await own.Admin.PostAsJsonAsync($"/api/v1/admin/courses/{courseId}/schedule/rules", new
        {
            ruleId = (Guid?)null,
            isoDayOfWeek = 1,
            startTime = "17:00",
            endTime = "19:00",
            academicHours = 2,
            effectiveFrom = "2026-10-01",
            effectiveTo = "2026-12-31",
            timeZoneId = "Asia/Omsk",
            deliveryMode = "InPerson",
            location = "Кабинет 305",
            onlineUrl = (string?)null,
            eventKind = "Lesson",
            title = "Занятие 4. Инварианты",
            description = "Synthetic",
            lessonId = (string?)null,
            isActive = true,
            expectedVersion = 0,
            idempotencyKey = Guid.NewGuid().ToString()
        });
        rule.EnsureSuccessStatusCode();

        using var studentClient = await Login(app, own.Org, student.User, student.Password);
        var events = await studentClient.GetAsync(
            $"/api/v1/courses/{courseId}/schedule/events?from=2026-10-05&to=2026-10-11");
        events.EnsureSuccessStatusCode();
        var items = (await events.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().ToArray();
        var item = Assert.Single(items);
        Assert.Equal("Кабинет 305", item.GetProperty("location").GetString());
        Assert.Equal(2, item.GetProperty("academicHours").GetInt32());
        var studentWrite = await studentClient.PostAsJsonAsync(
            $"/api/v1/admin/courses/{courseId}/workload", workloadBody);
        Assert.Equal(HttpStatusCode.Forbidden, studentWrite.StatusCode);

        var foreignRead = await foreign.Admin.GetAsync($"/api/v1/admin/courses/{courseId}/schedule");
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        Assert.DoesNotContain("Олимпиадная", await foreignRead.Content.ReadAsStringAsync());

        using var unassignedClient = await Login(app, own.Org, unassigned.User, unassigned.Password);
        Assert.Null(unassignedClient.DefaultRequestHeaders.Authorization);
        Assert.Equal(HttpStatusCode.Unauthorized, (await unassignedClient.GetAsync(
            $"/api/v1/courses/{courseId}/schedule?from=2026-10-05&to=2026-10-11")).StatusCode);

        own.Admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.Forbidden, (await own.Admin.PostAsJsonAsync(
            $"/api/v1/admin/courses/{courseId}/workload", workloadBody)).StatusCode);
    }
}
