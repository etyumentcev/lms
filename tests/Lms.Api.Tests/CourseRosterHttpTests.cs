using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Lms.Api.Tests;

public sealed class CourseRosterHttpTests(ApiPostgresFixture fixture) : IClassFixture<ApiPostgresFixture>
{
    private static string PlatformToken()
    {
        var now = DateTime.UtcNow;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("lms-local-development",
            OrganizationBootstrapHttp.PlatformAudience,
            [new Claim("sub", "synthetic-m1-05-operator"), new Claim("scope", OrganizationBootstrapHttp.BootstrapCapability)],
            now, now.AddMinutes(10), new SigningCredentials(new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(ApiFactory.LocalSigningKey)), SecurityAlgorithms.HmacSha256)));
    }

    private static HttpClient Client(ApiFactory app)
    {
        var client = app.CreateClient(); client.BaseAddress = new("https://lms.test");
        client.DefaultRequestHeaders.Add("Origin", "https://lms.test"); return client;
    }

    private static async Task Csrf(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/auth/csrf"); response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
    }

    private static async Task<(Guid Org, HttpClient Admin)> Bootstrap(ApiFactory app, string suffix)
    {
        var client = Client(app); client.DefaultRequestHeaders.Authorization = new("Bearer", PlatformToken());
        var user = "m105-admin-" + suffix + "-" + Guid.NewGuid().ToString("N")[..6];
        var response = await client.PostAsJsonAsync("/api/v1/platform/organizations", new
        { commandId = Guid.NewGuid(), organizationName = "Синтетическая школа " + suffix, adminUserName = user });
        response.EnsureSuccessStatusCode(); var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var org = json.GetProperty("organizationId").GetGuid(); client.DefaultRequestHeaders.Authorization = null; await Csrf(client);
        (await client.PostAsJsonAsync("/api/v1/auth/activate-first-admin", new { organizationId = org, userName = user,
            activationCode = json.GetProperty("activationCode").GetString(), password = "Synthetic-Admin-Password-2039!" })).EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = user,
            password = "Synthetic-Admin-Password-2039!", organizationId = org }); login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        await Csrf(client); return (org, client);
    }

    private static async Task<(Guid Actor, string User, string Password)> Account(ApiFactory app, HttpClient admin, Guid org,
        string suffix, string role)
    {
        var user = "m105-" + suffix + "-" + Guid.NewGuid().ToString("N")[..6];
        var password = "Synthetic-Participant-Password-2039!";
        var response = await admin.PostAsJsonAsync("/api/v1/admin/accounts", new { externalRosterKey = suffix,
            displayName = "Синтетический " + suffix, userName = user, role, idempotencyKey = Guid.NewGuid().ToString() });
        response.EnsureSuccessStatusCode(); var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        using var activation = Client(app); await Csrf(activation);
        var activated = await activation.PostAsJsonAsync("/api/v1/auth/activate", new { organizationId = org,
            userName = user, code = json.GetProperty("activationCode").GetString(), password });
        activated.EnsureSuccessStatusCode();
        return (json.GetProperty("actorId").GetGuid(), user, password);
    }

    private static async Task<HttpClient> Login(ApiFactory app, Guid org, string user, string password)
    {
        var client = Client(app); await Csrf(client);
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = user, password, organizationId = org });
        response.EnsureSuccessStatusCode(); var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var access = json.GetProperty("accessToken");
        if (access.ValueKind == JsonValueKind.String)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access.GetString());
        await Csrf(client); return client;
    }

    [Fact]
    public async Task Protected_course_roster_routes_enforce_role_org_and_current_assignments()
    {
        await using var app = await fixture.CreateApp(organizationBootstrap: true);
        var own = await Bootstrap(app, "A"); var foreign = await Bootstrap(app, "B");
        var student = await Account(app, own.Admin, own.Org, "Ученик-01", "Student");
        var teacher = await Account(app, own.Admin, own.Org, "Преподаватель-01", "Teacher");
        var unassigned = await Account(app, own.Admin, own.Org, "Преподаватель-02", "Teacher");

        var create = await own.Admin.PostAsJsonAsync("/api/v1/admin/courses", new { title = "Подготовка к олимпиаде Эйлера",
            description = "Черновой курс", idempotencyKey = Guid.NewGuid().ToString() });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode); var course = await create.Content.ReadFromJsonAsync<JsonElement>();
        var courseId = course.GetProperty("courseId").GetString()!;
        async Task<string> Group(string name)
        {
            var response = await own.Admin.PostAsJsonAsync($"/api/v1/admin/courses/{courseId}/groups", new
            { name, description = "Синтетическая группа", idempotencyKey = Guid.NewGuid().ToString() });
            response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("groupId").GetString()!;
        }
        var first = await Group("Эйлер — основная"); var second = await Group("Эйлер — резерв");
        (await own.Admin.PostAsJsonAsync($"/api/v1/admin/courses/{courseId}/groups/{first}/students", new
        { studentIds = new[] { student.Actor }, idempotencyKey = Guid.NewGuid().ToString() })).EnsureSuccessStatusCode();
        (await own.Admin.PostAsJsonAsync($"/api/v1/admin/courses/{courseId}/groups/{first}/teachers", new
        { teacherId = teacher.Actor, idempotencyKey = Guid.NewGuid().ToString() })).EnsureSuccessStatusCode();

        using var studentClient = await Login(app, own.Org, student.User, student.Password);
        var studentCourses = await studentClient.GetFromJsonAsync<JsonElement>("/api/v1/my/courses");
        Assert.Equal(courseId, studentCourses.GetProperty("items")[0].GetProperty("courseId").GetString());
        Assert.Equal("Draft", studentCourses.GetProperty("items")[0].GetProperty("state").GetString());

        using var teacherClient = await Login(app, own.Org, teacher.User, teacher.Password);
        Assert.Single((await teacherClient.GetFromJsonAsync<JsonElement>("/api/v1/my/courses")).GetProperty("items").EnumerateArray());
        var teacherView = await teacherClient.GetFromJsonAsync<JsonElement>($"/api/v1/teacher/courses/{courseId}");
        Assert.Single(teacherView.GetProperty("students").EnumerateArray());
        Assert.Equal(first, teacherView.GetProperty("groups")[0].GetProperty("groupId").GetString());

        using var unassignedClient = await Login(app, own.Org, unassigned.User, unassigned.Password);
        Assert.Null(unassignedClient.DefaultRequestHeaders.Authorization);
        Assert.Equal(HttpStatusCode.Unauthorized, (await unassignedClient.GetAsync("/api/v1/my/courses")).StatusCode);

        var detail = await own.Admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/courses/{courseId}");
        var enrolled = detail.GetProperty("students")[0];
        var moved = await own.Admin.PostAsJsonAsync($"/api/v1/admin/courses/{courseId}/students/{student.Actor}/move", new
        { targetGroupId = second, expectedVersion = enrolled.GetProperty("version").GetInt64(), idempotencyKey = Guid.NewGuid().ToString() });
        moved.EnsureSuccessStatusCode();
        teacherView = await teacherClient.GetFromJsonAsync<JsonElement>($"/api/v1/teacher/courses/{courseId}");
        Assert.Empty(teacherView.GetProperty("students").EnumerateArray());

        var assignment = (await own.Admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/courses/{courseId}"))
            .GetProperty("teachers")[0];
        var removeTeacherKey = Guid.NewGuid().ToString();
        var removeTeacherBody = new
        {
            grantId = assignment.GetProperty("grantId").GetGuid(),
            expectedVersion = assignment.GetProperty("version").GetInt64(),
            idempotencyKey = removeTeacherKey
        };
        var removedTeacher = await own.Admin.PostAsJsonAsync(
            $"/api/v1/admin/courses/{courseId}/groups/{first}/teachers/{teacher.Actor}/remove", removeTeacherBody);
        removedTeacher.EnsureSuccessStatusCode();
        Assert.True((await removedTeacher.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changed").GetBoolean());
        var replayedRemoval = await own.Admin.PostAsJsonAsync(
            $"/api/v1/admin/courses/{courseId}/groups/{first}/teachers/{teacher.Actor}/remove", removeTeacherBody);
        replayedRemoval.EnsureSuccessStatusCode();
        Assert.False((await replayedRemoval.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changed").GetBoolean());
        using var teacherAfterRemoval = await Login(app, own.Org, teacher.User, teacher.Password);
        Assert.Null(teacherAfterRemoval.DefaultRequestHeaders.Authorization);
        Assert.Equal(HttpStatusCode.Unauthorized, (await teacherAfterRemoval.GetAsync("/api/v1/my/courses")).StatusCode);

        var foreignList = await foreign.Admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/courses");
        Assert.Empty(foreignList.GetProperty("items").EnumerateArray());
        var forbiddenSelector = await foreign.Admin.GetAsync($"/api/v1/admin/courses/{courseId}");
        Assert.Equal(HttpStatusCode.NotFound, forbiddenSelector.StatusCode);
        var foreignBody = await forbiddenSelector.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Подготовка", foreignBody); Assert.DoesNotContain("studentCount", foreignBody);
    }
}
