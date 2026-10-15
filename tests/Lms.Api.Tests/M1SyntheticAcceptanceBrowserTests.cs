using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Lms.Application;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Lms.Api.Tests;

public sealed class M1SyntheticAcceptanceBrowserTests(ApiPostgresFixture fixture) : IClassFixture<ApiPostgresFixture>
{
    private sealed class AcceptanceClock : TimeProvider, IQualifiedClockSource
    {
        private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
        private readonly long timestamp = Stopwatch.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => started + Stopwatch.GetElapsedTime(timestamp);
        public QualifiedClockReading Read()
        {
            var elapsed = Stopwatch.GetElapsedTime(timestamp);
            var now = started + elapsed;
            return new("m1-11-acceptance", true, new(now, now), elapsed, elapsed);
        }
    }

    private sealed record Participant(string UserName, string DisplayName, string RosterKey, string Role);

    [BrowserReadTheory]
    [InlineData(true)]
    public async Task Clean_React_journey_bootstraps_to_Accepted_without_user_entity_SQL(bool browser)
    {
        Assert.True(browser);
        await using var database = await fixture.CreateApp(testAuthentication: false, organizationBootstrap: true);
        await using var databaseContext = database.Open();
        var connectionString = databaseContext.Database.GetConnectionString()!;
        var root = FindRoot();
        var web = Path.Combine(root, "src/Lms.Web/dist");
        Assert.True(File.Exists(Path.Combine(web, "index.html")), "Build the production React UI first");

        var evidence = Environment.GetEnvironmentVariable("LMS_M1_11_EVIDENCE")
            ?? Path.Combine(root, "codex/evidence/M1-11");
        var screenshots = Path.Combine(evidence, "screenshots");
        var journeyEvidence = Path.Combine(evidence, "journey");
        var materialsEvidence = Path.Combine(evidence, "materials");
        var photoEvidence = Path.Combine(evidence, "photos");
        var reviewEvidence = Path.Combine(evidence, "review");
        var progressEvidence = Path.Combine(evidence, "progress");
        foreach (var path in new[] { evidence, screenshots, journeyEvidence, materialsEvidence, photoEvidence,
                     reviewEvidence, progressEvidence }) Directory.CreateDirectory(path);

        using var tls = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", tls, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        certificateRequest.CertificateExtensions.Add(san.Build());
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(2));
        var port = FreePort();
        var origin = $"https://127.0.0.1:{port}";
        var time = new AcceptanceClock();
        var authorityClock = new QualifiedAuthorityClock(time, TimeSpan.Zero, TimeSpan.Zero);
        using var componentKey = RSA.Create(2048);
        var componentJwt = new ComponentJwt(componentKey, "m1-11-browser", "m1-11-browser", authorityClock);
        var platformKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.LocalSigningKey));
        await using var app = BuildApp(connectionString, database.StorageRoot, root, web, origin, port, certificate,
            time, authorityClock, componentJwt, platformKey);
        await app.StartAsync();

        using (var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true })
        using (var probe = new HttpClient(handler) { BaseAddress = new Uri(origin) })
        {
            using var health = await probe.GetAsync("/api/health");
            Assert.True(health.IsSuccessStatusCode);
            var index = await probe.GetStringAsync("/");
            Assert.Contains("id=\"root\"", index);
        }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var adminUser = $"m111-admin-{suffix}";
        var foreignAdminUser = $"m111-admin-b-{suffix}";
        var teacherUser = $"m111-ta-{suffix}";
        var otherTeacherUser = $"m111-tx-{suffix}";
        const string adminPassword = "Synthetic-M1-11-Admin-2039!";
        const string participantPassword = "Synthetic-M1-11-Participant-2039!";
        const string organizationName = "Сапиентум — Демокружок A";
        const string foreignOrganizationName = "Сапиентум — Демокружок B";
        const string courseTitle = "Подготовка к олимпиаде Эйлера";
        var students = Enumerable.Range(1, 12).Select(index => new Participant(
            $"m111-p{index:00}-{suffix}", $"P{index:00}", $"m111-p{index:00}-{suffix}", "Student")).ToArray();
        var participants = students.Concat([
            new Participant(teacherUser, "T-A", $"m111-ta-{suffix}", "Teacher"),
            new Participant(otherTeacherUser, "T-X", $"m111-tx-{suffix}", "Teacher")
        ]).ToArray();

        var now = DateTime.UtcNow;
        var platformToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            "m1-11-platform", OrganizationBootstrapHttp.PlatformAudience,
            [new Claim("sub", "synthetic-platform-operator"),
             new Claim("scope", OrganizationBootstrapHttp.BootstrapCapability)],
            now, now.AddMinutes(30), new SigningCredentials(platformKey, SecurityAlgorithms.HmacSha256)));
        var setupResult = await RunNode(root, "tools/browser_read/m1-11-setup.mjs", new
        {
            origin, platformToken, organizationName, foreignOrganizationName, adminUser, foreignAdminUser,
            adminPassword, participantPassword, teacherUser, teacherDisplayName = "T-A",
            unassignedTeacherDisplayName = "T-X", courseTitle, participants, screenshots
        }, TimeSpan.FromMinutes(10), [platformToken, adminPassword, participantPassword]);
        using var setupJson = JsonDocument.Parse(setupResult);
        Assert.Equal("PASS", setupJson.RootElement.GetProperty("status").GetString());
        var organizationId = setupJson.RootElement.GetProperty("organizationId").GetGuid();
        var foreignOrganizationId = setupJson.RootElement.GetProperty("foreignOrganizationId").GetGuid();
        string courseId;
        string groupId;
        int processingContexts;
        int courseContextBindings;
        await using (var setupOracle = database.Open())
        {
            Assert.Equal(2, await setupOracle.Set<OrganizationProfile>().CountAsync());
            Assert.Equal(14, await setupOracle.Set<OrganizationParticipantProfile>()
                .CountAsync(x => x.OrganizationId == organizationId));
            Assert.Equal(16, await setupOracle.Set<ActorIdentity>().CountAsync(x => x.IsActive));
            Assert.Empty(await setupOracle.Assignments.ToListAsync());
            var course = await setupOracle.Set<CourseCatalogRecord>().SingleAsync(x => x.Title == courseTitle);
            courseId = course.CourseId;
            groupId = (await setupOracle.Set<CourseGroupCatalogRecord>()
                .SingleAsync(x => x.CourseId == courseId && x.Name == "G1")).GroupId;
            Assert.Equal(12, await setupOracle.Set<EnrollmentScope>()
                .CountAsync(x => x.CourseId == courseId && x.GroupId == groupId && x.State == "Active"));
            Assert.Single(await setupOracle.Set<TeacherAssignmentScope>()
                .Where(x => x.CourseId == courseId && x.GroupId == groupId && x.State == "Active").ToListAsync());
            Assert.Single(await setupOracle.Set<CourseEditorScope>()
                .Where(x => x.OrganizationId == organizationId && x.CourseId == courseId && x.State == "Active").ToListAsync());
            processingContexts = await setupOracle.Set<ProcessingContextVersion>()
                .CountAsync(x => x.OrganizationId == organizationId && x.SyntheticOnly && x.ApprovalState == "SyntheticOnly");
            courseContextBindings = await setupOracle.Set<CourseContextBinding>()
                .CountAsync(x => x.OrganizationId == organizationId && x.CourseId == courseId);
            Assert.Equal(1, processingContexts);
            Assert.Equal(1, courseContextBindings);
        }

        await File.WriteAllTextAsync(Path.Combine(evidence, "E03-authority-oracle.json"), JsonSerializer.Serialize(new
        {
            courseId, groupId, courseEditorScopes = 1, teacherGroupScopes = 1,
            processingContexts, courseContextBindings
        }, new JsonSerializerOptions { WriteIndented = true }));

        var photoOne = Path.Combine(root, "tests/fixtures/photos/a4-1.png");
        var photoTwo = Path.Combine(root, "tests/fixtures/photos/a4-2.png");
        var photoOneHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(photoOne))).ToLowerInvariant();
        var photoTwoHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(photoTwo))).ToLowerInvariant();
        await RunNode(root, "tools/browser_read/content-publication.mjs", new
        {
            origin, organizationId, teacherUser, studentUser = students[0].UserName,
            password = participantPassword, courseTitle, evidence = journeyEvidence, materialsEvidence,
            photoEvidence, m1Evidence = reviewEvidence, progressEvidence, courseId, groupId,
            photoOne, photoTwo, photoOneHash, photoTwoHash,
            invalidZip = Path.Combine(root, "tests/fixtures/content/m1-06-invalid.zip"),
            validZip = Path.Combine(root, "tests/fixtures/content/m1-06-olympiad.zip"),
            versionTwoZip = Path.Combine(root, "tests/fixtures/content/m1-06-olympiad-v2.zip")
        }, TimeSpan.FromMinutes(7), [participantPassword]);

        async Task<HttpClient> Login(string userName)
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            var client = new HttpClient(handler) { BaseAddress = new Uri(origin) };
            var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = JsonContent.Create(new { userName, password = participantPassword, organizationId })
            };
            request.Headers.TryAddWithoutValidation("Origin", origin);
            request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var session = await response.Content.ReadFromJsonAsync<JsonElement>();
            client.DefaultRequestHeaders.Authorization = new("Bearer",
                session.GetProperty("accessToken").GetString());
            client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", origin);
            var authenticatedCsrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-CSRF-TOKEN",
                authenticatedCsrf.GetProperty("token").GetString());
            return client;
        }

        async Task<(Guid WorkId, Guid SubmissionId, long Version)> Submit(string userName, int problemIndex)
        {
            using var student = await Login(userName);
            var catalog = await student.GetFromJsonAsync<JsonElement>($"/api/v1/my/courses/{courseId}/assignments");
            var assignmentId = catalog.GetProperty("items")[problemIndex].GetProperty("assignmentId").GetGuid();
            using var response = await student.PostAsJsonAsync($"/api/v1/problem-assignments/{assignmentId}/submissions",
                new { bodyMarkdown = "Synthetic aggregate acceptance", attachmentIds = Array.Empty<Guid>(), expectedVersion = (long?)null });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            return (result.GetProperty("problemWorkId").GetGuid(),
                result.GetProperty("submissionId").GetGuid(), result.GetProperty("version").GetInt64());
        }

        var p01Revision = await Submit(students[0].UserName, 1);
        var p01Submitted = await Submit(students[0].UserName, 2);
        var p02Accepted = await Submit(students[1].UserName, 0);
        var p03Revision = await Submit(students[2].UserName, 0);
        var p04Submitted = await Submit(students[3].UserName, 0);
        using (var teacher = await Login(teacherUser))
        {
            async Task Review((Guid WorkId, Guid SubmissionId, long Version) target, string decision)
            {
                using var response = await teacher.PostAsJsonAsync(
                    $"/api/v1/problem-works/{target.WorkId}/reviews/{decision}", new
                    {
                        commandId = Guid.NewGuid(), target.SubmissionId, expectedVersion = target.Version,
                        feedback = decision == "accept" ? "Принято" : "Требуется доработка",
                        annotations = Array.Empty<object>()
                    });
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            await Review(p01Revision, "needs-revision");
            await Review(p02Accepted, "accept");
            await Review(p03Revision, "needs-revision");
            var progress = await teacher.GetFromJsonAsync<JsonElement>(
                $"/api/v1/teacher/group-progress?courseId={courseId}&groupId={groupId}");
            var summary = progress.GetProperty("summary");
            Assert.Equal(12, summary.GetProperty("studentCount").GetInt32());
            Assert.Equal(60, summary.GetProperty("assignedCount").GetInt32());
            Assert.Equal(54, summary.GetProperty("notStarted").GetInt32());
            Assert.Equal(2, summary.GetProperty("submitted").GetInt32());
            Assert.Equal(2, summary.GetProperty("needsRevision").GetInt32());
            Assert.Equal(2, summary.GetProperty("accepted").GetInt32());
        }

        await using var verify = database.Open();
        Assert.Equal(2, await verify.ContentVersions.CountAsync(x => x.CourseId == courseId));
        var assignments = await verify.Assignments.Where(x => x.CourseId == courseId).ToListAsync();
        Assert.Equal(60, assignments.Count);
        Assert.Equal(12, assignments.Select(x => x.StudentId).Distinct().Count());
        var work = await verify.Works.Include(x => x.Submissions).Include(x => x.Reviews)
            .SingleAsync(x => x.Submissions.Count == 2);
        Assert.Equal(ProblemWorkStatus.Accepted, work.Status);
        Assert.Equal(2, work.Submissions.Count);
        Assert.Equal(2, work.Reviews.Count);
        var revision = work.Reviews.Single(x => x.Decision == ReviewDecision.NeedsRevision);
        Assert.Equal(2, revision.Annotations.Count);
        Assert.All(revision.Annotations, annotation => Assert.Single(annotation.Strokes));
        var privateRoot = Path.Combine(database.StorageRoot, "m1-07-attachments");
        var protectedFiles = new List<object>();
        foreach (var submission in work.Submissions.OrderBy(x => x.AttemptNumber))
        foreach (var attachmentId in submission.AttachmentIds)
        {
            var stored = await verify.StoredAttachments.SingleAsync(x => x.Id == attachmentId);
            var bytes = await File.ReadAllBytesAsync(Path.Combine(privateRoot, stored.StorageKey));
            protectedFiles.Add(new
            {
                submission.AttemptNumber, attachmentId, stored.FileName, stored.Size,
                sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
            });
        }
        Assert.Equal(4, protectedFiles.Count);
        Assert.DoesNotContain(verify.ContentArtifacts.Where(x => x.ContentVersionId == assignments[0].Content.CourseVersionId)
            .Single().StudentArtifactJson, "M1_TEACHER_ONLY");
        Assert.NotEqual(organizationId, foreignOrganizationId);

        await File.WriteAllTextAsync(Path.Combine(evidence, "environment.json"), JsonSerializer.Serialize(new
        {
            head = "688d8cf3305d7739a3cb76824a60ad9d5de3596b",
            transport = "HTTPS", browser = "Chromium", database = "PostgreSQL 17.6",
            productionReactBundle = true, privateStorage = true, syntheticOnly = true
        }, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.Combine(evidence, "e2e-journey.json"), JsonSerializer.Serialize(new
        {
            passed = true, organizations = 2, students = 12, teachers = 2, assignments = 60,
            statusJourney = new[] { "NotStarted", "Submitted", "NeedsRevision", "Submitted", "Accepted" },
            submissions = 2, reviews = 2, annotations = 2,
            progress = new { accepted = 2, needsRevision = 2, submitted = 2, notStarted = 54 }, protectedFiles
        }, new JsonSerializerOptions { WriteIndented = true }));
        await app.StopAsync();
        await verify.DisposeAsync();

        async Task<string> Readback(string targetConnection, string targetStorage, string stage,
            bool expectMissingFiles = false)
        {
            await using var readApp = BuildApp(targetConnection, targetStorage, root, web, origin, port,
                certificate, time, authorityClock, componentJwt, platformKey);
            await readApp.StartAsync();
            try
            {
                var output = await RunNode(root, "tools/browser_read/m1-11-readback.mjs", new
                {
                    origin, organizationId, teacherUser, otherTeacherUser,
                    studentUser = students[0].UserName, password = participantPassword,
                    courseId, groupId, assignmentId = work.ProblemAssignmentId, workId = work.Id,
                    files = protectedFiles, evidence = Path.Combine(evidence, "backup-restore"),
                    stage, expectMissingFiles
                }, TimeSpan.FromMinutes(3), [participantPassword]);
                using var result = JsonDocument.Parse(output);
                Assert.Equal(expectMissingFiles ? "EXPECTED_FAILURE" : "PASS",
                    result.RootElement.GetProperty("status").GetString());
                await File.WriteAllTextAsync(Path.Combine(evidence, "backup-restore", stage + ".json"),
                    JsonSerializer.Serialize(result.RootElement, new JsonSerializerOptions { WriteIndented = true }));
                return output;
            }
            finally
            {
                await readApp.StopAsync();
            }
        }

        Directory.CreateDirectory(Path.Combine(evidence, "backup-restore"));
        await Readback(connectionString, database.StorageRoot, "api-restart");
        var restartedConnection = await fixture.RestartPostgres(connectionString);
        Npgsql.NpgsqlConnection.ClearAllPools();
        await Readback(restartedConnection, database.StorageRoot, "postgres-restart");

        var backupEvidence = Path.Combine(evidence, "backup-restore");
        var captureStartedAtUtc = TimeProvider.System.GetUtcNow();
        string[] migrations;
        await using (var migrationDb = new LmsDbContext(new DbContextOptionsBuilder<LmsDbContext>()
            .UseNpgsql(restartedConnection).Options))
            migrations = (await migrationDb.Database.GetAppliedMigrationsAsync()).ToArray();

        var backup = await fixture.Backup(restartedConnection);
        var backupId = $"m1-11-evidence-fix-{backup.StartedAtUtc:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}";
        var backupPayloadRoot = Path.Combine(backupEvidence, "backup-set");
        var dumpRoot = Path.Combine(backupPayloadRoot, "postgresql");
        var snapshotRoot = Path.Combine(backupPayloadRoot, "private-files");
        Directory.CreateDirectory(dumpRoot);
        Directory.CreateDirectory(snapshotRoot);
        var dumpPath = Path.Combine(dumpRoot, "database.dump");
        await File.WriteAllBytesAsync(dumpPath, backup.Content);
        Assert.Equal(backup.Size, new FileInfo(dumpPath).Length);
        Assert.Equal(backup.Sha256,
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(dumpPath))).ToLowerInvariant());

        foreach (var directory in Directory.GetDirectories(database.StorageRoot, "*", SearchOption.AllDirectories))
        {
            Assert.False(new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint));
            Directory.CreateDirectory(Path.Combine(snapshotRoot,
                Path.GetRelativePath(database.StorageRoot, directory)));
        }
        foreach (var file in Directory.GetFiles(database.StorageRoot, "*", SearchOption.AllDirectories))
        {
            Assert.False(new FileInfo(file).Attributes.HasFlag(FileAttributes.ReparsePoint));
            var destination = Path.Combine(snapshotRoot, Path.GetRelativePath(database.StorageRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        var captureCompletedAtUtc = TimeProvider.System.GetUtcNow();

        var sourceManifest = Directory.GetFiles(snapshotRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => path).Select(path => new
            {
                path = Path.GetRelativePath(snapshotRoot, path).Replace(Path.DirectorySeparatorChar, '/'),
                size = new FileInfo(path).Length,
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()
            }).ToArray();
        var privateManifestRecords = string.Concat(sourceManifest.OrderBy(item => item.path)
            .Select(item => $"{item.sha256}  {item.path}\n"));
        var privateAggregateSha256 = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(privateManifestRecords))).ToLowerInvariant();

        static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("O");
        var manifestPath = Path.Combine(backupEvidence, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            backupId,
            syntheticOnly = true,
            build = new
            {
                head = "688d8cf3305d7739a3cb76824a60ad9d5de3596b",
                evidencePurpose = "M1-11-EVIDENCE-FIX"
            },
            capture = new
            {
                startedAt = Utc(captureStartedAtUtc),
                writersQuiescedAt = Utc(captureStartedAtUtc),
                databaseDumpCompletedAt = Utc(backup.CompletedAtUtc),
                privateFilesCompletedAt = Utc(captureCompletedAtUtc),
                completedAt = Utc(captureCompletedAtUtc),
                timeZone = "UTC",
                timeSource = "TimeProvider.System.GetUtcNow"
            },
            migrations,
            postgresqlBackup = new
            {
                identifier = $"{backup.Database}:{backup.Sha256}",
                database = backup.Database,
                relativePath = "backup-set/postgresql/database.dump",
                format = "custom",
                tool = "pg_dump from postgres:17.6-alpine",
                sha256 = backup.Sha256,
                size = backup.Size
            },
            privateFilesSnapshot = new
            {
                identifier = $"private-files:{privateAggregateSha256}",
                relativeRoot = "backup-set/private-files",
                fileCount = sourceManifest.Length,
                totalBytes = sourceManifest.Sum(item => item.size),
                aggregateSha256 = privateAggregateSha256,
                files = sourceManifest
            },
            consistency = new
            {
                writersQuiesced = true,
                method = "All API instances were stopped before pg_dump; PostgreSQL remained online; pg_dump -Fc completed before a no-symlink recursive copy of the unchanged private root; the restored DB and copied snapshot were then checked together by DB, HTTP authorization, history/progress and original-byte SHA-256 oracles.",
                sourceMarker = new
                {
                    workId = work.Id,
                    state = work.Status.ToString(),
                    submissions = work.Submissions.Count,
                    reviews = work.Reviews.Count,
                    assignments = assignments.Count,
                    originals = sourceManifest.Length
                }
            },
            commands = new
            {
                pgDump = backup.Command,
                privateFilesSnapshot = "recursive copy of the complete private storage root; reject ReparsePoint; preserve relative paths and bytes"
            }
        }, new JsonSerializerOptions { WriteIndented = true }));

        var manifestSha256 = Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(manifestPath))).ToLowerInvariant();
        await File.WriteAllTextAsync(Path.Combine(backupEvidence, "backup-set.sha256"),
            $"{manifestSha256}  manifest.json\n{backup.Sha256}  backup-set/postgresql/database.dump\n" +
            $"{privateAggregateSha256}  backup-set/private-files (aggregate records)\n");

        var restoreStorage = Path.Combine(Path.GetTempPath(), "m1-11-restore-" + Guid.NewGuid().ToString("N"));
        var missingStorage = Path.Combine(Path.GetTempPath(), "m1-11-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(restoreStorage);
        Directory.CreateDirectory(missingStorage);
        foreach (var directory in Directory.GetDirectories(snapshotRoot, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(restoreStorage, Path.GetRelativePath(snapshotRoot, directory)));
        foreach (var file in Directory.GetFiles(snapshotRoot, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(restoreStorage, Path.GetRelativePath(snapshotRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        var restoredManifest = Directory.GetFiles(restoreStorage, "*", SearchOption.AllDirectories)
            .OrderBy(path => path).Select(path => new
            {
                path = Path.GetRelativePath(restoreStorage, path).Replace(Path.DirectorySeparatorChar, '/'),
                size = new FileInfo(path).Length,
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()
            }).ToArray();
        Assert.Equal(JsonSerializer.Serialize(sourceManifest), JsonSerializer.Serialize(restoredManifest));

        var restored = await fixture.Restore(backup.Path);
        var restoredConnection = restored.ConnectionString;
        await using (var restoredDb = new LmsDbContext(new DbContextOptionsBuilder<LmsDbContext>()
            .UseNpgsql(restoredConnection).Options))
        {
            Assert.Equal(migrations, (await restoredDb.Database.GetAppliedMigrationsAsync()).ToArray());
            Assert.Equal(2, await restoredDb.ContentVersions.CountAsync(x => x.CourseId == courseId));
            Assert.Equal(60, await restoredDb.Assignments.CountAsync(x => x.CourseId == courseId));
            var restoredWork = await restoredDb.Works.Include(x => x.Submissions).Include(x => x.Reviews)
                .SingleAsync(x => x.Id == work.Id);
            Assert.Equal(ProblemWorkStatus.Accepted, restoredWork.Status);
            Assert.Equal(2, restoredWork.Submissions.Count);
            Assert.Equal(2, restoredWork.Reviews.Count);
            Assert.Equal(2, restoredWork.Reviews.Single(x => x.Decision == ReviewDecision.NeedsRevision)
                .Annotations.Count);
        }
        await Readback(restoredConnection, restoreStorage, "restore");
        await File.WriteAllTextAsync(Path.Combine(backupEvidence, "full-restore-drill.json"),
            JsonSerializer.Serialize(new
            {
                scenario = "A-full-db-private-files",
                input = new
                {
                    backupId,
                    manifest = "manifest.json",
                    manifestSha256,
                    databaseSha256 = backup.Sha256,
                    privateFilesAggregateSha256 = privateAggregateSha256,
                    migrations
                },
                expected = "pg_restore succeeds into a different empty database; private files restore into a different empty root; DB/history/progress/authorization and all protected byte hashes match.",
                commands = new[]
                {
                    new { command = restored.CreateCommand, exitCode = restored.CreateExitCode,
                        stdout = restored.CreateStdout, stderr = restored.CreateStderr },
                    new { command = restored.RestoreCommand, exitCode = restored.RestoreExitCode,
                        stdout = restored.RestoreStdout, stderr = restored.RestoreStderr }
                },
                actual = new
                {
                    database = restored.Database,
                    restoredPrivateFiles = restoredManifest.Length,
                    restoredPrivateBytes = restoredManifest.Sum(item => item.size),
                    dbOracle = "2 versions; 60 assignments; Accepted; 2 submissions; 2 reviews; 2 annotations",
                    httpOracle = "restore.json PASS; student bytes/history and teacher progress; T-X denied"
                },
                sourceDatabase = backup.Database,
                targetDatabaseDifferent = backup.Database != restored.Database,
                targetPrivateRootDifferent = database.StorageRoot != restoreStorage,
                checksumManifestMatched = JsonSerializer.Serialize(sourceManifest) == JsonSerializer.Serialize(restoredManifest),
                hiddenDataLossDetected = false,
                partialSuccessDetected = false,
                status = "PASS",
                startedAt = Utc(restored.StartedAtUtc),
                completedAt = Utc(restored.CompletedAtUtc)
            }, new JsonSerializerOptions { WriteIndented = true }));

        await Readback(restoredConnection, missingStorage, "restore-db-only-negative", expectMissingFiles: true);
        Directory.Delete(restoreStorage, recursive: true);
        Directory.Delete(missingStorage, recursive: true);
    }

    private static WebApplication BuildApp(string connectionString, string storageRoot, string root, string web,
        string origin, int port, X509Certificate2 certificate, AcceptanceClock time,
        QualifiedAuthorityClock authorityClock, ComponentJwt componentJwt, SecurityKey platformKey)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, port,
            listen => listen.UseHttps(certificate)));
        builder.Services.AddSingleton(time);
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddSingleton(authorityClock);
        builder.Services.AddSingleton<IAuthorityClock>(authorityClock);
        builder.Services.AddSingleton(componentJwt);
        builder.Services.AddDbContextFactory<LmsDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<LmsDbContext>>().CreateDbContext());
        builder.Services.AddSingleton(new OrganizationBootstrapOptions
        {
            ChallengeKey = Enumerable.Range(31, 32).Select(x => (byte)x).ToArray(), KeyVersion = 1
        });
        builder.Services.AddScoped<IOrganizationBootstrap, PostgresOrganizationBootstrap>();
        builder.Services.AddScoped<IOrganizationAdministrationQueries, PostgresOrganizationAdministrationQueries>();
        builder.Services.AddScoped<PostgresAccountAdministration>();
        builder.Services.AddScoped<IAccountAdministration>(sp => sp.GetRequiredService<PostgresAccountAdministration>());
        builder.Services.AddScoped<IAccountSecurityOperations>(sp => sp.GetRequiredService<PostgresAccountAdministration>());
        builder.Services.AddScoped<ICourseRosterAdministration, PostgresCourseRosterAdministration>();
        builder.Services.AddScoped<IProcessingContextAdministration, PostgresProcessingContextAdministration>();
        builder.Services.AddSingleton(new ContentImportRuntimeOptions
        {
            RepositoryRoot = root, PythonPath = "/usr/bin/python3",
            NodePath = "/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node",
            KatexPath = Path.Combine(root, "src/Lms.Web/node_modules/katex"),
            StorageRoot = Path.Combine(storageRoot, "m1-11-content-import"), MaxZipBytes = 20_000_000
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
        builder.Services.AddSingleton(new LocalAttachmentStorageOptions
            { Root = Path.Combine(storageRoot, "m1-07-attachments") });
        builder.Services.AddSingleton<IAttachmentStorage, LocalFileAttachmentStorage>();
        builder.Services.AddScoped<AttachmentService>();
        builder.Services.AddScoped<PostgresTeacherCommands>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddTeacherRevocationBrowserProtection();
        builder.Services.AddRateLimiter(options => options.AddPolicy(OrganizationBootstrapHttp.ActivationRatePolicy,
            _ => RateLimitPartition.GetFixedWindowLimiter("m1-11-browser", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 100, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        builder.Services.AddAuthentication(CourseBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, CourseBearerHandler>(CourseBearerHandler.SchemeName, _ => { })
            .AddJwtBearer(OrganizationBootstrapHttp.PlatformBearerScheme, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new()
                {
                    ValidIssuer = "m1-11-platform", ValidAudience = OrganizationBootstrapHttp.PlatformAudience,
                    IssuerSigningKey = platformKey, ValidateIssuer = true, ValidateAudience = true,
                    ValidateIssuerSigningKey = true, ValidateLifetime = true, RequireSignedTokens = true,
                    ClockSkew = TimeSpan.Zero
                };
            });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(QueriesController).Assembly)
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            });

        var app = builder.Build();
        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseMiddleware<CourseRequestIngress>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapGet("/api/health", async (LmsDbContext db, CancellationToken ct) =>
            await db.Database.CanConnectAsync(ct) ? Results.Ok() : Results.StatusCode(503));
        app.MapTeacherRevocation(origin);
        app.MapOrganizationBootstrap(origin);
        app.MapAccountAdministration(origin);
        app.MapCourseRoster(origin);
        app.MapProcessingContextAdministration(origin);
        app.MapContentPublication(origin, 20_000_000);
        app.MapProgressHistory();
        app.MapControllers();
        app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(web) });
        app.MapFallback(async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = 404;
                return;
            }
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(Path.Combine(web, "index.html"));
        });
        return app;
    }

    private static async Task<string> RunNode(string root, string script, object input, TimeSpan duration,
        IEnumerable<string> secrets)
    {
        var node = Environment.GetEnvironmentVariable("LMS_BROWSER_NODE") ?? "node";
        var start = new ProcessStartInfo(node)
        {
            WorkingDirectory = root, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false
        };
        var executable = Environment.GetEnvironmentVariable("LMS_BROWSER_EXECUTABLE");
        if (string.IsNullOrWhiteSpace(executable) && File.Exists("/opt/google/chrome/chrome"))
            executable = "/opt/google/chrome/chrome";
        if (!string.IsNullOrWhiteSpace(executable)) start.Environment["LMS_BROWSER_EXECUTABLE"] = executable;
        start.ArgumentList.Add(Path.Combine(root, script));
        using var runner = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(duration);
        var errors = runner.StandardError.ReadToEndAsync(timeout.Token);
        await runner.StandardInput.WriteLineAsync(JsonSerializer.Serialize(input,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var output = await runner.StandardOutput.ReadLineAsync(timeout.Token) ?? string.Empty;
        await runner.WaitForExitAsync(timeout.Token);
        var diagnostic = await errors;
        foreach (var secret in secrets) diagnostic = diagnostic.Replace(secret, "[REDACTED]");
        Assert.True(runner.ExitCode == 0, diagnostic);
        return output;
    }

    private static string FindRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Lms.sln"))) root = root.Parent;
        return Assert.IsType<DirectoryInfo>(root).FullName;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
