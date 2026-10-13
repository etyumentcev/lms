using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lms.Infrastructure.Migrations;

// No historical row changes. Legacy AccessValidUntil is now the revoke marker for new
// changes, never a lease granting fresh tokens. Existing constraint accepts older history.
[DbContext(typeof(LmsDbContext))]
[Migration("20261012120000_BoundedTeacherRevocation")]
public sealed class BoundedTeacherRevocation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE OR REPLACE FUNCTION public.s3_change_authority(p_kind text,p_op text,p_org uuid,p_actor uuid,p_expected bigint,p_membergen bigint,
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
            "RevocationRequestedAt"=p_at,"AccessValidUntil"=p_at WHERE "OrganizationId"=p_org AND "ActorId"=p_actor RETURNING * INTO m;
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
             UPDATE public."EnrollmentScopes" SET "State"='Revoked',"Version"="Version"+1,"RevocationRequestedAt"=p_at,"AccessValidUntil"=p_at
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
             UPDATE public."TeacherAssignmentScopes" SET "State"='Revoked',"Version"="Version"+1,"RevocationRequestedAt"=p_at,"AccessValidUntil"=p_at
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
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Preserve authority history; use a reviewed forward fix.");
}
