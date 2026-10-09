using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ScopedAuthorityS3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssignmentScopes",
                columns: table => new
                {
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<string>(type: "text", nullable: false),
                    GroupId = table.Column<string>(type: "text", nullable: true),
                    MembershipGeneration = table.Column<long>(type: "bigint", nullable: false),
                    EnrollmentGeneration = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentScopes", x => x.AssignmentId);
                    table.CheckConstraint("CK_AssignmentScope_Generation", "\"MembershipGeneration\" > 0 AND \"EnrollmentGeneration\" > 0");
                    table.ForeignKey(
                        name: "FK_AssignmentScopes_CourseGroups_CourseId_GroupId",
                        columns: x => new { x.CourseId, x.GroupId },
                        principalTable: "CourseGroups",
                        principalColumns: new[] { "CourseId", "GroupId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssignmentScopes_OrganizationMemberships_OrganizationId_Stu~",
                        columns: x => new { x.OrganizationId, x.StudentId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "OrganizationId", "ActorId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssignmentScopes_ProblemAssignments_AssignmentId_StudentId",
                        columns: x => new { x.AssignmentId, x.StudentId },
                        principalTable: "ProblemAssignments",
                        principalColumns: new[] { "Id", "StudentId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuthorityHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorityHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthorityHistory_OrganizationMemberships_OrganizationId_Act~",
                        columns: x => new { x.OrganizationId, x.ActorId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "OrganizationId", "ActorId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentScopes_AssignmentId_StudentId",
                table: "AssignmentScopes",
                columns: new[] { "AssignmentId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentScopes_CourseId_GroupId",
                table: "AssignmentScopes",
                columns: new[] { "CourseId", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentScopes_OrganizationId_StudentId",
                table: "AssignmentScopes",
                columns: new[] { "OrganizationId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorityHistory_OrganizationId_ActorId",
                table: "AuthorityHistory",
                columns: new[] { "OrganizationId", "ActorId" });
            migrationBuilder.Sql("""
                -- B01: leave S1 immutable; replace only the enrollment group guards in this forward migration.
                ALTER TABLE "AssignmentScopes" ADD CONSTRAINT "FK_AssignmentScopes_CourseOrganization"
                 FOREIGN KEY ("CourseId","OrganizationId") REFERENCES "CourseRegistry" ("CourseId","OrganizationId") ON DELETE RESTRICT;
                CREATE TRIGGER s3_assignment_scope_immutable BEFORE UPDATE OR DELETE ON "AssignmentScopes"
                 FOR EACH ROW EXECUTE FUNCTION s1_immutable_row();
                CREATE TRIGGER s3_authority_history_immutable BEFORE UPDATE OR DELETE ON "AuthorityHistory"
                 FOR EACH ROW EXECUTE FUNCTION s1_immutable_row();

                CREATE FUNCTION s3_authority_history() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
                DECLARE actor uuid;
                BEGIN
                 actor := COALESCE((to_jsonb(NEW)->>'ActorId')::uuid,(to_jsonb(NEW)->>'StudentId')::uuid,(to_jsonb(NEW)->>'TeacherId')::uuid);
                 -- Also capture OLD: no backfill on migration, but the first post-upgrade mutation retains its predecessor.
                 IF TG_OP='UPDATE' THEN
                  INSERT INTO public."AuthorityHistory" ("Kind","OrganizationId","ActorId","Snapshot","RecordedAt")
                  VALUES (TG_TABLE_NAME,NEW."OrganizationId",actor,to_jsonb(OLD),clock_timestamp());
                 END IF;
                 INSERT INTO public."AuthorityHistory" ("Kind","OrganizationId","ActorId","Snapshot","RecordedAt")
                 VALUES (TG_TABLE_NAME,NEW."OrganizationId",actor,to_jsonb(NEW),clock_timestamp());
                 RETURN NEW;
                END $$;
                CREATE TRIGGER s3_membership_history AFTER INSERT OR UPDATE ON "OrganizationMemberships" FOR EACH ROW EXECUTE FUNCTION s3_authority_history();
                CREATE TRIGGER s3_enrollment_history AFTER INSERT OR UPDATE ON "EnrollmentScopes" FOR EACH ROW EXECUTE FUNCTION s3_authority_history();
                CREATE TRIGGER s3_teacher_history AFTER INSERT OR UPDATE ON "TeacherAssignmentScopes" FOR EACH ROW EXECUTE FUNCTION s3_authority_history();

                DROP TRIGGER s1_enrollment_key ON "EnrollmentScopes";
                CREATE TRIGGER s1_enrollment_key BEFORE UPDATE OR DELETE ON "EnrollmentScopes"
                 FOR EACH ROW EXECUTE FUNCTION s1_stable_key('StudentId','CourseId','OrganizationId');
                CREATE FUNCTION s3_enrollment_move_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                 IF NEW."GroupId" IS DISTINCT FROM OLD."GroupId" AND
                  (OLD."State" <> 'Revoked' OR NEW."State" <> 'Active' OR NEW."EnrollmentGeneration" <> OLD."EnrollmentGeneration"+1) THEN
                  RAISE EXCEPTION 'L1 group move requires a retired predecessor and new generation' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END $$;
                CREATE TRIGGER s3_enrollment_move BEFORE UPDATE ON "EnrollmentScopes" FOR EACH ROW EXECUTE FUNCTION s3_enrollment_move_guard();

                -- Retain the S1 serialization write to AccessEnrollments, but equality is checked at commit.
                CREATE FUNCTION s3_enrollment_lock() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                 UPDATE public."AccessEnrollments" SET "IsActive"="IsActive" WHERE "StudentId"=NEW."StudentId" AND "CourseId"=NEW."CourseId";
                 IF NOT FOUND THEN RAISE EXCEPTION 'L1 missing enrollment' USING ERRCODE='23503'; END IF;
                 RETURN NEW;
                END $$;
                DROP TRIGGER s1_enrollment_match ON "EnrollmentScopes";
                CREATE TRIGGER s3_enrollment_lock BEFORE INSERT OR UPDATE ON "EnrollmentScopes" FOR EACH ROW EXECUTE FUNCTION s3_enrollment_lock();
                DROP TRIGGER s1_legacy_enrollment ON "AccessEnrollments";
                CREATE TRIGGER s3_legacy_enrollment_key BEFORE UPDATE ON "AccessEnrollments"
                 FOR EACH ROW EXECUTE FUNCTION s1_stable_key('StudentId','CourseId');
                CREATE FUNCTION s3_enrollment_consistency() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
                BEGIN
                 IF EXISTS (SELECT 1 FROM public."EnrollmentScopes" s JOIN public."AccessEnrollments" e USING ("StudentId","CourseId")
                   WHERE s."StudentId"=NEW."StudentId" AND s."CourseId"=NEW."CourseId" AND (s."GroupId" IS DISTINCT FROM e."GroupId" OR (s."State"='Active') IS DISTINCT FROM e."IsActive")) THEN
                  RAISE EXCEPTION 'L1 enrollment sidecar mismatch at commit' USING ERRCODE='23514';
                 END IF;
                 RETURN NULL;
                END $$;
                REVOKE ALL ON FUNCTION s3_enrollment_consistency() FROM PUBLIC;
                CREATE CONSTRAINT TRIGGER s3_enrollment_consistency AFTER INSERT OR UPDATE ON "EnrollmentScopes"
                 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION s3_enrollment_consistency();
                CREATE CONSTRAINT TRIGGER s3_legacy_enrollment_consistency AFTER INSERT OR UPDATE ON "AccessEnrollments"
                 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION s3_enrollment_consistency();

                -- Capture new assignment affiliation from server rows. Existing assignments are untouched.
                -- The same organization lock serializes capture with membership/revoke/move.
                CREATE FUNCTION s3_capture_assignment_scope() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
                DECLARE org uuid; e public."EnrollmentScopes"%ROWTYPE;
                BEGIN
                 SELECT "OrganizationId" INTO org FROM public."CourseRegistry" WHERE "CourseId"=NEW."CourseId" AND "MappingState"='Verified';
                 IF org IS NULL THEN RETURN NEW; END IF;
                 PERFORM 1 FROM public."Organizations" WHERE "OrganizationId"=org AND "State"='Enabled' AND "SyntheticOnly" FOR UPDATE;
                 IF NOT FOUND THEN RETURN NEW; END IF;
                 SELECT s.* INTO e FROM public."EnrollmentScopes" s JOIN public."OrganizationMemberships" m
                  ON m."OrganizationId"=s."OrganizationId" AND m."ActorId"=s."StudentId"
                  JOIN public."AccessEnrollments" a ON a."StudentId"=s."StudentId" AND a."CourseId"=s."CourseId"
                  WHERE s."StudentId"=NEW."StudentId" AND s."CourseId"=NEW."CourseId" AND s."OrganizationId"=org
                   AND s."State"='Active' AND m."State"='Active' AND m."EducationalRole"='Student'
                   AND s."MembershipGeneration"=m."Generation" AND a."IsActive" AND a."GroupId" IS NOT DISTINCT FROM s."GroupId";
                 IF FOUND THEN
                  INSERT INTO public."AssignmentScopes" VALUES (NEW."Id",org,NEW."StudentId",NEW."CourseId",e."GroupId",e."MembershipGeneration",e."EnrollmentGeneration");
                 END IF;
                 RETURN NEW;
                END $$;
                REVOKE ALL ON FUNCTION s3_capture_assignment_scope() FROM PUBLIC;
                CREATE TRIGGER s3_capture_assignment AFTER INSERT ON "ProblemAssignments" FOR EACH ROW EXECUTE FUNCTION s3_capture_assignment_scope();

                CREATE FUNCTION s3_change_authority(p_kind text,p_op text,p_org uuid,p_actor uuid,p_expected bigint,p_membergen bigint,
                 p_course text,p_group text,p_grant uuid,p_role text,p_permissions integer)
                RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
                DECLARE m public."OrganizationMemberships"%ROWTYPE; e public."EnrollmentScopes"%ROWTYPE;
                 g public."TeacherAssignmentScopes"%ROWTYPE; gen bigint; ver bigint; deadline timestamptz; resource text; p_at timestamptz;
                BEGIN
                 IF p_kind NOT IN ('Membership','Enrollment','Teacher') OR p_kind IS NULL OR p_op NOT IN ('Grant','Revoke','Move') OR p_op IS NULL
                  OR p_org IS NULL OR p_actor IS NULL OR p_org='00000000-0000-0000-0000-000000000000' OR p_actor='00000000-0000-0000-0000-000000000000'
                  OR p_expected IS NULL OR p_expected<0
                  OR p_permissions IS NULL OR p_permissions NOT BETWEEN 0 AND 15 OR p_role IS NOT NULL AND p_role NOT IN ('Student','Teacher')
                  OR p_group IS NOT NULL AND btrim(p_group)='' THEN RAISE EXCEPTION 'invalid_scope'; END IF;
                 PERFORM 1 FROM public."Organizations" WHERE "OrganizationId"=p_org AND "State"='Enabled' AND "SyntheticOnly" FOR UPDATE;
                 IF NOT FOUND THEN RAISE EXCEPTION 'scope_denied'; END IF;
                 p_at:=clock_timestamp();
                 PERFORM 1 FROM public."Actors" WHERE "ActorId"=p_actor;
                 IF NOT FOUND THEN RAISE EXCEPTION 'scope_denied'; END IF;
                 SELECT * INTO m FROM public."OrganizationMemberships" WHERE "OrganizationId"=p_org AND "ActorId"=p_actor FOR UPDATE;
                 IF p_kind='Membership' THEN
                  IF p_op='Move' OR p_course IS NOT NULL OR p_group IS NOT NULL OR p_grant IS NOT NULL THEN RAISE EXCEPTION 'invalid_scope'; END IF;
                  IF COALESCE(m."Version",0)<>p_expected THEN RAISE EXCEPTION 'ConcurrencyConflict'; END IF;
                  IF p_op='Revoke' AND m."ActorId" IS NULL THEN RAISE EXCEPTION 'scope_denied'; END IF;
                  IF p_op='Revoke' AND m."State"='Revoked' THEN
                   RETURN jsonb_build_object('generation',m."Generation",'version',m."Version",'deadline',m."AccessValidUntil",'grant',NULL);
                  END IF;
                  IF m."ActorId" IS NOT NULL AND m."State"='Active' THEN
                   UPDATE public."OrganizationMemberships" SET "State"='Revoked',"Version"="Version"+1,
                    "RevocationRequestedAt"=p_at,"AccessValidUntil"=p_at+interval '2 hours' WHERE "OrganizationId"=p_org AND "ActorId"=p_actor RETURNING * INTO m;
                  END IF;
                  IF p_op='Grant' THEN
                   IF m."ActorId" IS NULL THEN
                    INSERT INTO public."OrganizationMemberships" ("OrganizationId","ActorId","Generation","Version","State","EducationalRole","PermissionFlags")
                     VALUES(p_org,p_actor,1,1,'Active',p_role,p_permissions) RETURNING * INTO m;
                   ELSE
                    IF p_at<m."RevocationRequestedAt" THEN RAISE EXCEPTION 'clock_regression'; END IF;
                    UPDATE public."OrganizationMemberships" SET "State"='Active',"Generation"="Generation"+1,"Version"="Version"+1,
                     "EducationalRole"=p_role,"PermissionFlags"=p_permissions,"RevocationRequestedAt"=NULL,"AccessValidUntil"=NULL
                     WHERE "OrganizationId"=p_org AND "ActorId"=p_actor RETURNING * INTO m;
                   END IF;
                  END IF;
                  gen:=m."Generation"; ver:=m."Version"; deadline:=m."AccessValidUntil";
                 ELSE
                  IF p_course IS NULL OR btrim(p_course)='' THEN RAISE EXCEPTION 'invalid_scope'; END IF;
                  PERFORM 1 FROM public."CourseRegistry" WHERE "CourseId"=p_course AND "OrganizationId"=p_org AND "MappingState"='Verified';
                  IF NOT FOUND THEN RAISE EXCEPTION 'scope_denied'; END IF;
                  IF p_group IS NOT NULL THEN
                   PERFORM 1 FROM public."CourseGroups" WHERE "CourseId"=p_course AND "GroupId"=p_group;
                   IF NOT FOUND THEN RAISE EXCEPTION 'scope_denied'; END IF;
                  END IF;
                  IF p_op<>'Revoke' AND (m."State" IS DISTINCT FROM 'Active' OR m."Generation" IS DISTINCT FROM p_membergen
                    OR m."EducationalRole" IS DISTINCT FROM CASE WHEN p_kind='Enrollment' THEN 'Student' ELSE 'Teacher' END) THEN RAISE EXCEPTION 'scope_denied'; END IF;
                  IF p_kind='Enrollment' THEN
                   IF p_grant IS NOT NULL THEN RAISE EXCEPTION 'invalid_scope'; END IF;
                   SELECT * INTO e FROM public."EnrollmentScopes" WHERE "StudentId"=p_actor AND "CourseId"=p_course FOR UPDATE;
                   IF e."OrganizationId" IS NOT NULL AND e."OrganizationId"<>p_org THEN RAISE EXCEPTION 'scope_denied'; END IF;
                   IF COALESCE(e."Version",0)<>p_expected THEN RAISE EXCEPTION 'ConcurrencyConflict'; END IF;
                   IF p_op='Move' AND (e."State" IS DISTINCT FROM 'Active' OR e."MembershipGeneration" IS DISTINCT FROM m."Generation"
                      OR e."GroupId" IS NULL OR p_group IS NULL OR e."GroupId"=p_group) THEN RAISE EXCEPTION 'scope_denied'; END IF;
                   IF p_op='Revoke' AND e."StudentId" IS NULL THEN RAISE EXCEPTION 'scope_denied'; END IF;
                   IF p_op='Revoke' AND e."State"='Revoked' THEN
                    RETURN jsonb_build_object('generation',e."EnrollmentGeneration",'version',e."Version",'deadline',e."AccessValidUntil",'grant',NULL);
                   END IF;
                   IF e."StudentId" IS NULL THEN
                    IF EXISTS(SELECT 1 FROM public."AccessEnrollments" WHERE "StudentId"=p_actor AND "CourseId"=p_course) THEN RAISE EXCEPTION 'legacy_scope_unclassified'; END IF;
                    INSERT INTO public."AccessEnrollments" VALUES (p_actor,p_course,p_group,true);
                    INSERT INTO public."EnrollmentScopes" ("StudentId","CourseId","OrganizationId","GroupId","MembershipGeneration","EnrollmentGeneration","State","Version")
                     VALUES(p_actor,p_course,p_org,p_group,m."Generation",1,'Active',1) RETURNING * INTO e;
                   ELSE
                    IF e."State"='Active' THEN
                     UPDATE public."EnrollmentScopes" SET "State"='Revoked',"Version"="Version"+1,"RevocationRequestedAt"=p_at,"AccessValidUntil"=p_at+interval '2 hours'
                      WHERE "StudentId"=p_actor AND "CourseId"=p_course RETURNING * INTO e;
                    END IF;
                    IF p_op<>'Revoke' THEN
                     IF p_at<e."RevocationRequestedAt" THEN RAISE EXCEPTION 'clock_regression'; END IF;
                     UPDATE public."AccessEnrollments" SET "GroupId"=p_group,"IsActive"=true WHERE "StudentId"=p_actor AND "CourseId"=p_course;
                     UPDATE public."EnrollmentScopes" SET "GroupId"=p_group,"State"='Active',"EnrollmentGeneration"="EnrollmentGeneration"+1,
                      "MembershipGeneration"=m."Generation","Version"="Version"+1,"RevocationRequestedAt"=NULL,"AccessValidUntil"=NULL
                      WHERE "StudentId"=p_actor AND "CourseId"=p_course RETURNING * INTO e;
                    ELSE
                     UPDATE public."AccessEnrollments" SET "IsActive"=false WHERE "StudentId"=p_actor AND "CourseId"=p_course;
                    END IF;
                   END IF;
                   gen:=e."EnrollmentGeneration"; ver:=e."Version"; deadline:=e."AccessValidUntil";
                  ELSE
                   IF p_op='Move' OR p_grant IS NULL OR p_grant='00000000-0000-0000-0000-000000000000' THEN RAISE EXCEPTION 'invalid_scope'; END IF;
                   SELECT * INTO g FROM public."TeacherAssignmentScopes" WHERE "GrantId"=p_grant FOR UPDATE;
                   IF g."GrantId" IS NOT NULL AND (g."TeacherId",g."OrganizationId",g."CourseId",g."GroupId") IS DISTINCT FROM (p_actor,p_org,p_course,p_group) THEN RAISE EXCEPTION 'scope_denied'; END IF;
                   IF COALESCE(g."Version",0)<>p_expected THEN RAISE EXCEPTION 'ConcurrencyConflict'; END IF;
                   IF p_op='Revoke' AND g."GrantId" IS NULL THEN RAISE EXCEPTION 'scope_denied'; END IF;
                   IF p_op='Revoke' AND g."State"='Revoked' THEN
                    RETURN jsonb_build_object('generation',g."Generation",'version',g."Version",'deadline',g."AccessValidUntil",'grant',p_grant);
                   END IF;
                   IF g."GrantId" IS NULL THEN
                    IF EXISTS(SELECT 1 FROM public."AccessTeachers" WHERE "Id"=p_grant) THEN RAISE EXCEPTION 'legacy_scope_unclassified'; END IF;
                    INSERT INTO public."AccessTeachers" VALUES(p_grant,p_actor,p_course,p_group);
                    INSERT INTO public."TeacherAssignmentScopes" ("GrantId","TeacherId","CourseId","GroupId","OrganizationId","MembershipGeneration","Generation","State","Version")
                     VALUES(p_grant,p_actor,p_course,p_group,p_org,m."Generation",1,'Active',1) RETURNING * INTO g;
                   ELSE
                    IF g."State"='Active' THEN
                     UPDATE public."TeacherAssignmentScopes" SET "State"='Revoked',"Version"="Version"+1,"RevocationRequestedAt"=p_at,"AccessValidUntil"=p_at+interval '2 hours'
                      WHERE "GrantId"=p_grant RETURNING * INTO g;
                    END IF;
                    IF p_op='Grant' THEN
                     IF p_at<g."RevocationRequestedAt" THEN RAISE EXCEPTION 'clock_regression'; END IF;
                     UPDATE public."TeacherAssignmentScopes" SET "State"='Active',"Generation"="Generation"+1,"Version"="Version"+1,
                      "MembershipGeneration"=m."Generation","RevocationRequestedAt"=NULL,"AccessValidUntil"=NULL WHERE "GrantId"=p_grant RETURNING * INTO g;
                    END IF;
                   END IF;
                   gen:=g."Generation"; ver:=g."Version"; deadline:=g."AccessValidUntil";
                  END IF;
                  resource:=p_course;
                 END IF;
                 INSERT INTO public."SecurityAudit" ("AuditId","OrganizationId","ActorId","Action","SubjectRef","ResourceRef","Result","AuthorityVersion","TraceId","OccurredAt")
                 VALUES(gen_random_uuid(),p_org,p_actor,'scope_'||lower(p_kind)||'_'||lower(p_op),p_actor::text,resource,'success',ver,gen_random_uuid()::text,p_at);
                 RETURN jsonb_build_object('generation',gen,'version',ver,'deadline',deadline,'grant',p_grant);
                END $$;
                REVOKE ALL ON FUNCTION s3_change_authority(text,text,uuid,uuid,bigint,bigint,text,text,uuid,text,integer) FROM PUBLIC;
                -- No grants to a web role here. Provision a dedicated internal capability role as documented.
                -- Owner/migration credentials must never be used as application credentials.
                REVOKE ALL ON "AuthorityHistory","AssignmentScopes" FROM PUBLIC;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("S3 authority and affiliation history must be retained; use a reviewed forward fix or restore.");
    }
}
