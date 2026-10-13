using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CourseEditorAuthorityM111Fix01 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CourseEditorScopes",
                columns: table => new
                {
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EditorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipGeneration = table.Column<long>(type: "bigint", nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RevocationRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AccessValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseEditorScopes", x => x.GrantId);
                    table.CheckConstraint("CK_CourseEditorScope_Deadline", "(\"RevocationRequestedAt\" IS NULL AND \"AccessValidUntil\" IS NULL AND \"State\" = 'Active') OR (\"RevocationRequestedAt\" IS NOT NULL AND \"AccessValidUntil\" IS NOT NULL AND \"State\" = 'Revoked' AND \"AccessValidUntil\" <= \"RevocationRequestedAt\" + interval '2 hours')");
                    table.CheckConstraint("CK_CourseEditorScope_Generation", "\"Generation\" > 0 AND \"Version\" > 0 AND \"MembershipGeneration\" > 0");
                    table.CheckConstraint("CK_CourseEditorScope_State", "\"State\" IN ('Active','Revoked')");
                    table.ForeignKey(
                        name: "FK_CourseEditorScopes_CourseRegistry_CourseId",
                        column: x => x.CourseId,
                        principalTable: "CourseRegistry",
                        principalColumn: "CourseId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseEditorScopes_OrganizationMemberships_OrganizationId_E~",
                        columns: x => new { x.OrganizationId, x.EditorId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "OrganizationId", "ActorId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                ALTER TABLE "CourseEditorScopes" ADD CONSTRAINT "FK_CourseEditorScope_CourseOrganization"
                  FOREIGN KEY ("CourseId", "OrganizationId")
                  REFERENCES "CourseRegistry" ("CourseId", "OrganizationId") ON DELETE RESTRICT;

                CREATE FUNCTION m111_course_editor_guard() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                  IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'course_editor_history_immutable'; END IF;
                  IF NEW."GrantId" <> OLD."GrantId" OR NEW."EditorId" <> OLD."EditorId" OR
                     NEW."CourseId" <> OLD."CourseId" OR NEW."OrganizationId" <> OLD."OrganizationId" THEN
                    RAISE EXCEPTION 'course_editor_scope_key_immutable';
                  END IF;
                  IF NEW."Version" <> OLD."Version" + 1 THEN RAISE EXCEPTION 'course_editor_scope_version'; END IF;
                  IF OLD."State" = 'Active' AND NEW."State" = 'Revoked' THEN
                    IF NEW."Generation" <> OLD."Generation" OR NEW."RevocationRequestedAt" IS NULL OR
                       NEW."AccessValidUntil" IS NULL THEN RAISE EXCEPTION 'course_editor_scope_transition'; END IF;
                  ELSIF NEW."State" = 'Active' AND OLD."State" IN ('Active', 'Revoked') THEN
                    IF NEW."Generation" <> OLD."Generation" + 1 OR
                       NEW."RevocationRequestedAt" IS NOT NULL OR NEW."AccessValidUntil" IS NOT NULL THEN
                      RAISE EXCEPTION 'course_editor_scope_transition';
                    END IF;
                  ELSE
                    RAISE EXCEPTION 'course_editor_scope_transition';
                  END IF;
                  RETURN NEW;
                END $fn$;
                CREATE TRIGGER m111_course_editor_guard BEFORE UPDATE OR DELETE ON "CourseEditorScopes"
                  FOR EACH ROW EXECUTE FUNCTION m111_course_editor_guard();
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CourseEditorScopes_CourseId",
                table: "CourseEditorScopes",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseEditorScopes_EditorId_CourseId",
                table: "CourseEditorScopes",
                columns: new[] { "EditorId", "CourseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseEditorScopes_OrganizationId_CourseId_State",
                table: "CourseEditorScopes",
                columns: new[] { "OrganizationId", "CourseId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseEditorScopes_OrganizationId_EditorId",
                table: "CourseEditorScopes",
                columns: new[] { "OrganizationId", "EditorId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseEditorScopes");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS m111_course_editor_guard();");
        }
    }
}
