using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lms.Infrastructure.Migrations;

// Additive: existing immutable versioned attempts and replay history stay intact.
[DbContext(typeof(LmsDbContext))]
[Migration("20261012122000_UnversionedTeacherIntents")]
public sealed class UnversionedTeacherIntents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public."TeacherOperationAttempts"
          ALTER COLUMN "ExpectedVersion" DROP NOT NULL,
          ADD COLUMN "IntentId" uuid,
          ADD CONSTRAINT teacher_intent_identity CHECK (
            ("ExpectedVersion" IS NOT NULL AND "IntentId" IS NULL) OR
            ("ExpectedVersion" IS NULL AND "IntentId" IS NOT NULL AND
             "IntentId"<>'00000000-0000-0000-0000-000000000000'::uuid));
        CREATE UNIQUE INDEX teacher_unversioned_intent ON public."TeacherOperationAttempts"
          ("OrganizationId", "ActorId", "Kind", "Resource", "IntentId") WHERE "IntentId" IS NOT NULL;
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Intent identities prevent replay; use a reviewed forward fix.");
}
