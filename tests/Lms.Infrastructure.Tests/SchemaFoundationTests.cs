using System.Security.Cryptography;
using System.Text;
using Lms.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Lms.Infrastructure.Tests;

// No ambient connection strings: each target is an isolated, synthetic Testcontainer DB.
public sealed class SchemaFoundationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Student = Guid.Parse("00000000-0000-0000-0000-000000000011");
    private static readonly Guid Teacher = Guid.Parse("00000000-0000-0000-0000-000000000012");
    private static readonly Guid User = Guid.Parse("00000000-0000-0000-0000-000000000021");
    private static readonly Guid ContextA = Guid.Parse("00000000-0000-0000-0000-000000000031");
    private static readonly Guid ContextB = Guid.Parse("00000000-0000-0000-0000-000000000032");
    private static readonly DateTimeOffset At = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);

    private async Task<string> Seed(string? migration = null)
    {
        var cs = await fixture.CreateDatabase(migration);
        await using var db = PostgresFixture.Open(cs);
        db.AddRange(new Organization { OrganizationId = A, State = "Enabled", SyntheticOnly = true },
            new Organization { OrganizationId = B, State = "Enabled", SyntheticOnly = true },
            new ActorRecord { ActorId = Student, PrimaryEducationalRole = "Student" },
            new ActorRecord { ActorId = Teacher, PrimaryEducationalRole = "Teacher" },
            new IdentityUser<Guid> { Id = User, UserName = "synthetic-shared", NormalizedUserName = "SYNTHETIC-SHARED" },
            new ActorIdentity { UserId = User, ActorId = Student, ProvisioningOrgId = A, IsActive = true });
        foreach (var org in new[] { A, B })
        foreach (var actor in new[] { Student, Teacher })
            db.Add(new OrganizationMembership { OrganizationId = org, ActorId = actor, Generation = 1,
                State = "Active", EducationalRole = actor == Student ? "Student" : "Teacher", Version = 1 });
        db.AddRange(new CourseRegistration { CourseId = "a", OrganizationId = A, MappingState = "Verified" },
            new CourseRegistration { CourseId = "a2", OrganizationId = A, MappingState = "Verified" },
            new CourseRegistration { CourseId = "b", OrganizationId = B, MappingState = "Verified" },
            new CourseRegistration { CourseId = "unknown", MappingState = "Unknown" });
        foreach (var course in new[] { "a", "a2", "b" }) db.Add(new CourseGroup { CourseId = course, GroupId = "g" });
        foreach (var (org, context) in new[] { (A, ContextA), (B, ContextB) })
            db.Add(new ProcessingContextVersion { ContextId = context, Version = 1, OrganizationId = org,
                SyntheticOnly = true, ApprovalState = "SyntheticOnly", EffectiveFrom = At, PurposeRef = "synthetic-test" });
        await db.SaveChangesAsync();
        return cs;
    }
    private static async Task Sql(string cs, string sql)
    {
        await using var db = PostgresFixture.Open(cs);
        await db.Database.ExecuteSqlRawAsync(sql);
    }
    private static async Task Reject(string cs, string sql, string state = PostgresErrorCodes.CheckViolation)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => Sql(cs, sql)); Assert.Equal(state, ex.SqlState);
    }
    private static async Task<T> Scalar<T>(string cs, string sql)
    {
        await using var connection = new NpgsqlConnection(cs); await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
    private static string Enrollment(Guid org, string course) => $"""
        INSERT INTO "EnrollmentScopes" ("StudentId","CourseId","OrganizationId","GroupId","MembershipGeneration","EnrollmentGeneration","State","Version")
        VALUES ('{Student}','{course}','{org}','g',1,1,'Active',1)
        """;
    private static Task LegacyEnrollment(string cs, string course)
        => Sql(cs, $"""INSERT INTO "AccessEnrollments" VALUES ('{Student}','{course}','g',true)""");

    [Fact]
    public async Task Empty_schema_has_validated_keys_no_cascades_and_repeatable_M1()
    {
        var cs = await fixture.CreateDatabase(); await using var db = PostgresFixture.Open(cs);
        Assert.False(db.Database.HasPendingModelChanges());
        foreach (var table in new[] { "AspNetUsers", "AspNetRoles", "AspNetUserRoles", "AspNetUserClaims", "AspNetUserLogins", "AspNetUserTokens", "AspNetRoleClaims",
                     "Actors", "ActorIdentities", "Organizations", "OrganizationMemberships", "CourseRegistry", "CourseGroups", "EnrollmentScopes", "TeacherAssignmentScopes",
                     "AttachmentScopes", "ProcessingContextVersions", "CourseContextBindings", "ProcessingRestrictions", "IdentitySessions", "RefreshVerifiers",
                     "AccountSecurityState", "AccessChallenges", "RecoveryRequests", "ChallengeReceipts", "ProvisioningRequests", "SecurityAudit" })
        {
            Assert.Equal(0L, await Scalar<long>(cs, $"SELECT count(*) FROM \"{table}\""));
            Assert.True(await Scalar<bool>(cs, $"SELECT EXISTS(SELECT 1 FROM pg_constraint WHERE conrelid='\"{table}\"'::regclass AND contype='p')"));
        }
        Assert.Equal(0L, await Scalar<long>(cs, "SELECT count(*) FROM pg_constraint WHERE contype='f' AND confdeltype <> 'r' AND connamespace='public'::regnamespace"));
        Assert.Equal(0L, await Scalar<long>(cs, "SELECT count(*) FROM pg_constraint WHERE NOT convalidated AND connamespace='public'::regnamespace"));
        Assert.Equal(2L, await Scalar<long>(cs, "SELECT count(*) FROM pg_constraint WHERE contype='x' AND connamespace='public'::regnamespace"));
        await db.Database.MigrateAsync(); Assert.Empty(await db.Set<ActorRecord>().ToListAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() => db.GetService<IMigrator>().MigrateAsync("20261008154119_ReviewAnnotations"));
    }

    [Fact]
    public async Task Stable_identity_multi_org_membership_does_not_create_educational_grants()
    {
        var cs = await Seed(); await using var db = PostgresFixture.Open(cs);
        Assert.Equal(2, await db.Set<OrganizationMembership>().CountAsync(x => x.ActorId == Student));
        Assert.Empty(await db.Enrollments.ToListAsync()); Assert.Empty(await db.TeacherGrants.ToListAsync());
        Assert.Empty(await db.Set<EnrollmentScope>().ToListAsync()); Assert.Empty(await db.Set<TeacherAssignmentScope>().ToListAsync());
        await Reject(cs, $"""UPDATE "ActorIdentities" SET "ActorId"='{Teacher}' WHERE "UserId"='{User}'""");
        await Reject(cs, $"""INSERT INTO "ActorIdentities" VALUES ('{Guid.NewGuid()}','{Student}',true,'{A}')""", PostgresErrorCodes.UniqueViolation);
        await Reject(cs, $"""DELETE FROM "Actors" WHERE "ActorId"='{Student}'""");
        await Reject(cs, $"""DELETE FROM "AspNetUsers" WHERE "Id"='{User}'""", PostgresErrorCodes.ForeignKeyViolation);
        await Reject(cs, $"""INSERT INTO "AspNetUsers" ("Id","NormalizedUserName","EmailConfirmed","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount") VALUES ('{Guid.NewGuid()}','SYNTHETIC-SHARED',false,false,false,false,0)""", PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Registry_quarantine_course_groups_and_enrollment_reject_cross_org_links()
    {
        var cs = await Seed(); await LegacyEnrollment(cs, "a"); await LegacyEnrollment(cs, "b");
        await Reject(cs, Enrollment(B, "a"), PostgresErrorCodes.ForeignKeyViolation);
        await Sql(cs, Enrollment(A, "a")); await Sql(cs, Enrollment(B, "b"));
        await Reject(cs, $"""UPDATE "CourseRegistry" SET "OrganizationId"='{B}' WHERE "CourseId"='a'""");
        await Reject(cs, """UPDATE "AccessEnrollments" SET "GroupId"=NULL WHERE "CourseId"='a'""");
        await Reject(cs, """INSERT INTO "CourseRegistry" ("CourseId","MappingState") VALUES ('invalid','Verified')""");
        await Reject(cs, $"""UPDATE "CourseRegistry" SET "OrganizationId"='{A}' WHERE "CourseId"='unknown'""");
        await Reject(cs, $"""DELETE FROM "Organizations" WHERE "OrganizationId"='{A}'""", PostgresErrorCodes.ForeignKeyViolation);
        await using var db = PostgresFixture.Open(cs); Assert.Empty(await db.Set<TeacherAssignmentScope>().ToListAsync());
    }

    // S3 blocker evidence: the approved move use case cannot use the retained S1 guards.
    [Fact]
    public async Task Enrollment_move_is_blocked_even_after_revoke_and_new_generation()
    {
        var cs = await Seed("20261009101331_SessionVerifierLookupS2");
        await LegacyEnrollment(cs, "a");
        await Sql(cs, Enrollment(A, "a"));
        await Sql(cs, """INSERT INTO "CourseGroups" ("CourseId","GroupId") VALUES ('a','g2')""");
        await Sql(cs, """UPDATE "EnrollmentScopes" SET "State"='Revoked',"Version"=2,"RevocationRequestedAt"='2026-10-09T00:00Z',"AccessValidUntil"='2026-10-09T02:00Z'""");

        var sidecar = await Assert.ThrowsAsync<PostgresException>(() => Sql(cs,
            """UPDATE "EnrollmentScopes" SET "GroupId"='g2',"EnrollmentGeneration"=2,"Version"=3,"State"='Active',"RevocationRequestedAt"=NULL,"AccessValidUntil"=NULL"""));
        Assert.Equal(PostgresErrorCodes.CheckViolation, sidecar.SqlState);
        Assert.Equal("L1 stable association: GroupId", sidecar.MessageText);
        var legacy = await Assert.ThrowsAsync<PostgresException>(() => Sql(cs,
            """UPDATE "AccessEnrollments" SET "GroupId"='g2' WHERE "CourseId"='a'"""));
        Assert.Equal(PostgresErrorCodes.CheckViolation, legacy.SqlState);
        Assert.Equal("L1 scoped enrollment binding is stable", legacy.MessageText);
        await Reject(cs, """DELETE FROM "EnrollmentScopes" WHERE true""");
        await Reject(cs, Enrollment(A, "a"), PostgresErrorCodes.UniqueViolation);

        await using var db = PostgresFixture.Open(cs);
        var scope = await db.Set<EnrollmentScope>().SingleAsync();
        Assert.Equal("g", scope.GroupId);
        Assert.Equal("Revoked", scope.State);
        Assert.Equal(1, scope.EnrollmentGeneration);
        Assert.Equal(2, scope.Version);
        Assert.Equal("g", (await db.Enrollments.SingleAsync()).GroupId);
    }

    [Fact]
    public async Task B01_forward_upgrade_preserves_S2_rows_and_does_not_backfill_history()
    {
        var cs = await Seed("20261009101331_SessionVerifierLookupS2");
        await LegacyEnrollment(cs, "a"); await Sql(cs, Enrollment(A, "a"));
        var before = await Scalar<string>(cs, """SELECT jsonb_agg(to_jsonb(s))::text FROM "EnrollmentScopes" s""");
        await using (var db = PostgresFixture.Open(cs)) { await db.Database.MigrateAsync(); await db.Database.MigrateAsync(); }
        Assert.Equal(before, await Scalar<string>(cs, """SELECT jsonb_agg(to_jsonb(s))::text FROM "EnrollmentScopes" s"""));
        Assert.Equal(0L, await Scalar<long>(cs, """SELECT count(*) FROM "AuthorityHistory" s"""));
        Assert.Equal(0L, await Scalar<long>(cs, """SELECT count(*) FROM "AssignmentScopes" s"""));
        await Sql(cs, """INSERT INTO "CourseGroups" ("CourseId","GroupId") VALUES ('a','g2')""");
        await Sql(cs, $"""SELECT public.s3_change_authority('Enrollment','Move','{A}','{Student}',1,1,'a','g2',NULL,NULL,0)""");
        Assert.Equal("g2", await Scalar<string>(cs, """SELECT "GroupId" FROM "EnrollmentScopes" WHERE "CourseId"='a'"""));
        Assert.Equal("g2", await Scalar<string>(cs, """SELECT "GroupId" FROM "AccessEnrollments" WHERE "CourseId"='a'"""));
        Assert.True(await Scalar<long>(cs, """SELECT count(*) FROM "AuthorityHistory" s""") > 0);
    }

    [Fact]
    public async Task Membership_regrant_leaves_old_scopes_stale_and_deadline_cannot_be_extended()
    {
        var cs = await Seed(); await LegacyEnrollment(cs, "a"); await Sql(cs, Enrollment(A, "a"));
        var target = $"\"OrganizationId\"='{A}' AND \"ActorId\"='{Student}'";
        await Sql(cs, $"""UPDATE "OrganizationMemberships" SET "State"='Revoked',"RevocationRequestedAt"='2026-10-09T00:00Z',"AccessValidUntil"='2026-10-09T02:00Z',"Version"=2 WHERE {target}""");
        await Reject(cs, $"""UPDATE "OrganizationMemberships" SET "AccessValidUntil"='2026-10-09T02:01Z',"Version"=3 WHERE {target}""");
        await Reject(cs, $"""UPDATE "OrganizationMemberships" SET "State"='Active',"RevocationRequestedAt"=NULL,"AccessValidUntil"=NULL,"Version"=3 WHERE {target}""");
        await Sql(cs, $"""UPDATE "OrganizationMemberships" SET "State"='Active',"Generation"=2,"RevocationRequestedAt"=NULL,"AccessValidUntil"=NULL,"Version"=3 WHERE {target}""");
        await using var db = PostgresFixture.Open(cs);
        Assert.Equal(1, (await db.Set<EnrollmentScope>().SingleAsync()).MembershipGeneration);
        Assert.Equal(1, (await db.Set<OrganizationMembership>().SingleAsync(x => x.OrganizationId == B && x.ActorId == Student)).Generation);
        await Reject(cs, """UPDATE "EnrollmentScopes" SET "MembershipGeneration"=2,"Version"=2""");
        await Sql(cs, """UPDATE "EnrollmentScopes" SET "MembershipGeneration"=2,"EnrollmentGeneration"=2,"Version"=2""");
    }

    [Fact]
    public async Task Teacher_sidecars_match_legacy_both_ways_and_whole_course_grants_are_unique()
    {
        var cs = await Seed(); var grant = Guid.NewGuid(); var second = Guid.NewGuid();
        await Sql(cs, $"""INSERT INTO "AccessTeachers" ("Id","TeacherId","CourseId") VALUES ('{grant}','{Teacher}','a'),('{second}','{Teacher}','a')""");
        string Scope(Guid id, Guid org, string course = "a") => $"""INSERT INTO "TeacherAssignmentScopes" ("GrantId","TeacherId","CourseId","OrganizationId","MembershipGeneration","Generation","State","Version") VALUES ('{id}','{Teacher}','{course}','{org}',1,1,'Active',1)""";
        await Reject(cs, Scope(grant, B), PostgresErrorCodes.ForeignKeyViolation);
        await Reject(cs, Scope(grant, B, "b")); await Sql(cs, Scope(grant, A));
        await Reject(cs, Scope(second, A), PostgresErrorCodes.UniqueViolation);
        await Reject(cs, $"""UPDATE "AccessTeachers" SET "CourseId"='b' WHERE "Id"='{grant}'""");
        await Reject(cs, $"""UPDATE "AccessTeachers" SET "GroupId"='g' WHERE "Id"='{grant}'""");
    }

    [Fact]
    public async Task Context_versions_are_immutable_synthetic_and_one_org_across_versions()
    {
        var cs = await Seed();
        await Reject(cs, $"""UPDATE "ProcessingContextVersions" SET "PurposeRef"='changed' WHERE "ContextId"='{ContextA}'""");
        await Reject(cs, $"""DELETE FROM "ProcessingContextVersions" WHERE "ContextId"='{ContextA}'""");
        await Reject(cs, $"""INSERT INTO "ProcessingContextVersions" ("ContextId","Version","OrganizationId","ApprovalState","SyntheticOnly","EffectiveFrom") VALUES ('{ContextA}',2,'{B}','SyntheticOnly',true,'2026-10-10T00:00Z')""", PostgresErrorCodes.ExclusionViolation);
        foreach (var (marker, state, basis) in new[] { ("false", "Unreviewed", "NULL"), ("true", "LegalApproved", "NULL"), ("true", "SyntheticOnly", "'invented-basis'") })
            await Reject(cs, $"""INSERT INTO "ProcessingContextVersions" ("ContextId","Version","OrganizationId","ApprovalState","SyntheticOnly","EffectiveFrom","LegalBasisRef") VALUES ('{Guid.NewGuid()}',1,'{A}','{state}',{marker},'2026-10-09T00:00Z',{basis})""");
        await Reject(cs, $"""INSERT INTO "Organizations" VALUES ('{Guid.NewGuid()}','Enabled',false)""");
    }
    private static string Binding(string course, Guid org, Guid context, string start, string? end = null) => $"""
        INSERT INTO "CourseContextBindings" ("CourseId","OrganizationId","ContextId","ContextVersion","EffectiveFrom","EffectiveTo")
        VALUES ('{course}','{org}','{context}',1,'{start}',{(end is null ? "NULL" : $"'{end}'")})
        """;
    [Fact]
    public async Task Binding_intervals_are_half_open_nonoverlapping_same_org_and_retained()
    {
        var cs = await Seed();
        await Reject(cs, Binding("a", A, ContextB, "2026-10-09T00:00Z"), PostgresErrorCodes.ForeignKeyViolation);
        await Reject(cs, Binding("a", B, ContextB, "2026-10-09T00:00Z"), PostgresErrorCodes.ForeignKeyViolation);
        await Reject(cs, Binding("a", A, ContextA, "2026-10-09T00:00Z", "2026-10-09T00:00Z"));
        await Sql(cs, Binding("a", A, ContextA, "2026-10-09T00:00Z", "2026-10-10T00:00Z"));
        await Reject(cs, Binding("a", A, ContextA, "2026-10-09T01:00Z"), PostgresErrorCodes.ExclusionViolation);
        await Sql(cs, Binding("a", A, ContextA, "2026-10-10T00:00Z"));
        await Reject(cs, """UPDATE "CourseContextBindings" SET "EffectiveTo"=NULL WHERE "EffectiveTo" IS NOT NULL""");
        await Reject(cs, """DELETE FROM "CourseContextBindings" """);
    }
    [Fact]
    public async Task Concurrent_overlapping_bindings_have_one_winner()
    {
        var cs = await Seed();
        var results = await Task.WhenAll(Record.ExceptionAsync(() => Sql(cs, Binding("a", A, ContextA, "2026-10-09T00:00Z"))),
            Record.ExceptionAsync(() => Sql(cs, Binding("a", A, ContextA, "2026-10-10T00:00Z"))));
        Assert.Single(results, x => x is null);
        Assert.Equal(PostgresErrorCodes.ExclusionViolation, Assert.IsType<PostgresException>(Assert.Single(results, x => x is not null)).SqlState);
    }
    [Fact]
    public async Task Restrictions_keep_org_subject_provenance_and_are_not_reset_by_new_context_version()
    {
        var cs = await Seed(); var outsider = Guid.NewGuid();
        await Sql(cs, $"""INSERT INTO "Actors" ("ActorId") VALUES ('{outsider}')""");
        string Restriction(Guid org, Guid subject) => $"""INSERT INTO "ProcessingRestrictions" ("RestrictionId","ContextId","OriginVersion","OrganizationId","SubjectActorId","Operations","State","Version") VALUES ('{Guid.NewGuid()}','{ContextA}',1,'{org}','{subject}',1,'Active',1)""";
        await Reject(cs, Restriction(B, Student), PostgresErrorCodes.ForeignKeyViolation);
        await Reject(cs, Restriction(A, outsider), PostgresErrorCodes.ForeignKeyViolation);
        await Sql(cs, Restriction(A, Student));
        await Sql(cs, $"""INSERT INTO "ProcessingContextVersions" ("ContextId","Version","OrganizationId","ApprovalState","SyntheticOnly","EffectiveFrom") VALUES ('{ContextA}',2,'{A}','SyntheticOnly',true,'2026-10-10T00:00Z')""");
        await using var db = PostgresFixture.Open(cs);
        Assert.Single(await db.Set<ProcessingRestriction>().Where(x => x.ContextId == ContextA && x.State == "Active").ToListAsync());
        Assert.Equal(1, (await db.Set<ProcessingRestriction>().SingleAsync()).OriginVersion);
    }
    [Fact]
    public async Task Challenges_reject_invalid_verifiers_ttl_duplicate_live_and_terminal_reuse()
    {
        var cs = await Seed(); var id = Guid.NewGuid();
        string Challenge(Guid challenge, string purpose = "Activation", string expiry = "2026-10-10T00:00Z", int bytes = 32) => $"""
            INSERT INTO "AccessChallenges" ("ChallengeId","UserId","Purpose","State","SecretMac","KeyVersion","CreatedAt","ExpiresAt","FailedAttempts","Version")
            VALUES ('{challenge}','{User}','{purpose}','Created',decode(repeat('ab',{bytes}),'hex'),1,'2026-10-09T00:00Z','{expiry}',0,1)
            """;
        await Reject(cs, Challenge(id, bytes: 16));
        await Reject(cs, Challenge(id, "Recovery", "2026-10-09T00:16Z"));
        await Sql(cs, Challenge(id));
        await Reject(cs, Challenge(Guid.NewGuid()), PostgresErrorCodes.UniqueViolation);
        await Reject(cs, $"""UPDATE "AccessChallenges" SET "FailedAttempts"=5,"Version"=2 WHERE "ChallengeId"='{id}'""");
        await Sql(cs, $"""UPDATE "AccessChallenges" SET "State"='Locked',"FailedAttempts"=5,"TerminalAt"='2026-10-09T00:01Z',"Version"=2 WHERE "ChallengeId"='{id}'""");
        await Reject(cs, $"""UPDATE "AccessChallenges" SET "State"='Created',"FailedAttempts"=0,"TerminalAt"=NULL,"Version"=3 WHERE "ChallengeId"='{id}'""");
        await Sql(cs, Challenge(Guid.NewGuid()));
    }

    [Fact]
    public async Task Security_shells_preserve_session_verifier_history_and_independent_recovery()
    {
        var cs = await Seed(); var sid = Guid.NewGuid(); var request = Guid.NewGuid();
        await Sql(cs, $"""INSERT INTO "IdentitySessions" ("SessionId","UserId","CreatedAt","LastSuccessfulRefreshAt","AbsoluteExpiresAt","Generation","Version") VALUES ('{sid}','{User}','2026-10-09T00:00Z','2026-10-09T00:00Z','2026-10-09T12:00Z',1,1)""");
        await Reject(cs, $"""UPDATE "IdentitySessions" SET "AbsoluteExpiresAt"='2026-10-10T00:00Z',"Version"=2 WHERE "SessionId"='{sid}'""");
        await Sql(cs, $"""INSERT INTO "RefreshVerifiers" ("SessionId","Generation","Verifier","KeyVersion","CreatedAt") VALUES ('{sid}',1,decode(repeat('ab',32),'hex'),1,'2026-10-09T00:00Z')""");
        await Sql(cs, $"""UPDATE "RefreshVerifiers" SET "ConsumedAt"='2026-10-09T00:01Z' WHERE "SessionId"='{sid}'""");
        await Reject(cs, $"""UPDATE "RefreshVerifiers" SET "ConsumedAt"=NULL WHERE "SessionId"='{sid}'""");
        await Sql(cs, $"""UPDATE "IdentitySessions" SET "RevokedAt"='2026-10-09T00:01Z',"Version"=2 WHERE "SessionId"='{sid}'""");
        await Reject(cs, $"""UPDATE "IdentitySessions" SET "RevokedAt"=NULL,"Version"=3 WHERE "SessionId"='{sid}'""");
        await Reject(cs, $"""INSERT INTO "RecoveryRequests" ("RequestId","TargetUserId","InitiatingOrgId","InitiatorActorId","ApproverActorId","State","Version","VerificationRef","ApprovedAt") VALUES ('{request}','{User}','{A}','{Student}','{Student}','Approved',1,'synthetic-verification','2026-10-09T00:00Z')""");
        await Sql(cs, $"""INSERT INTO "RecoveryRequests" ("RequestId","TargetUserId","InitiatingOrgId","InitiatorActorId","ApproverActorId","State","Version","VerificationRef","ApprovedAt") VALUES ('{request}','{User}','{A}','{Student}','{Teacher}','Approved',1,'synthetic-verification','2026-10-09T00:00Z')""");
        await Sql(cs, $"""INSERT INTO "SecurityAudit" ("AuditId","OrganizationId","ActorId","Action","SubjectRef","Result","TraceId","OccurredAt") VALUES ('{Guid.NewGuid()}','{A}','{Student}','synthetic-test','synthetic-subject','ok','test','2026-10-09T00:00Z')""");
        await Reject(cs, """UPDATE "SecurityAudit" SET "Result"='changed'""");
        await Reject(cs, """DELETE FROM "SecurityAudit" """);
        var restored = await fixture.Restore(await fixture.Backup(cs));
        Assert.True(await Scalar<bool>(restored, $"""SELECT "RevokedAt" IS NOT NULL FROM "IdentitySessions" WHERE "SessionId"='{sid}'"""));
        Assert.True(await Scalar<bool>(restored, $"""SELECT "ConsumedAt" IS NOT NULL FROM "RefreshVerifiers" WHERE "SessionId"='{sid}'"""));
        await Reject(restored, $"""UPDATE "IdentitySessions" SET "RevokedAt"=NULL,"Version"=3 WHERE "SessionId"='{sid}'""");
    }

    [Fact]
    public async Task M1_preserves_synthetic_legacy_history_and_original_hashes_without_backfill()
    {
        using var assets = new SyntheticAssets();
        var assetHash = assets.Hash();
        var cs = await fixture.CreateDatabase("20261008154119_ReviewAnnotations");
        var attachment = Guid.NewGuid(); var content = Guid.NewGuid();
        var assignment = new ProblemAssignment(Guid.NewGuid(), Student, "legacy-synthetic", new(content, "lesson", "problem"));
        await using (var db = PostgresFixture.Open(cs))
        {
            db.AddRange(assignment, new PublishedContentVersion { Id = content, CourseId = assignment.CourseId, VersionNumber = 1,
                PublishedAt = At, SourceRevision = "synthetic", ContentJson = "{\"title\":\"Synthetic\",\"lessons\":[]}" },
                new AttachmentAccess { Id = attachment, OwnerId = Student, IsEligible = true, ExpiresAt = At.AddDays(1) },
                new StoredAttachment { Id = attachment, OwnerId = Student, FileName = "synthetic.bin", MediaType = "application/octet-stream", Size = 1, StorageKey = "synthetic-key", CreatedAt = At });
            await db.SaveChangesAsync();
            var work = new ProblemWork(Guid.NewGuid(), assignment);
            work.SubmitSolution(assignment, Student, "synthetic submission", [attachment], At);
            var store = new PostgresWorkStore(db); await store.Save(work, null, CancellationToken.None);
            work.Accept(Teacher, work.Submissions[0].Id, "synthetic review", At.AddMinutes(1),
                [new(attachment, [new(Guid.NewGuid(), "pen", "#dc2626", .003, [new(.2, .3)])])]);
            await store.Save(work, 1, CancellationToken.None);
        }
        string[] tables = ["PublishedContentVersions", "ProblemAssignments", "ProblemWorks", "Submissions", "Messages", "Reviews", "DomainEvents", "AccessAttachments", "StoredAttachments"];
        async Task<string> Digest(string connectionString)
        {
            var values = new List<string>();
            foreach (var table in tables)
                values.Add(await Scalar<string>(connectionString, $"SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text)::text,'[]') FROM \"{table}\" t"));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', values))));
        }
        var before = await Digest(cs);
        var backup = await fixture.Backup(cs);
        await using (var db = PostgresFixture.Open(cs)) { await db.Database.MigrateAsync(); await db.Database.MigrateAsync(); }
        Assert.Equal(before, await Digest(cs));
        Assert.Equal(assetHash, assets.Hash());
        assets.Restore();
        Assert.Equal(assetHash, assets.Hash());
        var restored = await fixture.Restore(backup);
        Assert.Equal(before, await Digest(restored));
        await using (var db = PostgresFixture.Open(restored)) await db.Database.MigrateAsync();
        Assert.Equal(before, await Digest(restored));
        await using var check = PostgresFixture.Open(cs);
        Assert.Empty(await check.Set<CourseRegistration>().ToListAsync()); Assert.Empty(await check.Set<ActorRecord>().ToListAsync());
        Assert.Empty(await check.Users.ToListAsync()); Assert.Empty(await check.Set<AttachmentScope>().ToListAsync());
        Assert.Empty(await check.Set<OrganizationMembership>().ToListAsync());
        await Assert.ThrowsAsync<PostgresException>(() => Sql(cs, """UPDATE "Submissions" SET "BodyMarkdown"='rewrite'"""));
        await Assert.ThrowsAsync<PostgresException>(() => Sql(cs, """DELETE FROM "Reviews" """));
        Assert.Equal(before, await Digest(cs));
        // Restore of the expanded schema must also preserve its custom guards.
        var expandedRestore = await fixture.Restore(await fixture.Backup(cs));
        Assert.Equal(before, await Digest(expandedRestore));
        Assert.True(await Scalar<bool>(expandedRestore, "SELECT EXISTS(SELECT 1 FROM pg_constraint WHERE conname='EX_CourseBinding_NoOverlap')"));
    }

    [Fact]
    public async Task Attachment_scope_rejects_foreign_context_uploader_and_cross_course_claim()
    {
        var cs = await Seed(); var attachment = Guid.NewGuid();
        await using (var db = PostgresFixture.Open(cs))
        {
            db.AddRange(new AttachmentAccess { Id = attachment, OwnerId = Student, IsEligible = true, ExpiresAt = At.AddDays(1) },
                new StoredAttachment { Id = attachment, OwnerId = Student, FileName = "synthetic.bin", MediaType = "application/octet-stream", Size = 1, StorageKey = "synthetic", CreatedAt = At });
            await db.SaveChangesAsync();
        }
        string Scope(Guid org, Guid context, Guid uploader) => $"""INSERT INTO "AttachmentScopes" ("AttachmentId","OrganizationId","CourseId","UploaderActorId","ContextId","ContextVersion","Quarantined") VALUES ('{attachment}','{org}','a','{uploader}','{context}',1,false)""";
        await Reject(cs, Scope(B, ContextB, Student), PostgresErrorCodes.ForeignKeyViolation);
        await Reject(cs, Scope(A, ContextB, Student), PostgresErrorCodes.ForeignKeyViolation);
        await Reject(cs, Scope(A, ContextA, Teacher)); await Sql(cs, Scope(A, ContextA, Student));
        await Reject(cs, """UPDATE "AttachmentScopes" SET "CourseId"='b'""");
        async Task<Exception?> Submit(string course)
        {
            await using var db = PostgresFixture.Open(cs);
            var assignment = new ProblemAssignment(Guid.NewGuid(), Student, course, new(Guid.NewGuid(), "l", "p"));
            db.Add(assignment); await db.SaveChangesAsync();
            var work = new ProblemWork(Guid.NewGuid(), assignment);
            work.SubmitSolution(assignment, Student, "synthetic", [attachment], At);
            return await Record.ExceptionAsync(() => new PostgresWorkStore(db).Save(work, null, CancellationToken.None));
        }
        Assert.NotNull(await Submit("b")); Assert.NotNull(await Submit("a2")); Assert.Null(await Submit("a"));
        Assert.Equal(1L, await Scalar<long>(cs, """SELECT count(*) FROM "Submissions" """));
    }
    [Fact]
    public async Task User_cannot_commit_without_actor_and_binding_cannot_predate_context()
    {
        var cs = await Seed();
        await Reject(cs, $"""INSERT INTO "AspNetUsers" ("Id","NormalizedUserName","EmailConfirmed","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount") VALUES ('{Guid.NewGuid()}','UNMAPPED-SYNTHETIC',false,false,false,false,0)""", PostgresErrorCodes.ForeignKeyViolation);
        await Reject(cs, Binding("a", A, ContextA, "2026-10-08T00:00Z"));
    }

    [Theory]
    [InlineData(System.Data.IsolationLevel.ReadCommitted, PostgresErrorCodes.CheckViolation)]
    [InlineData(System.Data.IsolationLevel.RepeatableRead, PostgresErrorCodes.SerializationFailure)]
    public async Task Scope_creation_cannot_be_bypassed_by_preexisting_legacy_writer_snapshot(System.Data.IsolationLevel isolation, string sqlState)
    {
        var cs = await Seed(); var id = Guid.NewGuid();
        await Sql(cs, $"""INSERT INTO "AccessTeachers" ("Id","TeacherId","CourseId") VALUES ('{id}','{Teacher}','a')""");
        await using var writer = new NpgsqlConnection(cs); await writer.OpenAsync();
        await using var transaction = await writer.BeginTransactionAsync(isolation);
        await using (var snapshot = new NpgsqlCommand("SELECT count(*) FROM \"AccessTeachers\"", writer, transaction)) await snapshot.ExecuteScalarAsync();
        await Sql(cs, $"""INSERT INTO "TeacherAssignmentScopes" ("GrantId","TeacherId","CourseId","OrganizationId","MembershipGeneration","Generation","State","Version") VALUES ('{id}','{Teacher}','a','{A}',1,1,'Active',1)""");
        await using var mutation = new NpgsqlCommand($"""UPDATE "AccessTeachers" SET "CourseId"='b' WHERE "Id"='{id}'""", writer, transaction);
        var error = await Assert.ThrowsAsync<PostgresException>(() => mutation.ExecuteNonQueryAsync());
        Assert.Equal(sqlState, error.SqlState);
        await transaction.RollbackAsync();
        Assert.Equal("a", await Scalar<string>(cs, $"""SELECT "CourseId" FROM "AccessTeachers" WHERE "Id"='{id}'"""));
    }

    [Fact]
    public async Task Restriction_origin_and_recovery_approval_cannot_be_rewritten()
    {
        var cs = await Seed(); var request = Guid.NewGuid();
        await Sql(cs, $"""INSERT INTO "ProcessingRestrictions" ("RestrictionId","ContextId","OriginVersion","OrganizationId","Operations","State","Version") VALUES ('{Guid.NewGuid()}','{ContextA}',1,'{A}',1,'Active',1)""");
        await Reject(cs, $"""UPDATE "ProcessingRestrictions" SET "ContextId"='{ContextB}',"OrganizationId"='{B}',"Version"=2""");
        await Sql(cs, $"""INSERT INTO "RecoveryRequests" ("RequestId","TargetUserId","InitiatingOrgId","InitiatorActorId","ApproverActorId","State","Version","VerificationRef","ApprovedAt") VALUES ('{request}','{User}','{A}','{Student}','{Teacher}','Approved',1,'synthetic-verification','2026-10-09T00:00Z')""");
        await Sql(cs, $"""UPDATE "RecoveryRequests" SET "State"='Fulfilled',"Version"=2 WHERE "RequestId"='{request}'""");
        await Reject(cs, $"""UPDATE "RecoveryRequests" SET "State"='Approved',"Version"=3 WHERE "RequestId"='{request}'""");
    }
    private sealed class SyntheticAssets : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("lms-s1-synthetic-").FullName;
        public SyntheticAssets()
        {
            Directory.CreateDirectory(Path.Combine(root, "live"));
            Directory.CreateDirectory(Path.Combine(root, "backup"));
            // Entirely generated fixture bytes, not a person's image or deployment key.
            File.WriteAllBytes(Path.Combine(root, "live", "original.bin"), [1]);
            File.WriteAllBytes(Path.Combine(root, "live", "fixture-key.bin"), Enumerable.Repeat((byte)42, 32).ToArray());
            foreach (var file in Directory.GetFiles(Path.Combine(root, "live")))
                File.Copy(file, Path.Combine(root, "backup", Path.GetFileName(file)));
        }
        public string Hash() => Convert.ToHexString(SHA256.HashData(Directory.GetFiles(Path.Combine(root, "live"))
            .OrderBy(Path.GetFileName).SelectMany(File.ReadAllBytes).ToArray()));
        public void Restore()
        {
            foreach (var file in Directory.GetFiles(Path.Combine(root, "backup")))
                File.Copy(file, Path.Combine(root, "live", Path.GetFileName(file)), overwrite: true);
        }
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
