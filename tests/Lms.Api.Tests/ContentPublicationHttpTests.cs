using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Application;
using Lms.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Tests;

public sealed class ContentPublicationHttpTests(ApiPostgresFixture fixture) : IClassFixture<ApiPostgresFixture>
{
    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Lms.sln"))) root = root.Parent;
        return root?.FullName ?? throw new InvalidOperationException("Lms.sln repository root was not found.");
    }

    private static MultipartFormDataContent ImportBody(string file, string key, string revision)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(File.ReadAllBytes(file));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(content, "file", Path.GetFileName(file));
        form.Add(new StringContent(key), "idempotencyKey");
        form.Add(new StringContent(revision), "sourceRevision");
        return form;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<(string CourseId, string GroupId)> SeedM105Draft(ApiFactory app)
    {
        await app.InitializeTeacherSecurity();
        var now = DateTimeOffset.UtcNow;
        var courseId = "m1-05-draft-" + Guid.NewGuid().ToString("N");
        const string groupId = "G1";
        await using (var db = app.Open())
        {
            db.AddRange(
                new CourseRegistration { CourseId = courseId, OrganizationId = app.OrganizationId, MappingState = "Verified" },
                new CourseGroup { CourseId = courseId, GroupId = groupId },
                new CourseCatalogRecord { CourseId = courseId, OrganizationId = app.OrganizationId,
                    Title = "Подготовка к олимпиаде Эйлера", Description = "Черновой курс M1-05",
                    State = "Draft", Version = 1, CreatedAt = now },
                new CourseGroupCatalogRecord { CourseId = courseId, GroupId = groupId, Name = "G1",
                    Description = "Основная группа", Version = 1, CreatedAt = now });
            await db.SaveChangesAsync();
            db.Add(new CourseContextBinding { CourseId = courseId, OrganizationId = app.OrganizationId,
                ContextId = app.ProcessingContextId, ContextVersion = 1, EffectiveFrom = now.AddMinutes(-1) });
            await db.SaveChangesAsync();
        }
        await using (var scopes = app.Open())
        {
            var admin = new PostgresScopedAuthorityAdministration(scopes);
            await admin.Change(new(AuthorityKind.Teacher, AuthorityOperation.Grant, app.OrganizationId,
                ApiFactory.Teacher, 0, 1, courseId, groupId, Guid.NewGuid()));
            scopes.Add(new CourseEditorScope { GrantId = Guid.NewGuid(), EditorId = ApiFactory.Teacher,
                CourseId = courseId, OrganizationId = app.OrganizationId, MembershipGeneration = 1,
                Generation = 1, State = "Active", Version = 1 });
            await scopes.SaveChangesAsync();
        }
        await using (var scopes = app.Open())
        {
            var admin = new PostgresScopedAuthorityAdministration(scopes);
            await admin.Change(new(AuthorityKind.Enrollment, AuthorityOperation.Grant, app.OrganizationId,
                ApiFactory.Student, 0, 1, courseId, groupId));
        }
        return (courseId, groupId);
    }

    [Fact]
    public async Task Zip_import_publication_assignment_and_v2_preserve_exact_v1_and_private_teacher_content()
    {
        await using var app = await fixture.CreateApp(organizationBootstrap: true);
        var setup = await SeedM105Draft(app);
        var root = RepositoryRoot();
        var valid = Path.Combine(root, "tests/fixtures/content/m1-06-olympiad.zip");
        var versionTwo = Path.Combine(root, "tests/fixtures/content/m1-06-olympiad-v2.zip");
        var invalid = Path.Combine(root, "tests/fixtures/content/m1-06-invalid.zip");
        using var teacher = await app.SecureClient();

        var importKey = Guid.NewGuid().ToString();
        using var firstBody = ImportBody(valid, importKey, "synthetic-m1-06-v1");
        using var firstResponse = await teacher.PostAsync($"/api/v1/teacher/courses/{setup.CourseId}/imports", firstBody);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal("no-store, private", firstResponse.Headers.CacheControl?.ToString());
        var first = await Json(firstResponse);
        Assert.Equal("Ready", first.GetProperty("state").GetString());
        Assert.Equal(2, first.GetProperty("version").GetInt64());
        var importId = first.GetProperty("importId").GetGuid();

        using (var replayBody = ImportBody(valid, importKey, "synthetic-m1-06-v1"))
        using (var replay = await teacher.PostAsync($"/api/v1/teacher/courses/{setup.CourseId}/imports", replayBody))
            Assert.Equal(importId, (await Json(replay)).GetProperty("importId").GetGuid());
        using (var conflictBody = ImportBody(versionTwo, importKey, "synthetic-m1-06-v2"))
        using (var conflict = await teacher.PostAsync($"/api/v1/teacher/courses/{setup.CourseId}/imports", conflictBody))
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        using var studentPreviewResponse = await teacher.GetAsync(
            $"/api/v1/teacher/courses/{setup.CourseId}/imports/{importId}/preview/student");
        var studentPreview = await studentPreviewResponse.Content.ReadAsStringAsync();
        Assert.True(studentPreviewResponse.IsSuccessStatusCode);
        Assert.DoesNotContain("M1_TEACHER_ONLY", studentPreview);
        Assert.DoesNotContain("teacherArtifactHash", studentPreview);
        Assert.DoesNotContain("packageHash", studentPreview);
        using var teacherPreviewResponse = await teacher.GetAsync(
            $"/api/v1/teacher/courses/{setup.CourseId}/imports/{importId}/preview/teacher");
        var teacherPreview = await teacherPreviewResponse.Content.ReadAsStringAsync();
        Assert.Contains("M1_TEACHER_ONLY_SOLUTION_v1", teacherPreview);
        Assert.Contains("M1_TEACHER_ONLY_RUBRIC", teacherPreview);

        var publishKey = Guid.NewGuid().ToString();
        var publishBody = new { expectedImportVersion = 2, expectedCourseVersion = 1, idempotencyKey = publishKey };
        using var publishedResponse = await teacher.PostAsJsonAsync(
            $"/api/v1/teacher/courses/{setup.CourseId}/imports/{importId}/publish", publishBody);
        var published = await Json(publishedResponse);
        Assert.Equal(1, published.GetProperty("versionNumber").GetInt32());
        Assert.True(published.GetProperty("created").GetBoolean());
        var v1 = published.GetProperty("contentVersionId").GetGuid();
        using (var replay = await teacher.PostAsJsonAsync(
            $"/api/v1/teacher/courses/{setup.CourseId}/imports/{importId}/publish", publishBody))
            Assert.False((await Json(replay)).GetProperty("created").GetBoolean());

        var problemIds = Enumerable.Range(1, 5).Select(x => $"congruence-{x}").ToArray();
        var assignmentKey = Guid.NewGuid().ToString();
        var assignmentBody = new { contentVersionId = v1, lessonId = "number-theory", groupId = setup.GroupId,
            problemIds, expectedCourseVersion = 2, idempotencyKey = assignmentKey };
        using var assignedResponse = await teacher.PostAsJsonAsync(
            $"/api/v1/teacher/courses/{setup.CourseId}/open-and-assign", assignmentBody);
        var assigned = await Json(assignedResponse);
        Assert.Equal(5, assigned.GetProperty("assignmentCount").GetInt32());
        Assert.Equal(1, assigned.GetProperty("studentCount").GetInt32());
        using (var replay = await teacher.PostAsJsonAsync(
            $"/api/v1/teacher/courses/{setup.CourseId}/open-and-assign", assignmentBody))
            Assert.False((await Json(replay)).GetProperty("created").GetBoolean());

        var secondImportKey = Guid.NewGuid().ToString();
        using var secondBody = ImportBody(versionTwo, secondImportKey, "synthetic-m1-06-v2");
        using var secondResponse = await teacher.PostAsync($"/api/v1/teacher/courses/{setup.CourseId}/imports", secondBody);
        var second = await Json(secondResponse); var secondImportId = second.GetProperty("importId").GetGuid();
        var secondPublishKey = Guid.NewGuid().ToString();
        using (var stale = await teacher.PostAsJsonAsync(
            $"/api/v1/teacher/courses/{setup.CourseId}/imports/{secondImportId}/publish",
            new { expectedImportVersion = 2, expectedCourseVersion = 1, idempotencyKey = secondPublishKey }))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var secondPublishedResponse = await teacher.PostAsJsonAsync(
            $"/api/v1/teacher/courses/{setup.CourseId}/imports/{secondImportId}/publish",
            new { expectedImportVersion = 2, expectedCourseVersion = 2, idempotencyKey = secondPublishKey });
        var secondPublished = await Json(secondPublishedResponse);
        Assert.Equal(2, secondPublished.GetProperty("versionNumber").GetInt32());
        var v2 = secondPublished.GetProperty("contentVersionId").GetGuid();
        Assert.NotEqual(v1, v2);

        using var student = await app.SecureClient("Student");
        using var assignmentsResponse = await student.GetAsync($"/api/v1/my/courses/{setup.CourseId}/assignments");
        var assignmentsRaw = await assignmentsResponse.Content.ReadAsStringAsync();
        Assert.Equal("no-store, private", assignmentsResponse.Headers.CacheControl?.ToString());
        Assert.DoesNotContain("M1_TEACHER_ONLY", assignmentsRaw);
        var assignments = JsonDocument.Parse(assignmentsRaw).RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(5, assignments.Length);
        Assert.All(assignments, x => { Assert.Equal(v1, x.GetProperty("contentVersionId").GetGuid());
            Assert.Equal(1, x.GetProperty("versionNumber").GetInt32());
            Assert.Equal("NotStarted", x.GetProperty("status").GetString());
            Assert.Equal(0, x.GetProperty("attemptCount").GetInt32()); });
        using var studentContentResponse = await student.GetAsync(
            $"/api/v1/my/courses/{setup.CourseId}/versions/{v1}/content");
        var studentContent = await studentContentResponse.Content.ReadAsStringAsync();
        Assert.True(studentContentResponse.IsSuccessStatusCode);
        Assert.Equal("no-store, private", studentContentResponse.Headers.CacheControl?.ToString());
        Assert.DoesNotContain("M1_TEACHER_ONLY", studentContent);
        Assert.DoesNotContain("teacherArtifactHash", studentContent);
        Assert.DoesNotContain("packageHash", studentContent);
        Assert.DoesNotContain("Комбинаторные конструкции", studentContent);
        Assert.DoesNotContain("invariant", studentContent);
        using var studentAsset = await student.GetAsync(
            $"/api/v1/my/courses/{setup.CourseId}/versions/{v1}/assets/assets/euler-diagram.png");
        Assert.True(studentAsset.IsSuccessStatusCode);
        Assert.Equal("image/png", studentAsset.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await studentAsset.Content.ReadAsByteArrayAsync());
        using var studentTeacherPreview = await student.GetAsync(
            $"/api/v1/teacher/courses/{setup.CourseId}/imports/{importId}/preview/teacher");
        Assert.Equal(HttpStatusCode.Forbidden, studentTeacherPreview.StatusCode);

        var assignmentId = assignments[0].GetProperty("assignmentId").GetGuid();
        using var submittedResponse = await student.PostAsJsonAsync(
            $"/api/v1/problem-assignments/{assignmentId}/submissions",
            new { bodyMarkdown = "Синтетическое решение", attachmentIds = Array.Empty<Guid>(), expectedVersion = (long?)null });
        var submitted = await Json(submittedResponse); var workId = submitted.GetProperty("problemWorkId").GetGuid();
        using var workspaceResponse = await student.GetAsync($"/api/v1/problem-assignments/{assignmentId}/workspace");
        var workspace = await workspaceResponse.Content.ReadAsStringAsync();
        Assert.True(workspaceResponse.IsSuccessStatusCode);
        Assert.Contains("Рассмотрите классы вычетов", workspace);
        Assert.DoesNotContain("M1_TEACHER_ONLY", workspace);

        using var referenceResponse = await teacher.GetAsync($"/api/v1/teacher/problem-works/{workId}/reference");
        var reference = await referenceResponse.Content.ReadAsStringAsync();
        Assert.True(referenceResponse.IsSuccessStatusCode);
        Assert.Equal("no-store, private", referenceResponse.Headers.CacheControl?.ToString());
        Assert.Contains("M1_TEACHER_ONLY_SOLUTION_v1", reference);
        Assert.Contains("M1_TEACHER_ONLY_RUBRIC", reference);
        Assert.Contains("M1_TEACHER_ONLY_NOTES", reference);
        Assert.DoesNotContain("M1_TEACHER_ONLY_SOLUTION_v2", reference);
        Assert.Equal(1, JsonDocument.Parse(reference).RootElement.GetProperty("versionNumber").GetInt32());
        using var forbiddenReference = await student.GetAsync($"/api/v1/teacher/problem-works/{workId}/reference");
        var forbiddenReferenceBody = await forbiddenReference.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenReference.StatusCode);
        Assert.DoesNotContain("M1_TEACHER_ONLY", forbiddenReferenceBody);
        using var forbiddenTeacherAsset = await student.GetAsync(
            $"/api/v1/teacher/problem-works/{workId}/reference/assets/assets/teacher-only.png");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenTeacherAsset.StatusCode);
        Assert.DoesNotContain("M1_TEACHER_ONLY", await forbiddenTeacherAsset.Content.ReadAsStringAsync());

        await using (var revoke = app.Open())
        {
            var grant = await revoke.Set<TeacherAssignmentScope>().AsNoTracking().SingleAsync(x =>
                x.OrganizationId == app.OrganizationId && x.TeacherId == ApiFactory.Teacher &&
                x.CourseId == setup.CourseId && x.GroupId == setup.GroupId);
            await new PostgresScopedAuthorityAdministration(revoke).Change(new(AuthorityKind.Teacher,
                AuthorityOperation.Revoke, app.OrganizationId, ApiFactory.Teacher, grant.Version,
                grant.MembershipGeneration, setup.CourseId, setup.GroupId, grant.GrantId));
        }
        using var editorOnly = await app.SecureClient();
        using var forbiddenEditorReference = await editorOnly.GetAsync(
            $"/api/v1/teacher/problem-works/{workId}/reference");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenEditorReference.StatusCode);
        Assert.DoesNotContain("M1_TEACHER_ONLY", await forbiddenEditorReference.Content.ReadAsStringAsync());

        using var invalidBody = ImportBody(invalid, Guid.NewGuid().ToString(), "synthetic-m1-06-invalid");
        using var invalidResponse = await editorOnly.PostAsync(
            $"/api/v1/teacher/courses/{setup.CourseId}/imports", invalidBody);
        var invalidResult = await Json(invalidResponse);
        Assert.Equal("Invalid", invalidResult.GetProperty("state").GetString());
        var diagnostic = Assert.Single(invalidResult.GetProperty("diagnostics").EnumerateArray().ToArray());
        Assert.Equal("lessons/number-theory/problems/congruence-1.md", diagnostic.GetProperty("file").GetString());
        Assert.Equal(2, diagnostic.GetProperty("line").GetInt32());

        await using (var verify = app.Open())
        {
            var historical = await verify.Assignments.Where(x => x.CourseId == setup.CourseId).ToListAsync();
            Assert.Equal(5, historical.Count);
            Assert.All(historical, x => Assert.Equal(v1, x.Content.CourseVersionId));
            Assert.Equal(2, await verify.ContentVersions.CountAsync(x => x.CourseId == setup.CourseId));
            var artifacts = await verify.ContentArtifacts.Where(x => x.ContentVersionId == v1 || x.ContentVersionId == v2).ToListAsync();
            Assert.Equal(2, artifacts.Count);
            Assert.Equal(artifacts[0].StudentProjectionHash, artifacts[1].StudentProjectionHash);
            Assert.NotEqual(artifacts[0].PackageHash, artifacts[1].PackageHash);
            Assert.NotEqual(artifacts[0].TeacherArtifactHash, artifacts[1].TeacherArtifactHash);
        }
    }

    [Fact]
    public async Task Import_requires_course_editor_and_active_processing_context()
    {
        await using var app = await fixture.CreateApp(organizationBootstrap: true);
        await app.InitializeTeacherSecurity();
        var now = DateTimeOffset.UtcNow;
        await using (var db = app.Open())
        {
            db.AddRange(
                new CourseCatalogRecord { CourseId = "security-bootstrap", OrganizationId = app.OrganizationId,
                    Title = "Group-only course", Description = "Scope proof", State = "Draft", Version = 1, CreatedAt = now },
                new CourseContextBinding { CourseId = "security-bootstrap", OrganizationId = app.OrganizationId,
                    ContextId = app.ProcessingContextId, ContextVersion = 1, EffectiveFrom = now.AddMinutes(-1) });
            await db.SaveChangesAsync();
        }
        using var teacher = await app.SecureClient();
        var fixturePath = Path.Combine(RepositoryRoot(), "tests/fixtures/content/m1-06-olympiad.zip");
        using var groupOnlyBody = ImportBody(fixturePath, Guid.NewGuid().ToString(), "scope-proof");
        using var groupOnly = await teacher.PostAsync("/api/v1/teacher/courses/security-bootstrap/imports", groupOnlyBody);
        Assert.Equal(HttpStatusCode.Forbidden, groupOnly.StatusCode);

        var setup = await SeedM105Draft(app);
        await using (var db = app.Open())
        {
            db.Add(new ProcessingRestriction { RestrictionId = Guid.NewGuid(), ContextId = app.ProcessingContextId,
                OriginVersion = 1, OrganizationId = app.OrganizationId, SubjectActorId = ApiFactory.Teacher,
                Operations = 1, ResourceRef = setup.CourseId, State = "Active", Version = 1 });
            await db.SaveChangesAsync();
        }
        using var restrictedTeacher = await app.SecureClient();
        using var restrictedBody = ImportBody(fixturePath, Guid.NewGuid().ToString(), "restriction-proof");
        using var restricted = await restrictedTeacher.PostAsync($"/api/v1/teacher/courses/{setup.CourseId}/imports", restrictedBody);
        Assert.Equal(HttpStatusCode.Forbidden, restricted.StatusCode);
    }

    [Fact]
    public async Task Course_editor_and_group_teacher_revocations_are_independent_and_course_scoped()
    {
        await using var app = await fixture.CreateApp(organizationBootstrap: true);
        var setup = await SeedM105Draft(app);
        var otherCourseId = "editor-other-" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        await using (var db = app.Open())
        {
            db.AddRange(
                new CourseRegistration { CourseId = otherCourseId, OrganizationId = app.OrganizationId, MappingState = "Verified" },
                new CourseCatalogRecord { CourseId = otherCourseId, OrganizationId = app.OrganizationId,
                    Title = "Other course", Description = "Isolation proof", State = "Draft", Version = 1, CreatedAt = now });
            await db.SaveChangesAsync();
            db.Add(new CourseContextBinding { CourseId = otherCourseId, OrganizationId = app.OrganizationId,
                ContextId = app.ProcessingContextId, ContextVersion = 1, EffectiveFrom = now.AddMinutes(-1) });
            await db.SaveChangesAsync();
        }

        var fixturePath = Path.Combine(RepositoryRoot(), "tests/fixtures/content/m1-06-olympiad.zip");
        Guid importId;
        using (var both = await app.SecureClient())
        {
            using var otherBody = ImportBody(fixturePath, Guid.NewGuid().ToString(), "wrong-course");
            using var other = await both.PostAsync($"/api/v1/teacher/courses/{otherCourseId}/imports", otherBody);
            Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
            Assert.DoesNotContain("Other course", await other.Content.ReadAsStringAsync());

            using var importBody = ImportBody(fixturePath, Guid.NewGuid().ToString(), "before-editor-revoke");
            using var imported = await both.PostAsync($"/api/v1/teacher/courses/{setup.CourseId}/imports", importBody);
            importId = (await Json(imported)).GetProperty("importId").GetGuid();
        }

        await using (var db = app.Open())
        {
            var editor = await db.Set<CourseEditorScope>().SingleAsync(x =>
                x.OrganizationId == app.OrganizationId && x.EditorId == ApiFactory.Teacher &&
                x.CourseId == setup.CourseId);
            editor.State = "Revoked";
            editor.Version++;
            editor.RevocationRequestedAt = now;
            editor.AccessValidUntil = now;
            await db.SaveChangesAsync();
        }

        using (var teachingOnly = await app.SecureClient())
        {
            using var deniedImportBody = ImportBody(fixturePath, Guid.NewGuid().ToString(), "after-editor-revoke");
            using var deniedImport = await teachingOnly.PostAsync(
                $"/api/v1/teacher/courses/{setup.CourseId}/imports", deniedImportBody);
            Assert.Equal(HttpStatusCode.Forbidden, deniedImport.StatusCode);
            using var deniedPublish = await teachingOnly.PostAsJsonAsync(
                $"/api/v1/teacher/courses/{setup.CourseId}/imports/{importId}/publish",
                new { expectedImportVersion = 2, expectedCourseVersion = 1, idempotencyKey = Guid.NewGuid().ToString() });
            Assert.Equal(HttpStatusCode.Forbidden, deniedPublish.StatusCode);
            var teachingView = await teachingOnly.GetFromJsonAsync<JsonElement>(
                $"/api/v1/teacher/courses/{setup.CourseId}");
            Assert.Single(teachingView.GetProperty("groups").EnumerateArray());
            Assert.False(teachingView.GetProperty("canEditContent").GetBoolean());
        }

        await using (var db = app.Open())
        {
            var editor = await db.Set<CourseEditorScope>().SingleAsync(x =>
                x.OrganizationId == app.OrganizationId && x.EditorId == ApiFactory.Teacher &&
                x.CourseId == setup.CourseId);
            editor.State = "Active";
            editor.Generation++;
            editor.Version++;
            editor.RevocationRequestedAt = null;
            editor.AccessValidUntil = null;
            await db.SaveChangesAsync();
        }
        await using (var db = app.Open())
        {
            var teacher = await db.Set<TeacherAssignmentScope>().AsNoTracking().SingleAsync(x =>
                x.OrganizationId == app.OrganizationId && x.TeacherId == ApiFactory.Teacher &&
                x.CourseId == setup.CourseId && x.GroupId == setup.GroupId);
            var administration = new PostgresScopedAuthorityAdministration(db);
            await administration.Change(new(AuthorityKind.Teacher, AuthorityOperation.Revoke,
                app.OrganizationId, ApiFactory.Teacher, teacher.Version, teacher.MembershipGeneration,
                setup.CourseId, setup.GroupId, teacher.GrantId));
        }

        using var editorOnly = await app.SecureClient();
        var editorOnlyView = await editorOnly.GetFromJsonAsync<JsonElement>(
            $"/api/v1/teacher/courses/{setup.CourseId}");
        Assert.Empty(editorOnlyView.GetProperty("groups").EnumerateArray());
        Assert.Empty(editorOnlyView.GetProperty("students").EnumerateArray());
        Assert.True(editorOnlyView.GetProperty("canEditContent").GetBoolean());
        using var allowedImportBody = ImportBody(fixturePath, Guid.NewGuid().ToString(), "editor-only");
        using var allowedImport = await editorOnly.PostAsync(
            $"/api/v1/teacher/courses/{setup.CourseId}/imports", allowedImportBody);
        Assert.Equal(HttpStatusCode.Created, allowedImport.StatusCode);
    }

    [Fact]
    public async Task Parallel_publish_with_same_expected_versions_commits_exactly_one_immutable_version()
    {
        await using var app = await fixture.CreateApp(organizationBootstrap: true);
        var setup = await SeedM105Draft(app);
        using var firstTeacher = await app.SecureClient();
        using var secondTeacher = await app.SecureClient();
        var fixturePath = Path.Combine(RepositoryRoot(), "tests/fixtures/content/m1-06-olympiad.zip");
        using var importBody = ImportBody(fixturePath, Guid.NewGuid().ToString(), "parallel-publish");
        using var importedResponse = await firstTeacher.PostAsync(
            $"/api/v1/teacher/courses/{setup.CourseId}/imports", importBody);
        var imported = await Json(importedResponse); var importId = imported.GetProperty("importId").GetGuid();
        var path = $"/api/v1/teacher/courses/{setup.CourseId}/imports/{importId}/publish";
        var first = firstTeacher.PostAsJsonAsync(path, new
        { expectedImportVersion = 2, expectedCourseVersion = 1, idempotencyKey = Guid.NewGuid().ToString() });
        var second = secondTeacher.PostAsJsonAsync(path, new
        { expectedImportVersion = 2, expectedCourseVersion = 1, idempotencyKey = Guid.NewGuid().ToString() });
        var responses = await Task.WhenAll(first, second);
        try
        {
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict],
                responses.Select(x => x.StatusCode).Order().ToArray());
        }
        finally { foreach (var response in responses) response.Dispose(); }
        await using var verify = app.Open();
        Assert.Equal(1, await verify.ContentVersions.CountAsync(x => x.CourseId == setup.CourseId));
        Assert.Equal(1, await verify.ContentArtifacts.CountAsync());
    }
}
