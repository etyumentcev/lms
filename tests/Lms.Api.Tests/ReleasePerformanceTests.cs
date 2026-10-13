using System.Data.Common;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Api.Tests;

public sealed partial class ContractApiTests
{
    // Real PostgreSQL and HTTP projection. Timings are evidence, never flaky pass thresholds.
    [Fact]
    public async Task Release_queue_1000_is_paginated_with_constant_query_count()
    {
        await using var app = await postgres.CreateApp(); var template = await app.Seed(); await Publish(app, template);
        await using (var db = app.Open())
        {
            for (var i = 0; i < 1000; i++)
            {
                var a = new ProblemAssignment(Guid.NewGuid(), Guid.NewGuid(), template.CourseId,
                    new(template.Content.CourseVersionId, template.Content.LessonId, template.Content.ProblemId));
                db.Assignments.Add(a);
                db.Enrollments.Add(new EnrollmentAccess { StudentId = a.StudentId, CourseId = a.CourseId, IsActive = true });
                var w = new ProblemWork(Guid.NewGuid(), a); w.SubmitSolution(a, a.StudentId, "Queue fixture", [], DateTimeOffset.UtcNow);
                db.Works.Add(w);
            }
            // Trusted fixture provisioning; no production SQL or invalid state transitions.
            await db.SaveChangesAsync();
        }
        var counter = new ReleaseQueryCounter();
        await using var measured = app.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.ConfigureDbContext<LmsDbContext>(o => o.AddInterceptors(counter))));
        using var teacher = measured.CreateClient(); teacher.DefaultRequestHeaders.Add("X-Test-Subject", ApiFactory.Teacher.ToString());
        teacher.DefaultRequestHeaders.Add("X-Test-Role", "Teacher");
        var rows = new List<object>();
        foreach (var size in new[] { 1, 50, 200 })
        {
            var samples = new List<double>(); int bytes = 0;
            for (var n = 0; n < 21; n++)
            {
                counter.Count = 0; var watch = Stopwatch.StartNew();
                var response = await teacher.GetAsync(QueuePath + $"?page=2&pageSize={size}");
                var body = await response.Content.ReadAsByteArrayAsync(); watch.Stop();
                response.EnsureSuccessStatusCode(); var json = JsonDocument.Parse(body).RootElement;
                Assert.Equal(1000, json.GetProperty("total").GetInt32()); Assert.Equal(size, json.GetProperty("items").GetArrayLength());
                Assert.Equal(5, counter.Count); bytes = body.Length;
                if (n > 0) samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            samples.Sort(); rows.Add(new { size, samples = 20, p50Ms = samples[9], p95Ms = samples[18], bytes, queries = counter.Count });
        }
        // An adjacent projection audit: measure (do not hide) the per-assignment Lesson N+1.
        counter.Count = 0; var lessonWatch = Stopwatch.StartNew();
        var lessonResponse = await teacher.GetAsync(LessonPath(template));
        var lessonBytes = await lessonResponse.Content.ReadAsByteArrayAsync(); lessonWatch.Stop();
        lessonResponse.EnsureSuccessStatusCode();
        rows.Add(new { profile = "teacherLesson1001Assignments", ms = lessonWatch.Elapsed.TotalMilliseconds,
            bytes = lessonBytes.Length, queries = counter.Count });
        var output = Environment.GetEnvironmentVariable("LMS_K_PERFORMANCE_OUTPUT");
        if (output is not null) await File.WriteAllTextAsync(output, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal sealed class ReleaseQueryCounter : DbCommandInterceptor
{
    public int Count;
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { Interlocked.Increment(ref Count); return ValueTask.FromResult(result); }
}
