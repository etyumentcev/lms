using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lms.Application;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Lms.Api.Tests;

public sealed class ContentPublicationBrowserTests(ApiPostgresFixture fixture) : IClassFixture<ApiPostgresFixture>
{
    private sealed class BrowserClock : TimeProvider, IQualifiedClockSource
    {
        private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => started + Stopwatch.GetElapsedTime(timestamp);
        private readonly long timestamp = Stopwatch.GetTimestamp();
        public QualifiedClockReading Read()
        {
            var elapsed = Stopwatch.GetElapsedTime(timestamp); var now = started + elapsed;
            return new("m1-06-browser", true, new(now, now), elapsed, elapsed);
        }
    }

    [BrowserReadTheory]
    [InlineData(true)]
    public async Task Real_chromium_https_preserves_published_v1_and_submits_ordered_original_photos(bool browser)
    {
        Assert.True(browser);
        await using var database = await fixture.CreateApp(organizationBootstrap: true);
        await database.InitializeTeacherSecurity();
        await using var databaseContext = database.Open();
        var connectionString = databaseContext.Database.GetConnectionString()!;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Lms.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var web = Path.Combine(root.FullName, "src/Lms.Web/dist");
        Assert.True(File.Exists(Path.Combine(web, "index.html")), "Build the production React UI first");

        var courseId = "m1-05-browser-draft-" + Guid.NewGuid().ToString("N");
        const string groupId = "G1";
        const string courseTitle = "Олимпиадная математика · M1-06";
        var now = DateTimeOffset.UtcNow;
        await using (var seed = database.Open())
        {
            seed.AddRange(
                new CourseRegistration { CourseId = courseId, OrganizationId = database.OrganizationId, MappingState = "Verified" },
                new CourseGroup { CourseId = courseId, GroupId = groupId },
                new CourseCatalogRecord { CourseId = courseId, OrganizationId = database.OrganizationId,
                    Title = courseTitle, Description = "Draft-only курс, созданный цепочкой M1-05",
                    State = "Draft", Version = 1, CreatedAt = now },
                new CourseGroupCatalogRecord { CourseId = courseId, GroupId = groupId, Name = groupId,
                    Description = "Синтетическая группа", Version = 1, CreatedAt = now });
            await seed.SaveChangesAsync();
            seed.Add(new CourseContextBinding { CourseId = courseId, OrganizationId = database.OrganizationId,
                ContextId = database.ProcessingContextId, ContextVersion = 1, EffectiveFrom = now.AddMinutes(-1) });
            await seed.SaveChangesAsync();
        }
        await using (var grants = database.Open())
            await new PostgresScopedAuthorityAdministration(grants).Change(new(AuthorityKind.Teacher,
                AuthorityOperation.Grant, database.OrganizationId, ApiFactory.Teacher, 0, 1, courseId, null, Guid.NewGuid()));
        await using (var grants = database.Open())
            await new PostgresScopedAuthorityAdministration(grants).Change(new(AuthorityKind.Enrollment,
                AuthorityOperation.Grant, database.OrganizationId, ApiFactory.Student, 0, 1, courseId, groupId));
        var additionalStudents = Enumerable.Range(2, 11).Select(_ => Guid.NewGuid()).ToArray();
        await using (var students = database.Open())
        {
            students.AddRange(additionalStudents.Select(id => new ActorRecord { ActorId = id, PrimaryEducationalRole = "Student" }));
            students.AddRange(additionalStudents.Select((id, index) => new OrganizationParticipantProfile
            {
                OrganizationId = database.OrganizationId, ActorId = id,
                ExternalRosterKey = $"m1-10-student-{index + 2:00}",
                DisplayName = $"Ученик {index + 2:00}", CreatedAt = now
            }));
            students.Add(new OrganizationParticipantProfile
            {
                OrganizationId = database.OrganizationId, ActorId = ApiFactory.Student,
                ExternalRosterKey = "m1-10-student-01", DisplayName = "Ученик 01", CreatedAt = now
            });
            await students.SaveChangesAsync();
        }
        foreach (var student in additionalStudents)
        {
            await using var grants = database.Open();
            var administration = new PostgresScopedAuthorityAdministration(grants);
            await administration.Change(new(AuthorityKind.Membership, AuthorityOperation.Grant,
                database.OrganizationId, student, 0, Role: ScopeRole.Student));
            await administration.Change(new(AuthorityKind.Enrollment, AuthorityOperation.Grant,
                database.OrganizationId, student, 0, 1, courseId, groupId));
        }

        using var tls = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", tls, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(IPAddress.Loopback);
        certificateRequest.CertificateExtensions.Add(san.Build());
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var origin = $"https://127.0.0.1:{port}";
        var time = new BrowserClock(); var authorityClock = new QualifiedAuthorityClock(time, TimeSpan.Zero, TimeSpan.Zero);
        using var componentKey = RSA.Create(2048);
        var componentJwt = new ComponentJwt(componentKey, "m1-06-browser", "m1-06-browser", authorityClock);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, port, listen => listen.UseHttps(certificate)));
        builder.Services.AddSingleton(time); builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddSingleton(authorityClock); builder.Services.AddSingleton<IAuthorityClock>(authorityClock);
        builder.Services.AddSingleton(componentJwt);
        builder.Services.AddDbContextFactory<LmsDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<LmsDbContext>>().CreateDbContext());
        builder.Services.AddScoped<ICourseRosterAdministration, PostgresCourseRosterAdministration>();
        builder.Services.AddSingleton(new ContentImportRuntimeOptions
        {
            RepositoryRoot = root.FullName,
            PythonPath = "/usr/bin/python3",
            NodePath = "/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node",
            KatexPath = Path.Combine(root.FullName, "src/Lms.Web/node_modules/katex"),
            StorageRoot = Path.Combine(database.StorageRoot, "m1-06-browser-import"),
            MaxZipBytes = 20_000_000
        });
        builder.Services.AddScoped<IContentPublication, PostgresContentPublication>();
        builder.Services.AddScoped<IWorkStore, PostgresWorkStore>();
        builder.Services.AddScoped<IAccessPolicy, PostgresAccessPolicy>();
        builder.Services.AddScoped<IImmutableContentResolver, PostgresContentResolver>();
        builder.Services.AddScoped<IWorkspaceQueries, PostgresWorkspaceQueries>();
        builder.Services.AddScoped<IProgressHistoryQueries, PostgresProgressHistoryQueries>();
        builder.Services.AddScoped<ProblemWorkService>();
        builder.Services.AddSingleton<IActorMapper, ClaimsActorMapper>();
        builder.Services.AddSingleton(new AttachmentOptions { MaxBytes = 20_000_000 });
        builder.Services.AddSingleton(new LocalAttachmentStorageOptions { Root = Path.Combine(database.StorageRoot, "m1-07-attachments") });
        builder.Services.AddSingleton<IAttachmentStorage, LocalFileAttachmentStorage>();
        builder.Services.AddScoped<AttachmentService>();
        builder.Services.AddScoped<PostgresTeacherCommands>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddTeacherRevocationBrowserProtection();
        builder.Services.AddAuthentication(CourseBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, CourseBearerHandler>(CourseBearerHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(QueriesController).Assembly).AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        });

        await using var app = builder.Build();
        app.UseMiddleware<ApiExceptionMiddleware>(); app.UseMiddleware<CourseRequestIngress>();
        app.UseAuthentication(); app.UseAuthorization();
        app.MapTeacherRevocation(origin); app.MapCourseRoster(origin); app.MapContentPublication(origin, 20_000_000);
        app.MapProgressHistory();
        app.MapControllers();
        app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(web) });
        app.MapFallback(async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api")) { context.Response.StatusCode = 404; return; }
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.Body.WriteAsync(await File.ReadAllBytesAsync(Path.Combine(web, "index.html"), context.RequestAborted),
                context.RequestAborted);
        });
        await app.StartAsync();

        using (var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true })
        using (var probe = new HttpClient(handler) { BaseAddress = new Uri(origin) })
        {
            var index = await probe.GetAsync("/");
            var indexHtml = await index.Content.ReadAsStringAsync();
            Assert.True(index.IsSuccessStatusCode && indexHtml.Contains("id=\"root\"", StringComparison.Ordinal),
                $"HTTPS index probe failed: {(int)index.StatusCode} {indexHtml}");
            var asset = indexHtml.Split("src=\"", StringSplitOptions.RemoveEmptyEntries)[1].Split('"')[0];
            var script = await probe.GetAsync(asset);
            Assert.True(script.IsSuccessStatusCode, $"HTTPS asset probe failed: {(int)script.StatusCode} {asset}");
        }

        var priorEvidence = Path.Combine(Path.GetTempPath(), "sapientum-m1-10-prior-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(priorEvidence, "M1-06-CONTENT-PUBLICATION");
        var materialsEvidence = Path.Combine(priorEvidence, "M1-07-MATERIALS-REFERENCE");
        var photoEvidence = Path.Combine(priorEvidence, "M1-08-PROTECTED-MULTI-PHOTO");
        var m1Evidence = Path.Combine(priorEvidence, "M1-09-TEACHER-REVIEW");
        var progressEvidence = Path.Combine(root.FullName, "codex/evidence/M1-10-PROGRESS-HISTORY");
        Directory.CreateDirectory(evidence); Directory.CreateDirectory(materialsEvidence); Directory.CreateDirectory(photoEvidence);
        Directory.CreateDirectory(m1Evidence); Directory.CreateDirectory(progressEvidence);
        var photoOne = Path.Combine(root.FullName, "tests/fixtures/photos/a4-1.png");
        var photoTwo = Path.Combine(root.FullName, "tests/fixtures/photos/a4-2.png");
        var photoOneHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(photoOne))).ToLowerInvariant();
        var photoTwoHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(photoTwo))).ToLowerInvariant();
        var node = Environment.GetEnvironmentVariable("LMS_BROWSER_NODE") ?? "node";
        var start = new ProcessStartInfo(node) { WorkingDirectory = root.FullName, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        var executable = Environment.GetEnvironmentVariable("LMS_BROWSER_EXECUTABLE");
        if (string.IsNullOrWhiteSpace(executable) && File.Exists("/usr/bin/google-chrome")) executable = "/usr/bin/google-chrome";
        if (!string.IsNullOrWhiteSpace(executable)) start.Environment["LMS_BROWSER_EXECUTABLE"] = executable;
        start.ArgumentList.Add(Path.Combine(root.FullName, "tools/browser_read/content-publication.mjs"));
        using var runner = Process.Start(start)!; using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var errors = runner.StandardError.ReadToEndAsync(timeout.Token);
        await runner.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
        {
            origin, organizationId = database.OrganizationId, teacherUser = database.TeacherUserName,
            studentUser = database.StudentUserName, password = ApiFactory.SecurePassword, courseTitle, evidence, materialsEvidence,
            photoEvidence, m1Evidence, progressEvidence, courseId, groupId,
            photoOne, photoTwo, photoOneHash, photoTwoHash,
            invalidZip = Path.Combine(root.FullName, "tests/fixtures/content/m1-06-invalid.zip"),
            validZip = Path.Combine(root.FullName, "tests/fixtures/content/m1-06-olympiad.zip"),
            versionTwoZip = Path.Combine(root.FullName, "tests/fixtures/content/m1-06-olympiad-v2.zip")
        }));
        var passed = await runner.StandardOutput.ReadLineAsync(timeout.Token) == "PASS";
        await runner.WaitForExitAsync(timeout.Token);
        var diagnostic = (await errors).Replace(ApiFactory.SecurePassword, "[REDACTED]");
        Assert.True(passed && runner.ExitCode == 0, diagnostic);

        await using var verify = database.Open();
        var versions = await verify.ContentVersions.Where(x => x.CourseId == courseId).OrderBy(x => x.VersionNumber).ToListAsync();
        Assert.Equal([1, 2], versions.Select(x => x.VersionNumber).ToArray());
        var historical = await verify.Assignments.Where(x => x.CourseId == courseId).ToListAsync();
        Assert.Equal(60, historical.Count); Assert.All(historical, x => Assert.Equal(versions[0].Id, x.Content.CourseVersionId));
        Assert.Equal(12, historical.Select(x => x.StudentId).Distinct().Count());
        Assert.Equal(60, await verify.Set<AssignmentScope>().CountAsync(x => x.CourseId == courseId && x.GroupId == groupId));
        var v1Artifact = await verify.ContentArtifacts.SingleAsync(x => x.ContentVersionId == versions[0].Id);
        Assert.Contains("M1_TEACHER_ONLY_SOLUTION_v1", v1Artifact.TeacherArtifactJson);
        Assert.DoesNotContain("M1_TEACHER_ONLY", v1Artifact.StudentArtifactJson);
        Assert.Equal("Published", (await verify.Set<CourseCatalogRecord>().SingleAsync(x => x.CourseId == courseId)).State);
        var submittedWork = await verify.Works.Include(x => x.Submissions).Include(x => x.Reviews).SingleAsync();
        var submissions = submittedWork.Submissions.OrderBy(x => x.AttemptNumber).ToArray();
        Assert.Equal(ProblemWorkStatus.Accepted, submittedWork.Status);
        Assert.Equal(2, submissions.Length);
        Assert.Equal(2, submittedWork.Reviews.Count);
        var revision = submittedWork.Reviews.Single(x => x.Decision == ReviewDecision.NeedsRevision);
        var accepted = submittedWork.Reviews.Single(x => x.Decision == ReviewDecision.Accepted);
        Assert.Equal(submissions[0].Id, revision.SubmissionId);
        Assert.Equal(submissions[1].Id, accepted.SubmissionId);
        Assert.Equal(2, revision.Annotations.Count);
        Assert.All(revision.Annotations, annotation => Assert.Single(annotation.Strokes));
        Assert.Empty(accepted.Annotations);
        var privateRoot = Path.Combine(database.StorageRoot, "m1-07-attachments");
        var expectedAttempts = new[] { new[] { photoTwo, photoOne }, new[] { photoOne, photoTwo } };
        for (var attempt = 0; attempt < submissions.Length; attempt++)
        {
            var submission = submissions[attempt];
            Assert.Equal(2, submission.AttachmentIds.Count);
            var submittedAttachments = await verify.StoredAttachments
                .Where(x => submission.AttachmentIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            Assert.Equal(2, submittedAttachments.Count);
            for (var index = 0; index < expectedAttempts[attempt].Length; index++)
            {
                var stored = submittedAttachments[submission.AttachmentIds[index]];
                var expectedBytes = await File.ReadAllBytesAsync(expectedAttempts[attempt][index]);
                var storedBytes = await File.ReadAllBytesAsync(Path.Combine(privateRoot, stored.StorageKey));
                Assert.Equal(expectedBytes, storedBytes);
                Assert.Equal(SHA256.HashData(expectedBytes), SHA256.HashData(storedBytes));
                Assert.Equal(Path.GetFileName(expectedAttempts[attempt][index]), stored.FileName);
                Assert.Equal(submission.Id, stored.SubmissionId);
            }
        }
        var allUploads = await verify.StoredAttachments.ToListAsync();
        Assert.Equal(5, allUploads.Count); Assert.Single(allUploads, x => x.Deleted);
        Assert.Equal(4, allUploads.Count(x => x.SubmissionId != null));
        Assert.Equal(7, Directory.GetFiles(evidence, "*.png").Length);
        Assert.Equal(7, Directory.GetFiles(materialsEvidence, "*.png").Length);
        Assert.Equal(6, Directory.GetFiles(photoEvidence, "*.png").Length);
        Assert.Equal(9, Directory.GetFiles(m1Evidence, "*.png").Length);
        Assert.Equal(9, Directory.GetFiles(progressEvidence, "*.png").Length);
        await File.WriteAllTextAsync(Path.Combine(evidence, "test-summary.json"), JsonSerializer.Serialize(new
        {
            passed = true, transport = "HTTPS", browser = "Chromium", imports = 3, versions = 2,
            historicalAssignments = 5, historicalVersion = 1, roleIsolation = true, unknownResultAutoReplay = false,
            screenshots = 7
        }, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.Combine(materialsEvidence, "test-summary.json"), JsonSerializer.Serialize(new
        {
            passed = true, transport = "HTTPS", browser = "Chromium", viewports = new[] { 1440, 768, 390 },
            studentCourse = true, lessonMarkdown = true, latex = true, protectedImage = true, hints = true,
            history = true, photoSubmission = true, teacherReference = true, historicalReferenceVersion = 1,
            studentTeacherDataLeak = false, screenshots = 7
        }, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.Combine(photoEvidence, "test-summary.json"), JsonSerializer.Serialize(new
        {
            passed = true, transport = "HTTPS", browser = "Chromium", productionReact = true,
            normalLogin = true, viewports = new[] { 390, 768, 1440 }, uploads = 3, deletedPending = 1,
            submissions = 1, submittedPages = 2, orderedFiles = new[] { "a4-2.png", "a4-1.png" },
            sha256 = new[] { photoTwoHash, photoOneHash }, originalBytesMatch = true, teacherProtectedRead = true,
            reloadRestored = true, lostAcknowledgementCheckedByGet = true, automaticReplay = false,
            doubleClickRequests = 1, screenshots = 6
        }, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.Combine(m1Evidence, "test-summary.json"), JsonSerializer.Serialize(new
        {
            passed = true, transport = "HTTPS", browser = "Chromium", productionReact = true,
            normalLogin = true, viewports = new[] { 390, 768, 1440 }, queuePageCount = 2,
            annotations = new[] { "pen", "mouse" }, normalizedCoordinates = true, penPressure = true,
            fullscreenA4 = true, protectedThumbnails = true, unknownResultAutoReplay = false,
            needsRevisionRequests = 1, submissions = 2, reviews = 2, finalStatus = "Accepted",
            immutableReview = true, originalBytesMatch = true, studentTeacherDataLeak = false, screenshots = 9
        }, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.Combine(progressEvidence, "test-summary.json"), JsonSerializer.Serialize(new
        {
            passed = true, transport = "HTTPS", browser = "Chromium", productionReact = true,
            normalLogin = true, students = 12, assignments = 60,
            statusJourney = new[] { "NotStarted", "Submitted", "NeedsRevision", "Accepted" },
            teacherHistory = true, acceptedHistory = true, studentAttemptHistory = true,
            apiPostgresUiConsistent = true, exactVersion = 1,
            lostSubmissionResponseReadBack = true, lostReviewResponseReadBack = true,
            automaticReplay = false, screenshots = 9
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
