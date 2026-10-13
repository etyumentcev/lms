using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Application;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lms.Api.Tests;

public sealed class ReadApiTests(ApiPostgresFixture postgres) : IClassFixture<ApiPostgresFixture>
{
    private static string Problem(Guid id) => $"/api/v1/problem-assignments/{id}/workspace";
    private static string Course(ProblemAssignment a) => $"/api/v1/courses/{a.CourseId}/workspace";
    private static string Lesson(ProblemAssignment a) => $"/api/v1/courses/{a.CourseId}/lessons/{a.Content.LessonId}/workspace";
    private static async Task<T> Get<T>(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json))!;
    }
    private static async Task Error(HttpClient client, string path, int status)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
    private static async Task Publish(ApiFactory app, ProblemAssignment a, int number = 1, string statement = "Old statement", string title = "Algebra")
    {
        await using var db = app.Open();
        db.ContentVersions.Add(new PublishedContentVersion { Id = a.Content.CourseVersionId, CourseId = a.CourseId,
            VersionNumber = number, SourceRevision = "commit-" + number, PublishedAt = DateTimeOffset.UtcNow,
            ContentJson = JsonSerializer.Serialize(new PublishedCourseContent(title, "Course description", [
                new(a.Content.LessonId, 1, "Divisibility", "Lesson summary", [new(a.Content.ProblemId, 1, "Remainders", statement)])]), ApiFactory.Json) });
        await db.SaveChangesAsync();
    }
    private static async Task<SubmissionResult> Submit(ApiFactory app, ProblemAssignment a, long? version = null, string body = "First solution")
    {
        using var student = app.Client(a.StudentId);
        var response = await student.PostAsJsonAsync($"/api/v1/problem-assignments/{a.Id}/submissions",
            new { bodyMarkdown = body, attachmentIds = Array.Empty<Guid>(), expectedVersion = version });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SubmissionResult>(ApiFactory.Json))!;
    }
    private static async Task<ReviewResult> Review(ApiFactory app, SubmissionResult s, bool accept = false)
    {
        // Read-model fixture: teacher execution is covered by the trusted HTTP suite.
        // Legacy endpoints are intentionally fail-closed and cannot seed historical state.
        await using var db = app.Open();
        await using var tx = await db.Database.BeginTransactionAsync();
        var work = (await new PostgresWorkStore(db).FindWork(s.ProblemWorkId, default))!;
        var messageIds = work.Messages.Select(m => m.Id).ToHashSet();
        var review = accept ? work.Accept(ApiFactory.Teacher, s.SubmissionId, null, DateTimeOffset.UtcNow)
            : work.RequestRevision(ApiFactory.Teacher, s.SubmissionId, "Explain this step", DateTimeOffset.UtcNow);
        await db.Works.Where(w => w.Id == work.Id && w.Version == s.Version).ExecuteUpdateAsync(set =>
            set.SetProperty(w => w.Version, work.Version).SetProperty(w => w.Status, work.Status).SetProperty(w => w.AcceptedAt, work.AcceptedAt));
        db.Add(review); db.AddRange(work.Messages.Where(m => !messageIds.Contains(m.Id)));
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return new(review.Id, review.MessageId, work.Status, work.Version);
    }

    [Fact]
    public async Task Not_started_workspace_and_lesson_use_assignment_identity_without_writes()
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed();
        await Publish(app, a);
        using var student = app.Client(ApiFactory.Student);
        var dto = await Get<StudentProblemWorkspaceDto>(student, Problem(a.Id));
        Assert.Equal(a.Id, dto.Assignment.Id);
        Assert.Null(dto.Work.ProblemWorkId);
        Assert.Equal(ProblemWorkStatus.NotStarted, dto.Work.Status);
        Assert.Equal(0, dto.Work.AttemptCount);
        Assert.Null(dto.Work.Version);
        Assert.Null(dto.Work.LatestSubmission);
        Assert.Null(dto.Work.LatestReview);
        Assert.Empty(dto.History.Submissions);
        Assert.Empty(dto.History.Reviews);
        Assert.Empty(dto.History.Messages);
        Assert.Empty(dto.Hints);
        Assert.Empty(dto.UsefulMaterials);
        Assert.True(dto.Permissions.CanSubmit);
        Assert.False(dto.Permissions.CanPostMessage);
        Assert.False(dto.Permissions.CanAccept);
        var lesson = await Get<LessonWorkspaceDto>(student, Lesson(a));
        Assert.Equal(a.Id, Assert.Single(lesson.Problems).ProblemAssignmentId);
        Assert.Null(lesson.Problems[0].Work.ProblemWorkId);
        Assert.Equal("NotStarted", lesson.Progress.Status);
        Assert.DoesNotContain("hasDraft", await student.GetStringAsync(Lesson(a)));
        await using var db = app.Open();
        Assert.Equal(0, await db.Works.CountAsync());
        Assert.Equal(0, await db.Set<Submission>().CountAsync());
        Assert.Equal(0, await db.Events.CountAsync());
    }

    [Fact]
    public async Task Old_and_new_assignments_resolve_exact_publication_in_all_workspaces()
    {
        await using var app = await postgres.CreateApp();
        var old = await app.Seed();
        await Publish(app, old);
        var first = await Submit(app, old);
        var next = new ProblemAssignment(Guid.NewGuid(), old.StudentId, old.CourseId,
            new(Guid.NewGuid(), old.Content.LessonId, old.Content.ProblemId));
        await Publish(app, next, 2, "New statement");
        await using (var db = app.Open())
        {
            db.Assignments.Add(next);
            db.Lessons.Add(new LessonAccess { CourseId = next.CourseId, CourseVersionId = next.Content.CourseVersionId, LessonId = next.Content.LessonId, IsPublished = true });
            await db.SaveChangesAsync();
        }
        using var student = app.Client(ApiFactory.Student);
        using var teacher = app.Client(ApiFactory.Teacher, "Teacher");
        Assert.Equal("Old statement", (await Get<StudentProblemWorkspaceDto>(student, Problem(old.Id))).Problem.StatementMarkdown);
        Assert.Equal("New statement", (await Get<StudentProblemWorkspaceDto>(student, Problem(next.Id))).Problem.StatementMarkdown);
        Assert.Equal("Old statement", (await Get<ProblemWorkDetailsDto>(teacher, $"/api/v1/problem-works/{first.ProblemWorkId}")).Problem.StatementMarkdown);
        var lesson = await Get<LessonWorkspaceDto>(student, Lesson(old));
        Assert.Equal(2, lesson.Problems.Count);
        Assert.Equal("Old statement", lesson.Problems.Single(p => p.ProblemAssignmentId == old.Id).Problem.StatementMarkdown);
        Assert.Equal("New statement", lesson.Problems.Single(p => p.ProblemAssignmentId == next.Id).Problem.StatementMarkdown);
        var course = await Get<CourseWorkspaceDto>(student, Course(old));
        Assert.Equal(1, course.Progress.AvailableLessons);
        Assert.Equal(2, Assert.Single(course.Lessons).CourseVersionIds.Count);
        Assert.Null(course.Course.PublishedVersionId);
    }

    [Theory]
    [InlineData("UPDATE \"PublishedContentVersions\" SET \"SourceRevision\" = 'changed'")]
    [InlineData("DELETE FROM \"PublishedContentVersions\"")]
    public async Task Published_snapshot_is_immutable_in_database(string sql)
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed();
        await Publish(app, a);
        await using var db = app.Open();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.Equal("23514", error.SqlState);
    }

    [Fact]
    public async Task Missing_historical_content_fails_closed_without_latest_fallback()
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed();
        var next = new ProblemAssignment(Guid.NewGuid(), a.StudentId, a.CourseId, new(Guid.NewGuid(), "lesson", "problem"));
        await Publish(app, next, 2, "Must not leak as fallback");
        using var student = app.Client(ApiFactory.Student);
        await Error(student, Problem(a.Id), 503);
    }

    [Theory]
    [InlineData("stranger")]
    [InlineData("inactive")]
    [InlineData("withdrawn")]
    [InlineData("unpublished")]
    [InlineData("scheduled")]
    public async Task Student_negative_access_blocks_assignment_work_and_locked_content(string kind)
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed();
        await Publish(app, a, statement: "SECRET STATEMENT");
        var submission = await Submit(app, a);
        await using (var db = app.Open())
        {
            if (kind == "inactive") (await db.Enrollments.SingleAsync()).IsActive = false;
            if (kind == "withdrawn") (await db.Assignments.SingleAsync()).Withdraw();
            if (kind == "unpublished") (await db.Lessons.SingleAsync()).IsPublished = false;
            if (kind == "scheduled") (await db.Lessons.SingleAsync()).AvailableFrom = DateTimeOffset.UtcNow.AddDays(1);
            await db.SaveChangesAsync();
        }
        using var student = app.Client(kind == "stranger" ? Guid.NewGuid() : ApiFactory.Student);
        await Error(student, Problem(a.Id), 403);
        await Error(student, $"/api/v1/problem-works/{submission.ProblemWorkId}", 403);
        if (kind is "inactive" or "stranger")
        {
            await Error(student, Course(a), 403);
            await Error(student, Lesson(a), 403);
        }
        if (kind is "unpublished" or "scheduled")
        {
            await Error(student, Lesson(a), 403);
            var course = await Get<CourseWorkspaceDto>(student, Course(a));
            Assert.False(Assert.Single(course.Lessons).Availability.IsAvailable);
            Assert.Null(course.Lessons[0].Lesson.Summary);
            Assert.Equal(0, course.Progress.AvailableLessons);
            Assert.Equal(0, course.Progress.Percent);
            Assert.DoesNotContain("SECRET STATEMENT", await student.GetStringAsync(Course(a)));
            Assert.DoesNotContain("Lesson summary", await student.GetStringAsync(Course(a)));
        }
    }

    [Theory]
    [InlineData("none")]
    [InlineData("wrong-group")]
    [InlineData("matching-group")]
    [InlineData("whole-course")]
    public async Task Teacher_work_and_queue_follow_C2_course_group_boundary(string grant)
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed(teacherGrant: grant != "none", studentGroup: "A",
            teacherGroup: grant == "matching-group" ? "A" : grant == "wrong-group" ? "B" : null);
        await Publish(app, a);
        var s = await Submit(app, a);
        using var teacher = app.Client(ApiFactory.Teacher, "Teacher");
        var allowed = grant is "matching-group" or "whole-course";
        if (allowed) Assert.True((await Get<ProblemWorkDetailsDto>(teacher, $"/api/v1/problem-works/{s.ProblemWorkId}")).Permissions.CanAccept);
        else await Error(teacher, $"/api/v1/problem-works/{s.ProblemWorkId}", 403);
        var queue = await Get<ReviewQueueResponseDto>(teacher, "/api/v1/teacher/review-queue");
        Assert.Equal(allowed ? 1 : 0, queue.Total);
        if (grant == "none")
        {
            await Error(teacher, Course(a), 403);
            await Error(teacher, Lesson(a), 403);
        }
        else
        {
            var lesson = await Get<LessonWorkspaceDto>(teacher, Lesson(a));
            Assert.Equal(allowed ? 1 : 0, lesson.Problems.Count);
        }
    }

    [Fact]
    public async Task Course_enrollment_is_real_for_student_null_for_teacher_and_progress_is_derived()
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed(studentGroup: "real-group");
        await Publish(app, a);
        var second = new ProblemAssignment(Guid.NewGuid(), a.StudentId, a.CourseId, new(a.Content.CourseVersionId, a.Content.LessonId, a.Content.ProblemId));
        await using (var db = app.Open()) { db.Assignments.Add(second); await db.SaveChangesAsync(); }
        using var student = app.Client(ApiFactory.Student);
        using var teacher = app.Client(ApiFactory.Teacher, "Teacher");
        var course = await Get<CourseWorkspaceDto>(student, Course(a));
        Assert.Equal(ApiFactory.Student, course.Enrollment!.StudentId);
        Assert.Equal("real-group", course.Enrollment.GroupId);
        Assert.Null(course.Enrollment.Id); // C2 enrollment has composite identity, no fabricated UUID.
        var teacherCourse = await Get<CourseWorkspaceDto>(teacher, Course(a));
        Assert.Null(teacherCourse.Enrollment);
        Assert.Equal(ApiFactory.Teacher, teacherCourse.TeacherContext!.TeacherId);
        var s = await Submit(app, a);
        Assert.Equal("InProgress", (await Get<LessonWorkspaceDto>(student, Lesson(a))).Progress.Status);
        await Review(app, s, true);
        var lesson = await Get<LessonWorkspaceDto>(student, Lesson(a));
        Assert.Equal(50, lesson.Progress.Percent);
        Assert.Equal(1, lesson.Progress.AcceptedProblems);
        Assert.Equal(2, lesson.Progress.TotalAssignedProblems);
        Assert.Equal(0, (await Get<CourseWorkspaceDto>(student, Course(a))).Progress.CompletedLessons);
        var secondSubmission = await Submit(app, second);
        await Review(app, secondSubmission, true);
        Assert.Equal("Completed", (await Get<LessonWorkspaceDto>(student, Lesson(a))).Progress.Status);
        course = await Get<CourseWorkspaceDto>(student, Course(a));
        Assert.Equal(1, course.Progress.CompletedLessons);
        Assert.Equal(100, course.Progress.Percent);
    }

    [Fact]
    public async Task Zero_assignments_and_zero_lessons_have_safe_progress()
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed(active: false);
        await Publish(app, a);
        using var student = app.Client(ApiFactory.Student);
        var lesson = await Get<LessonWorkspaceDto>(student, Lesson(a));
        Assert.Equal("NotStarted", lesson.Progress.Status);
        Assert.Equal(0, lesson.Progress.Percent);
        Assert.Empty(lesson.Problems);
        await using (var db = app.Open()) { await db.Lessons.ExecuteDeleteAsync(); }
        var course = await Get<CourseWorkspaceDto>(student, Course(a));
        Assert.Empty(course.Lessons);
        Assert.Equal(0, course.Progress.Percent);
        Assert.Equal(0, course.Progress.CompletedLessons);
    }

    [Fact]
    public async Task Current_submission_history_review_linkage_and_permissions_follow_full_flow()
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed();
        await Publish(app, a);
        using var student = app.Client(ApiFactory.Student);
        using var teacher = app.Client(ApiFactory.Teacher, "Teacher");
        var first = await Submit(app, a);
        var studentDto = await Get<StudentProblemWorkspaceDto>(student, Problem(a.Id));
        Assert.False(studentDto.Permissions.CanSubmit);
        Assert.True(studentDto.Permissions.CanPostMessage);
        var revision = await Review(app, first);
        studentDto = await Get<StudentProblemWorkspaceDto>(student, Problem(a.Id));
        Assert.True(studentDto.Permissions.CanSubmit);
        Assert.Equal(revision.MessageId, studentDto.Work.LatestTeacherFeedback!.Id);
        Assert.False((await Get<ProblemWorkDetailsDto>(teacher, $"/api/v1/problem-works/{first.ProblemWorkId}")).Permissions.CanAccept);
        var second = await Submit(app, a, revision.Version, "Second solution");
        var dto = await Get<ProblemWorkDetailsDto>(teacher, $"/api/v1/problem-works/{first.ProblemWorkId}");
        Assert.Equal(second.SubmissionId, dto.CurrentSubmission!.Id);
        Assert.Equal(new[] { 2, 1 }, dto.Submissions.Select(s => s.AttemptNumber));
        Assert.Equal("First solution", dto.Submissions[1].BodyMarkdown);
        Assert.Equal(first.SubmissionId, Assert.Single(dto.Reviews).SubmissionId);
        Assert.Equal(revision.MessageId, Assert.Single(dto.Messages).Id);
        Assert.True(dto.Permissions.CanAccept);
        Assert.True(dto.Permissions.CanRequestRevision);
        await Review(app, second, true);
        studentDto = await Get<StudentProblemWorkspaceDto>(student, Problem(a.Id));
        Assert.False(studentDto.Permissions.CanSubmit);
        Assert.True(studentDto.Permissions.CanPostMessage);
        Assert.Null(studentDto.Work.LatestTeacherFeedback); // Latest accept had no feedback.
        dto = await Get<ProblemWorkDetailsDto>(teacher, $"/api/v1/problem-works/{first.ProblemWorkId}");
        Assert.False(dto.Permissions.CanAccept);
        Assert.False(dto.Permissions.CanRequestRevision);
        Assert.Equal(new[] { second.SubmissionId, first.SubmissionId }, dto.Reviews.Select(r => r.SubmissionId));
    }

    [Fact]
    public async Task Queue_filters_search_totals_and_summary_use_full_filtered_dataset_before_paging()
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed(studentGroup: "G");
        await Publish(app, a, title: "Special Algebra");
        var first = await Submit(app, a);
        var assignments = new List<ProblemAssignment>();
        await using (var db = app.Open())
        {
            for (var i = 0; i < 4; i++)
            {
                var item = new ProblemAssignment(Guid.NewGuid(), a.StudentId, a.CourseId, new(a.Content.CourseVersionId, a.Content.LessonId, a.Content.ProblemId));
                assignments.Add(item); db.Assignments.Add(item);
            }
            await db.SaveChangesAsync();
        }
        var revision1 = await Submit(app, assignments[0]);
        var review1 = await Review(app, revision1);
        revision1 = await Submit(app, assignments[0], review1.Version);
        var revision2 = await Submit(app, assignments[1]);
        var review2 = await Review(app, revision2);
        revision2 = await Submit(app, assignments[1], review2.Version);
        var accepted = await Submit(app, assignments[2]);
        await Review(app, accepted, true);
        var needsRevision = await Submit(app, assignments[3]);
        await Review(app, needsRevision);
        using var teacher = app.Client(ApiFactory.Teacher, "Teacher");
        const string route = "/api/v1/teacher/review-queue";
        var all = await Get<ReviewQueueResponseDto>(teacher, route + "?pageSize=1");
        Assert.Equal(3, all.Total);
        Assert.Equal(3, all.Summary.Total);
        Assert.Equal(1, all.Summary.FirstAttempts);
        Assert.Equal(2, all.Summary.Revisions);
        Assert.Equal(3, Assert.Single(all.Summary.ByCourse).Count);
        Assert.NotNull(all.Summary.AverageWaitingSeconds);
        Assert.Single(all.Items);
        var revision = await Get<ReviewQueueResponseDto>(teacher, route + "?attemptKind=revision&pageSize=1&page=2");
        Assert.Equal(2, revision.Total);
        Assert.Equal(0, revision.Summary.FirstAttempts);
        Assert.Equal(2, Assert.Single(revision.Items).AttemptNumber);
        var firstPage = await Get<ReviewQueueResponseDto>(teacher, route + "?attemptKind=first&pageSize=1");
        Assert.Equal(first.SubmissionId, Assert.Single(firstPage.Items).SubmissionId);
        Assert.Equal(1, firstPage.Total);
        foreach (var search in new[] { "sPeCiAl", "Remainders", "Divisibility", a.StudentId.ToString() })
        {
            var searched = await Get<ReviewQueueResponseDto>(teacher, route + "?search=" + search + "&pageSize=1");
            Assert.Equal(3, searched.Total);
            Assert.Equal(3, searched.Summary.Total);
        }
        var combined = await Get<ReviewQueueResponseDto>(teacher, route + $"?courseId={a.CourseId}&groupId=G&studentId={a.StudentId}&attemptKind=revision&search=Algebra&pageSize=1");
        Assert.Equal(2, combined.Total);
        foreach (var filter in new[] { "search=absent", "search=%25", "groupId=wrong", "courseId=wrong", "studentId=" + Guid.NewGuid() })
        {
            var empty = await Get<ReviewQueueResponseDto>(teacher, route + "?" + filter);
            Assert.Empty(empty.Items);
            Assert.Equal(0, empty.Total);
            Assert.Empty(empty.Summary.ByCourse);
            Assert.Null(empty.Summary.AverageWaitingSeconds);
        }
        var beyond = await Get<ReviewQueueResponseDto>(teacher, route + "?page=99&pageSize=1");
        Assert.Empty(beyond.Items);
        Assert.Equal(3, beyond.Summary.Total);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=201")]
    [InlineData("attemptKind=other")]
    [InlineData("page=2147483647&pageSize=200")]
    public async Task Queue_rejects_invalid_query(string query)
    {
        await using var app = await postgres.CreateApp();
        using var teacher = app.Client(ApiFactory.Teacher, "Teacher");
        await Error(teacher, "/api/v1/teacher/review-queue?" + query, 400);
    }

    [Fact]
    public async Task All_queries_require_authentication_and_teacher_queue_rejects_student()
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed();
        using var anonymous = app.Client();
        foreach (var route in new[] { Problem(a.Id), Course(a), Lesson(a), "/api/v1/problem-works/" + Guid.NewGuid(), "/api/v1/teacher/review-queue" })
            await Error(anonymous, route, 401);
        using var student = app.Client(ApiFactory.Student);
        await Error(student, "/api/v1/teacher/review-queue", 403);
    }
    [Fact]
    public async Task Course_counts_completed_over_available_lessons_and_excludes_withdrawn_assignments()
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed();
        await using (var db = app.Open())
        {
            db.ContentVersions.Add(new PublishedContentVersion { Id = a.Content.CourseVersionId, CourseId = a.CourseId,
                VersionNumber = 1, SourceRevision = "two-lessons", PublishedAt = DateTimeOffset.UtcNow,
                ContentJson = JsonSerializer.Serialize(new PublishedCourseContent("Course", null, [
                    new("lesson", 1, "One", null, [new("problem", 1, "Task", "Statement")]),
                    new("second", 2, "Two", null, []),
                    new("locked", 3, "Locked", "SECRET", [new("locked-problem", 1, "Locked", "SECRET")])]), ApiFactory.Json) });
            db.Lessons.AddRange(new LessonAccess { CourseId = a.CourseId, CourseVersionId = a.Content.CourseVersionId, LessonId = "second", IsPublished = true },
                new LessonAccess { CourseId = a.CourseId, CourseVersionId = a.Content.CourseVersionId, LessonId = "locked", IsPublished = false });
            var withdrawn = new ProblemAssignment(Guid.NewGuid(), a.StudentId, a.CourseId, new(a.Content.CourseVersionId, "lesson", "problem"));
            withdrawn.Withdraw();
            db.Assignments.Add(withdrawn);
            await db.SaveChangesAsync();
        }
        await Review(app, await Submit(app, a), true);
        using var student = app.Client(ApiFactory.Student);
        var course = await Get<CourseWorkspaceDto>(student, Course(a));
        Assert.Equal(3, course.Progress.TotalLessons);
        Assert.Equal(2, course.Progress.AvailableLessons);
        Assert.Equal(1, course.Progress.CompletedLessons);
        Assert.Equal(50, course.Progress.Percent);
        Assert.Equal(1, course.Lessons.Single(l => l.Lesson.Id == "lesson").Progress.TotalAssignedProblems);
        Assert.DoesNotContain("SECRET", await student.GetStringAsync(Course(a)));
    }

    [Fact]
    public async Task Inactive_group_membership_revokes_teacher_reads_and_queue()
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed(teacherGroup: "A", studentGroup: "A");
        await Publish(app, a);
        var submission = await Submit(app, a);
        await using (var db = app.Open())
        {
            (await db.Enrollments.SingleAsync()).IsActive = false;
            await db.SaveChangesAsync();
        }
        using var teacher = app.Client(ApiFactory.Teacher, "Teacher");
        await Error(teacher, $"/api/v1/problem-works/{submission.ProblemWorkId}", 403);
        var queue = await Get<ReviewQueueResponseDto>(teacher, "/api/v1/teacher/review-queue");
        Assert.Equal(0, queue.Summary.Total);
        Assert.Empty((await Get<LessonWorkspaceDto>(teacher, Lesson(a))).Problems);
    }

    [Fact]
    public async Task Attachments_and_discussion_are_projected_without_fake_storage_metadata()
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed();
        await Publish(app, a);
        var attachment = await app.Attachment(ApiFactory.Student);
        using var student = app.Client(ApiFactory.Student);
        using var teacher = app.Client(ApiFactory.Teacher, "Teacher");
        var response = await student.PostAsJsonAsync($"/api/v1/problem-assignments/{a.Id}/submissions",
            new { bodyMarkdown = "Body with attachment", attachmentIds = new[] { attachment } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var submission = (await response.Content.ReadFromJsonAsync<SubmissionResult>(ApiFactory.Json))!;
        var review = await Review(app, submission);
        var messageAttachment = await app.Attachment(ApiFactory.Student);
        response = await student.PostAsJsonAsync($"/api/v1/problem-works/{submission.ProblemWorkId}/messages",
            new { bodyMarkdown = "Student reply", replyToMessageId = review.MessageId, attachmentIds = new[] { messageAttachment } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reply = (await response.Content.ReadFromJsonAsync<MessageResult>(ApiFactory.Json))!;
        var dto = await Get<ProblemWorkDetailsDto>(teacher, $"/api/v1/problem-works/{submission.ProblemWorkId}");
        Assert.Equal(attachment, Assert.Single(dto.CurrentSubmission!.AttachmentIds));
        Assert.Equal(new[] { review.MessageId!.Value, reply.MessageId }, dto.Messages.Select(m => m.Id));
        Assert.Equal(review.MessageId, dto.Messages[1].ReplyToMessageId);
        Assert.Equal(messageAttachment, Assert.Single(dto.Messages[1].AttachmentIds));
        Assert.Equal(ProblemWorkStatus.NeedsRevision, dto.Status);
        Assert.Equal(1, dto.AttemptCount);
    }

    [Fact]
    public async Task Unavailable_version_is_excluded_when_another_version_of_lesson_is_open()
    {
        await using var app = await postgres.CreateApp();
        var old = await app.Seed();
        await Publish(app, old);
        var locked = new ProblemAssignment(Guid.NewGuid(), old.StudentId, old.CourseId, new(Guid.NewGuid(), "lesson", "problem"));
        await Publish(app, locked, 2, "SECRET NEW STATEMENT");
        await using (var db = app.Open())
        {
            db.Assignments.Add(locked);
            db.Lessons.Add(new LessonAccess { CourseId = old.CourseId, CourseVersionId = locked.Content.CourseVersionId,
                LessonId = "lesson", IsPublished = false });
            await db.SaveChangesAsync();
        }
        using var student = app.Client(ApiFactory.Student);
        var lesson = await Get<LessonWorkspaceDto>(student, Lesson(old));
        Assert.Equal(old.Id, Assert.Single(lesson.Problems).ProblemAssignmentId);
        Assert.DoesNotContain("SECRET", await student.GetStringAsync(Lesson(old)));
        await Error(student, Problem(locked.Id), 403);
        var course = await Get<CourseWorkspaceDto>(student, Course(old));
        Assert.Equal(1, course.Progress.AvailableLessons);
        Assert.Equal(1, Assert.Single(course.Lessons).Progress.TotalAssignedProblems);
        using var teacher = app.Client(ApiFactory.Teacher, "Teacher");
        var teacherLesson = await Get<LessonWorkspaceDto>(teacher, Lesson(old));
        var teacherCourse = await Get<CourseWorkspaceDto>(teacher, Course(old));
        Assert.Equal(2, teacherLesson.Progress.TotalAssignedProblems);
        Assert.Equal(teacherLesson.Progress, Assert.Single(teacherCourse.Lessons).Progress);
    }

}
