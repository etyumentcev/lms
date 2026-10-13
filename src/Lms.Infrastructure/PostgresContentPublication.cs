using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lms.Infrastructure;

public sealed class PostgresContentPublication(LmsDbContext db, IAuthorityClock clock,
    ContentImportRuntimeOptions options) : IContentPublication
{
    private static readonly JsonSerializerOptions Json = PostgresContentResolver.Json;

    public async Task<ContentImportResult> Import(TokenAuthority token, string courseId, Stream zip, long length,
        string mediaType, string idempotencyKey, string sourceRevision, CancellationToken ct = default)
    {
        courseId = Required(courseId, 1, 80); idempotencyKey = Required(idempotencyKey, 1, 128);
        sourceRevision = Required(sourceRevision, 1, 128);
        if (mediaType is not ("application/zip" or "application/x-zip-compressed") || length <= 0 ||
            length > options.MaxZipBytes) throw new UseCaseException("ValidationFailed");
        var resource = TeacherResource(token, courseId, null, wholeCourse: true);
        await EnsureRead(resource, token, ct);

        if (!OperatingSystem.IsLinux()) throw new UseCaseException("ContentUnavailable");
        options.Validate(); Directory.CreateDirectory(options.StorageRoot);
        var stageDirectory = Path.Combine(options.StorageRoot, "incoming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stageDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var zipPath = Path.Combine(stageDirectory, "input.zip");
        string requestDigest;
        try
        {
            requestDigest = await CopyZip(zip, zipPath, length, options.MaxZipBytes, ct);
            var now = Now(); var importId = Guid.NewGuid();
            await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
            {
                await Processing(resource, token, ct); await Advisory(token.Identity.ActorId, idempotencyKey, ct);
                var prior = await db.ContentImports.SingleOrDefaultAsync(x => x.OwnerActorId == token.Identity.ActorId &&
                    x.IdempotencyKey == idempotencyKey, ct);
                if (prior != null)
                {
                    if (prior.CourseId != courseId || prior.RequestDigest != requestDigest || prior.SourceRevision != sourceRevision)
                        throw new UseCaseException("IdempotencyConflict");
                    await tx.CommitAsync(ct); return Result(prior);
                }
                if (!await db.Set<CourseCatalogRecord>().AnyAsync(x => x.CourseId == courseId &&
                    x.OrganizationId == resource.OrganizationId, ct)) throw new UseCaseException("NotFound");
                var attempt = new ContentImportAttempt
                {
                    ImportId = importId, OrganizationId = resource.OrganizationId, CourseId = courseId,
                    OwnerActorId = token.Identity.ActorId, IdempotencyKey = idempotencyKey,
                    RequestDigest = requestDigest, SourceRevision = sourceRevision, State = "Validating", Version = 1,
                    CreatedAt = now, ExpiresAt = now.AddHours(24), DiagnosticsJson = "[]"
                };
                db.Add(attempt); db.Add(Audit(resource.OrganizationId, token, "content_import_started", importId.ToString(), courseId));
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); ct.ThrowIfCancellationRequested();
            }

            db.ChangeTracker.Clear();
            ImportExecution execution;
            try { execution = await new ContentImportRunner(options).Run(zipPath, sourceRevision, ct); }
            catch (OperationCanceledException) { throw; }
            catch { execution = FailedExecution("I_IMPORT"); }

            await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
            {
                await Processing(resource, token, ct);
                var attempt = await db.ContentImports.FromSqlInterpolated(
                    $"SELECT * FROM \"ContentImportAttempts\" WHERE \"ImportId\"={importId} FOR UPDATE").SingleAsync(ct);
                if (attempt.State != "Validating" || attempt.Version != 1) throw new UseCaseException("ConcurrencyConflict");
                var diagnostics = Diagnostics(execution.Diagnostics);
                attempt.DiagnosticsJson = JsonSerializer.Serialize(diagnostics, Json);
                attempt.State = execution.Status switch { "Ready" => "Ready", "Invalid" => "Invalid", _ => "Failed" };
                attempt.Version = 2;
                if (attempt.State == "Ready")
                {
                    if (execution.Student == null || execution.Teacher == null || execution.ReceiptJson == null)
                        throw new UseCaseException("ContentImportFailed");
                    var receipt = JsonDocument.Parse(execution.ReceiptJson).RootElement;
                    attempt.StudentArtifactJson = execution.Student.Json; attempt.TeacherArtifactJson = execution.Teacher.Json;
                    attempt.ReceiptJson = execution.ReceiptJson;
                    attempt.StudentProjectionHash = Text(receipt, "studentProjectionHash");
                    attempt.PackageHash = Text(receipt, "packageHash"); attempt.TeacherArtifactHash = Text(receipt, "teacherArtifactHash");
                    foreach (var asset in Assets(importId, "student", execution.Student)) db.Add(asset);
                    foreach (var asset in Assets(importId, "teacher", execution.Teacher)) db.Add(asset);
                }
                db.Add(Audit(resource.OrganizationId, token, "content_import_" + attempt.State.ToLowerInvariant(),
                    importId.ToString(), courseId));
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); ct.ThrowIfCancellationRequested();
                return Result(attempt);
            }
        }
        finally
        {
            try { if (Directory.Exists(stageDirectory)) Directory.Delete(stageDirectory, true); } catch { }
        }
    }

    public async Task<ContentImportResult?> ImportStatus(TokenAuthority token, string courseId, Guid importId,
        CancellationToken ct = default)
    {
        var resource = TeacherResource(token, Required(courseId, 1, 80), null, true); await EnsureRead(resource, token, ct);
        var row = await db.ContentImports.AsNoTracking().SingleOrDefaultAsync(x => x.ImportId == importId &&
            x.CourseId == courseId && x.OrganizationId == resource.OrganizationId && x.OwnerActorId == token.Identity.ActorId, ct);
        return row == null ? null : Result(row);
    }

    public async Task<ContentPreview> PreviewImport(TokenAuthority token, string courseId, Guid importId,
        string audience, CancellationToken ct = default)
    {
        audience = Audience(audience); var resource = TeacherResource(token, Required(courseId, 1, 80), null, true);
        await EnsureRead(resource, token, ct);
        var row = await db.ContentImports.AsNoTracking().SingleOrDefaultAsync(x => x.ImportId == importId &&
            x.CourseId == courseId && x.OrganizationId == resource.OrganizationId && x.OwnerActorId == token.Identity.ActorId, ct)
            ?? throw new UseCaseException("NotFound");
        if (row.State != "Ready" || row.ExpiresAt <= Now()) throw new UseCaseException("ContentUnavailable");
        return new(audience, audience == "student" ? row.StudentArtifactJson! : row.TeacherArtifactJson!);
    }

    public async Task<ContentAsset> PreviewAsset(TokenAuthority token, string courseId, Guid importId,
        string audience, string assetPath, CancellationToken ct = default)
    {
        audience = Audience(audience); assetPath = AssetPath(assetPath);
        var resource = TeacherResource(token, Required(courseId, 1, 80), null, true); await EnsureRead(resource, token, ct);
        var valid = await db.ContentImports.AsNoTracking().AnyAsync(x => x.ImportId == importId && x.CourseId == courseId &&
            x.OrganizationId == resource.OrganizationId && x.OwnerActorId == token.Identity.ActorId && x.State == "Ready" && x.ExpiresAt > Now(), ct);
        if (!valid) throw new UseCaseException("NotFound");
        var row = await db.ContentImportAssets.AsNoTracking().SingleOrDefaultAsync(x => x.ImportId == importId &&
            x.Audience == audience && x.AssetPath == assetPath, ct) ?? throw new UseCaseException("NotFound");
        return new(row.MediaType, row.Bytes, row.Sha256);
    }

    public async Task<ContentPublicationResult> Publish(TokenAuthority token, string courseId, Guid importId,
        long expectedImportVersion, long expectedCourseVersion, string idempotencyKey, CancellationToken ct = default)
    {
        courseId = Required(courseId, 1, 80); idempotencyKey = Required(idempotencyKey, 1, 128);
        var resource = TeacherResource(token, courseId, null, true);
        var digest = Digest($"{courseId}\n{importId}\n{expectedImportVersion}\n{expectedCourseVersion}");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await Processing(resource, token, ct); await Advisory(token.Identity.ActorId, idempotencyKey, ct);
        var prior = await Receipt<ContentPublicationResult>(token, idempotencyKey, "Publish", digest, ct);
        if (prior != null) { await tx.CommitAsync(ct); return prior with { Created = false }; }
        var course = await db.Set<CourseCatalogRecord>().FromSqlInterpolated(
            $"SELECT * FROM \"CourseCatalog\" WHERE \"CourseId\"={courseId} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw new UseCaseException("NotFound");
        if (course.OrganizationId != resource.OrganizationId || course.Version != expectedCourseVersion)
            throw new UseCaseException("ConcurrencyConflict");
        var attempt = await db.ContentImports.FromSqlInterpolated(
            $"SELECT * FROM \"ContentImportAttempts\" WHERE \"ImportId\"={importId} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw new UseCaseException("NotFound");
        if (attempt.OrganizationId != resource.OrganizationId || attempt.CourseId != courseId ||
            attempt.OwnerActorId != token.Identity.ActorId) throw new UseCaseException("NotFound");
        if (attempt.Version != expectedImportVersion || attempt.State != "Ready" || attempt.ExpiresAt <= Now())
            throw new UseCaseException("ConcurrencyConflict");
        if (attempt.PublishedVersionId != null) throw new UseCaseException("ConcurrencyConflict");
        var content = Published(attempt.StudentArtifactJson!);
        var versionNumber = (await db.ContentVersions.Where(x => x.CourseId == courseId)
            .MaxAsync(x => (int?)x.VersionNumber, ct) ?? 0) + 1;
        var versionId = Guid.NewGuid(); var now = Now();
        db.Add(new PublishedContentVersion { Id = versionId, CourseId = courseId, VersionNumber = versionNumber,
            SourceRevision = "import:" + importId.ToString("N") + ":" + attempt.PackageHash, PublishedAt = now,
            ContentJson = JsonSerializer.Serialize(content, Json) });
        db.Add(new PublishedContentArtifact { ContentVersionId = versionId, ImportId = importId,
            StudentArtifactJson = attempt.StudentArtifactJson!, TeacherArtifactJson = attempt.TeacherArtifactJson!,
            StudentProjectionHash = attempt.StudentProjectionHash!, PackageHash = attempt.PackageHash!,
            TeacherArtifactHash = attempt.TeacherArtifactHash! });
        foreach (var source in await db.ContentImportAssets.AsNoTracking().Where(x => x.ImportId == importId).ToListAsync(ct))
            db.Add(new PublishedContentAsset { ContentVersionId = versionId, Audience = source.Audience,
                AssetPath = source.AssetPath, MediaType = source.MediaType, Sha256 = source.Sha256, Bytes = source.Bytes });
        attempt.PublishedVersionId = versionId; attempt.Version++;
        course.State = "Published"; course.Version++;
        var result = new ContentPublicationResult(versionId, versionNumber, course.Version, true);
        AddReceipt(token, idempotencyKey, courseId, "Publish", digest, result);
        db.Add(Audit(resource.OrganizationId, token, "content_published", versionId.ToString(), courseId));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); ct.ThrowIfCancellationRequested(); return result;
    }

    public async Task<ContentAssignmentResult> OpenAndAssign(TokenAuthority token, string courseId,
        Guid contentVersionId, string lessonId, string groupId, IReadOnlyList<string> problemIds,
        long expectedCourseVersion, string idempotencyKey, CancellationToken ct = default)
    {
        courseId = Required(courseId, 1, 80); lessonId = Required(lessonId, 1, 100); groupId = Required(groupId, 1, 100);
        idempotencyKey = Required(idempotencyKey, 1, 128);
        var ids = problemIds.Select(x => Required(x, 1, 100)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (ids.Length != 5) throw new UseCaseException("ValidationFailed");
        var resource = TeacherResource(token, courseId, groupId, false);
        var digest = Digest($"{courseId}\n{contentVersionId}\n{lessonId}\n{groupId}\n{string.Join(',', ids)}\n{expectedCourseVersion}");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await Processing(resource, token, ct); await Advisory(token.Identity.ActorId, idempotencyKey, ct);
        var prior = await Receipt<ContentAssignmentResult>(token, idempotencyKey, "OpenAndAssign", digest, ct);
        if (prior != null) { await tx.CommitAsync(ct); return prior with { Created = false }; }
        var course = await db.Set<CourseCatalogRecord>().FromSqlInterpolated(
            $"SELECT * FROM \"CourseCatalog\" WHERE \"CourseId\"={courseId} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw new UseCaseException("NotFound");
        if (course.OrganizationId != resource.OrganizationId || course.State != "Published" || course.Version != expectedCourseVersion)
            throw new UseCaseException("ConcurrencyConflict");
        if (!await db.Set<CourseGroup>().AnyAsync(x => x.CourseId == courseId && x.GroupId == groupId, ct))
            throw new UseCaseException("NotFound");
        var version = await db.ContentVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == contentVersionId && x.CourseId == courseId, ct)
            ?? throw new UseCaseException("NotFound");
        var content = JsonSerializer.Deserialize<PublishedCourseContent>(version.ContentJson, Json)
            ?? throw new UseCaseException("ContentUnavailable");
        var lesson = content.Lessons.SingleOrDefault(x => x.Id == lessonId) ?? throw new UseCaseException("NotFound");
        if (lesson.Problems.Where(x => ids.Contains(x.Id)).Select(x => x.Id).Distinct().Count() != 5)
            throw new UseCaseException("ValidationFailed");
        var students = await db.Set<EnrollmentScope>().FromSqlInterpolated($"SELECT * FROM \"EnrollmentScopes\" WHERE \"OrganizationId\"={resource.OrganizationId} AND \"CourseId\"={courseId} AND \"GroupId\"={groupId} AND \"State\"='Active' ORDER BY \"StudentId\" FOR UPDATE").ToListAsync(ct);
        if (students.Count == 0) throw new UseCaseException("ValidationFailed");
        var opening = await db.LessonGroupOpenings.SingleOrDefaultAsync(x => x.CourseId == courseId && x.GroupId == groupId &&
            x.ContentVersionId == contentVersionId && x.LessonId == lessonId, ct);
        if (opening == null) db.Add(new LessonGroupOpening { OrganizationId = resource.OrganizationId, CourseId = courseId,
            GroupId = groupId, ContentVersionId = contentVersionId, LessonId = lessonId, OpenedAt = Now(), OpenedBy = token.Identity.ActorId });
        var studentIds = students.Select(x => x.StudentId).ToArray();
        var existing = await db.Assignments.Where(x => studentIds.Contains(x.StudentId) && x.CourseId == courseId &&
            x.Content.CourseVersionId == contentVersionId && x.Content.LessonId == lessonId && ids.Contains(x.Content.ProblemId))
            .Select(x => new { x.StudentId, x.Content.ProblemId }).ToListAsync(ct);
        var seen = existing.Select(x => (x.StudentId, x.ProblemId)).ToHashSet(); var created = 0;
        foreach (var student in students)
            foreach (var problem in lesson.Problems.Where(x => ids.Contains(x.Id)).OrderBy(x => x.Order))
                if (seen.Add((student.StudentId, problem.Id)))
                { db.Add(new ProblemAssignment(Guid.NewGuid(), student.StudentId, courseId,
                    new ProblemContentReference(contentVersionId, lessonId, problem.Id))); created++; }
        var result = new ContentAssignmentResult(contentVersionId, lessonId, groupId, 5, students.Count, created, true);
        AddReceipt(token, idempotencyKey, courseId, "OpenAndAssign", digest, result);
        db.Add(Audit(resource.OrganizationId, token, "lesson_opened_and_assigned", contentVersionId.ToString(), courseId));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); ct.ThrowIfCancellationRequested(); return result;
    }

    public async Task<IReadOnlyList<StudentAssignmentItem>> StudentAssignments(TokenAuthority token, string courseId,
        CancellationToken ct = default)
    {
        courseId = Required(courseId, 1, 80); var resource = StudentResource(token, courseId); await EnsureRead(resource, token, ct);
        var openings = db.LessonGroupOpenings.AsNoTracking().Where(x => x.OrganizationId == resource.OrganizationId &&
            x.CourseId == courseId && x.GroupId == resource.GroupId);
        var rows = await (from a in db.Assignments.AsNoTracking()
            join v in db.ContentVersions.AsNoTracking() on a.Content.CourseVersionId equals v.Id
            join o in openings on new { a.CourseId, ContentVersionId = a.Content.CourseVersionId, LessonId = a.Content.LessonId }
                equals new { o.CourseId, o.ContentVersionId, o.LessonId }
            where a.StudentId == token.Identity.ActorId && a.CourseId == courseId && a.Status == AssignmentStatus.Active
            orderby v.VersionNumber, a.Content.LessonId, a.Content.ProblemId
            select new { a.Id, a.Content.CourseVersionId, v.VersionNumber, a.Content.LessonId, a.Content.ProblemId, v.ContentJson }).ToListAsync(ct);
        var result = new List<StudentAssignmentItem>();
        foreach (var row in rows)
        {
            var content = JsonSerializer.Deserialize<PublishedCourseContent>(row.ContentJson, Json)!;
            var lesson = content.Lessons.Single(x => x.Id == row.LessonId); var problem = lesson.Problems.Single(x => x.Id == row.ProblemId);
            result.Add(new(row.Id, row.CourseVersionId, row.VersionNumber, row.LessonId, lesson.Title,
                row.ProblemId, problem.Title, problem.Order));
        }
        return result;
    }

    public async Task<ContentPreview> StudentPublishedContent(TokenAuthority token, string courseId,
        Guid contentVersionId, CancellationToken ct = default)
    {
        var resource = StudentResource(token, Required(courseId, 1, 80)); await EnsureRead(resource, token, ct);
        var allowed = await (from a in db.Assignments.AsNoTracking()
            join o in db.LessonGroupOpenings.AsNoTracking() on new { a.CourseId, ContentVersionId = a.Content.CourseVersionId,
                LessonId = a.Content.LessonId, GroupId = resource.GroupId! } equals new { o.CourseId, o.ContentVersionId, o.LessonId, o.GroupId }
            where a.StudentId == token.Identity.ActorId && a.CourseId == courseId && a.Content.CourseVersionId == contentVersionId
            select new { a.Content.LessonId, a.Content.ProblemId }).Distinct().ToListAsync(ct);
        if (allowed.Count == 0) throw new UseCaseException("NotFound");
        var artifact = await db.ContentArtifacts.AsNoTracking().SingleOrDefaultAsync(x => x.ContentVersionId == contentVersionId, ct)
            ?? throw new UseCaseException("NotFound");
        var lessons = allowed.Select(x => x.LessonId).ToHashSet(StringComparer.Ordinal);
        var problems = allowed.Select(x => x.ProblemId).ToHashSet(StringComparer.Ordinal);
        var root = JsonNode.Parse(artifact.StudentArtifactJson)?.AsObject() ?? throw new UseCaseException("ContentUnavailable");
        var courseLessons = root["course"]?["lessons"]?.AsArray() ?? throw new UseCaseException("ContentUnavailable");
        for (var index = courseLessons.Count - 1; index >= 0; index--)
        {
            var lesson = courseLessons[index]?.AsObject() ?? throw new UseCaseException("ContentUnavailable");
            var lessonId = lesson["id"]?.GetValue<string>() ?? throw new UseCaseException("ContentUnavailable");
            if (!lessons.Contains(lessonId)) { courseLessons.RemoveAt(index); continue; }
            var lessonProblems = lesson["problems"]?.AsArray() ?? throw new UseCaseException("ContentUnavailable");
            for (var problemIndex = lessonProblems.Count - 1; problemIndex >= 0; problemIndex--)
            {
                var problemId = lessonProblems[problemIndex]?["id"]?.GetValue<string>();
                if (problemId == null || !problems.Contains(problemId)) lessonProblems.RemoveAt(problemIndex);
            }
        }
        if (root["assets"] is JsonArray assets)
            for (var index = assets.Count - 1; index >= 0; index--)
            {
                var item = assets[index]?.AsObject(); var ownerKind = item?["ownerKind"]?.GetValue<string>();
                var ownerId = item?["ownerId"]?.GetValue<string>();
                if (ownerKind != "course" && !(ownerKind == "lesson" && ownerId != null && lessons.Contains(ownerId)) &&
                    !(ownerKind == "problem" && ownerId != null && problems.Contains(ownerId))) assets.RemoveAt(index);
            }
        return new("student", root.ToJsonString(Json));
    }

    public async Task<ContentAsset> StudentPublishedAsset(TokenAuthority token, string courseId, Guid contentVersionId,
        string assetPath, CancellationToken ct = default)
    {
        var preview = await StudentPublishedContent(token, courseId, contentVersionId, ct); assetPath = AssetPath(assetPath);
        using var visible = JsonDocument.Parse(preview.ArtifactJson);
        if (!visible.RootElement.GetProperty("assets").EnumerateArray().Any(x =>
            x.GetProperty("artifactPath").GetString() == assetPath)) throw new UseCaseException("NotFound");
        var asset = await db.ContentAssets.AsNoTracking().SingleOrDefaultAsync(x => x.ContentVersionId == contentVersionId &&
            x.Audience == "student" && x.AssetPath == assetPath, ct) ?? throw new UseCaseException("NotFound");
        return new(asset.MediaType, asset.Bytes, asset.Sha256);
    }

    private async Task EnsureRead(ResourceOwnership resource, TokenAuthority token, CancellationToken ct)
    {
        EnsureTime(token);
        if (!await new PostgresCurrentProcessingGate(db, clock).Allows(resource, token.Identity.ActorId, ScopeAction.Read, ct))
            throw new UseCaseException("Forbidden");
        EnsureTime(token);
    }

    private async Task Processing(ResourceOwnership resource, TokenAuthority token, CancellationToken ct)
    {
        EnsureTime(token); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT public.processing_lock({resource.OrganizationId})", ct);
        if (!await new PostgresCurrentProcessingGate(db, clock).AllowsInTransaction(resource, token.Identity.ActorId, ScopeAction.Read, ct))
            throw new UseCaseException("Forbidden");
        EnsureTime(token);
    }

    private ResourceOwnership TeacherResource(TokenAuthority token, string courseId, string? groupId, bool wholeCourse)
    {
        EnsureTime(token); var member = token.Authority.Membership;
        var allowed = member.Role == ScopeRole.Teacher && member.ActorId == token.Identity.ActorId &&
            token.Authority.Grants.Any(x => x.Kind == AuthorityKind.Teacher && x.CourseId == courseId &&
                x.MembershipGeneration == member.Generation && (wholeCourse ? x.GroupId == null : x.GroupId == null || x.GroupId == groupId));
        if (!allowed) throw new UseCaseException("Forbidden");
        return new(member.OrganizationId, courseId, groupId);
    }

    private ResourceOwnership StudentResource(TokenAuthority token, string courseId)
    {
        EnsureTime(token); var member = token.Authority.Membership;
        var grant = token.Authority.Grants.SingleOrDefault(x => x.Kind == AuthorityKind.Enrollment && x.CourseId == courseId &&
            x.MembershipGeneration == member.Generation && x.GroupId != null);
        if (member.Role != ScopeRole.Student || member.ActorId != token.Identity.ActorId || grant == null)
            throw new UseCaseException("Forbidden");
        return new(member.OrganizationId, courseId, grant.GroupId, token.Identity.ActorId,
            MembershipGeneration: grant.MembershipGeneration, EnrollmentGeneration: grant.Generation);
    }

    private void EnsureTime(TokenAuthority token)
    {
        var time = clock.Read();
        if (time is not { Valid: true } || time.Earliest < token.NotBefore || time.Latest >= token.ExpiresAt)
            throw new UseCaseException("Forbidden");
    }

    private DateTimeOffset Now() => clock.Read() is { Valid: true } time ? time.Earliest : throw new UseCaseException("Forbidden");
    private Task Advisory(Guid actor, string key, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT pg_advisory_xact_lock(hashtextextended({"m1-06:" + actor + ":" + key}, 0))", ct);

    private async Task<T?> Receipt<T>(TokenAuthority token, string key, string kind, string digest, CancellationToken ct)
    {
        var prior = await db.ContentOperationReceipts.SingleOrDefaultAsync(x => x.ActorId == token.Identity.ActorId && x.IdempotencyKey == key, ct);
        if (prior == null) return default;
        if (prior.Kind != kind || prior.PayloadDigest != digest) throw new UseCaseException("IdempotencyConflict");
        return JsonSerializer.Deserialize<T>(prior.ResultJson, Json) ?? throw new UseCaseException("ContentUnavailable");
    }

    private void AddReceipt<T>(TokenAuthority token, string key, string courseId, string kind, string digest, T result) =>
        db.Add(new ContentOperationReceipt { ActorId = token.Identity.ActorId, IdempotencyKey = key,
            OrganizationId = token.Authority.Membership.OrganizationId, CourseId = courseId, Kind = kind,
            PayloadDigest = digest, ResultJson = JsonSerializer.Serialize(result, Json), CreatedAt = Now() });

    private static ContentImportResult Result(ContentImportAttempt row) => new(row.ImportId, row.State, row.Version,
        row.CourseId, row.CreatedAt, row.ExpiresAt,
        JsonSerializer.Deserialize<ContentDiagnostic[]>(row.DiagnosticsJson, Json) ?? [],
        row.StudentProjectionHash, row.PackageHash, row.PublishedVersionId);

    private static IReadOnlyList<ContentDiagnostic> Diagnostics(IEnumerable<JsonElement> source) => source.Select(x => new ContentDiagnostic(
        x.TryGetProperty("severity", out var severity) ? severity.GetString() ?? "ERROR" : "ERROR",
        x.TryGetProperty("code", out var code) ? code.GetString() ?? "I_IMPORT" : "I_IMPORT",
        x.TryGetProperty("file", out var file) ? file.GetString() ?? "content-package.yaml" : "content-package.yaml",
        x.TryGetProperty("line", out var line) && line.TryGetInt32(out var l) ? l : 1,
        x.TryGetProperty("column", out var column) && column.TryGetInt32(out var c) ? c : 1,
        x.TryGetProperty("message", out var message) ? message.GetString() ?? "Import failed." : "Import failed.")).ToArray();

    private static ImportExecution FailedExecution(string code)
    {
        using var document = JsonDocument.Parse($"{{\"severity\":\"ERROR\",\"code\":\"{code}\",\"message\":\"Local import did not complete.\"}}");
        return new("Failed", [document.RootElement.Clone()], null, null, null);
    }

    private static IEnumerable<ContentImportAsset> Assets(Guid importId, string audience, ImportedArtifact artifact)
    {
        using var document = JsonDocument.Parse(artifact.Json);
        var manifest = document.RootElement.GetProperty("assets").EnumerateArray().ToDictionary(
            x => x.GetProperty("artifactPath").GetString()!, x => (MediaType: x.GetProperty("mediaType").GetString()!, Sha: x.GetProperty("sha256").GetString()!), StringComparer.Ordinal);
        if (!manifest.Keys.Order().SequenceEqual(artifact.Assets.Keys.Order(), StringComparer.Ordinal))
            throw new UseCaseException("ContentImportFailed");
        foreach (var pair in artifact.Assets)
        {
            var expected = manifest[pair.Key]; var actual = Convert.ToHexString(SHA256.HashData(pair.Value)).ToLowerInvariant();
            if (actual != expected.Sha) throw new UseCaseException("ContentImportFailed");
            yield return new() { ImportId = importId, Audience = audience, AssetPath = pair.Key,
                MediaType = expected.MediaType, Sha256 = actual, Bytes = pair.Value };
        }
    }

    private static PublishedCourseContent Published(string artifactJson)
    {
        using var document = JsonDocument.Parse(artifactJson); var course = document.RootElement.GetProperty("course");
        var lessons = course.GetProperty("lessons").EnumerateArray().Select(lesson => new PublishedLessonContent(
            lesson.GetProperty("id").GetString()!, lesson.GetProperty("order").GetInt32(), lesson.GetProperty("title").GetString()!,
            lesson.TryGetProperty("bodyMarkdown", out var lessonBody) ? lessonBody.GetString() : null,
            lesson.GetProperty("problems").EnumerateArray().Select(problem => new PublishedProblemContent(
                problem.GetProperty("id").GetString()!, problem.GetProperty("order").GetInt32(),
                problem.TryGetProperty("title", out var title) ? title.GetString() : null,
                problem.GetProperty("bodyMarkdown").GetString()!)).ToArray())).ToArray();
        return new(course.GetProperty("title").GetString()!,
            course.TryGetProperty("description", out var description) ? description.GetString() : null, lessons);
    }

    private static async Task<string> CopyZip(Stream source, string path, long expectedLength, long limit, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) throw new UseCaseException("ContentUnavailable");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long total = 0; var buffer = new byte[65536];
        await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer, ct); if (read == 0) break; total += read;
                if (total > limit) throw new UseCaseException("ValidationFailed");
                hash.AppendData(buffer, 0, read); await target.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            await target.FlushAsync(ct); target.Flush(true);
        }
        File.SetUnixFileMode(path, UnixFileMode.UserRead);
        if (total != expectedLength) throw new UseCaseException("ValidationFailed");
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string Text(JsonElement value, string property) => value.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()) ? item.GetString()! : throw new UseCaseException("ContentImportFailed");
    private static string Audience(string value) => value is "student" or "teacher" ? value : throw new UseCaseException("NotFound");
    private static string AssetPath(string value) => value.Length is > 0 and <= 240 && value.StartsWith("assets/", StringComparison.Ordinal) &&
        !value.Contains("..", StringComparison.Ordinal) && value.All(x => char.IsLetterOrDigit(x) || x is '/' or '.' or '-' or '_')
        ? value : throw new UseCaseException("NotFound");
    private static string Required(string? value, int min, int max) => value != null && value.Length >= min && value.Length <= max &&
        !string.IsNullOrWhiteSpace(value) && value.All(x => !char.IsControl(x)) ? value : throw new UseCaseException("ValidationFailed");
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private SecurityAudit Audit(Guid org, TokenAuthority token, string action, string subject, string resource) => new()
    { AuditId = Guid.NewGuid(), OrganizationId = org, ActorId = token.Identity.ActorId, Action = action,
        SubjectRef = subject, ResourceRef = resource, Result = "success", AuthorityVersion = token.Authority.Membership.Version,
        TraceId = Guid.NewGuid().ToString("N"), OccurredAt = Now() };
}
