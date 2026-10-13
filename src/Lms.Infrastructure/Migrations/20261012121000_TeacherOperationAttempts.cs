using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lms.Infrastructure.Migrations;

// SQL-owned immutable attempt identity, no EF model or public runtime grants.
[DbContext(typeof(LmsDbContext))]
[Migration("20261012121000_TeacherOperationAttempts")]
public sealed class TeacherOperationAttempts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE public."TeacherOperationAttempts" (
          "OperationId" uuid PRIMARY KEY,
          "OrganizationId" uuid NOT NULL REFERENCES public."Organizations"("OrganizationId") ON DELETE RESTRICT,
          "ActorId" uuid NOT NULL REFERENCES public."Actors"("ActorId") ON DELETE RESTRICT,
          "CommandId" uuid NOT NULL,
          "Kind" varchar(64) NOT NULL,
          "Resource" varchar(512) NOT NULL,
          "ExpectedVersion" bigint NOT NULL CHECK ("ExpectedVersion" >= 0),
          "Fingerprint" varchar(64) NOT NULL CHECK ("Fingerprint" ~ '^[0-9A-F]{64}$'),
          "Deadline" timestamptz NOT NULL,
          UNIQUE ("OrganizationId", "ActorId", "CommandId"),
          UNIQUE ("OrganizationId", "ActorId", "Kind", "Resource", "ExpectedVersion"));
        REVOKE ALL ON public."TeacherOperationAttempts" FROM PUBLIC;
        CREATE TRIGGER teacher_operation_attempt_immutable BEFORE UPDATE OR DELETE ON public."TeacherOperationAttempts"
          FOR EACH ROW EXECUTE FUNCTION public.s1_immutable_row();
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Attempt identities prevent replay; use a reviewed forward fix.");
}
