using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ImmutableContentSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublishedContentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<string>(type: "text", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    SourceRevision = table.Column<string>(type: "text", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ContentJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublishedContentVersions", x => x.Id);
                    table.CheckConstraint("CK_Content_Version", "\"VersionNumber\" > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublishedContentVersions_CourseId_VersionNumber",
                table: "PublishedContentVersions",
                columns: new[] { "CourseId", "VersionNumber" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION reject_content_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Published content is immutable' USING ERRCODE = '23514';
                END $$;
                CREATE TRIGGER immutable_content BEFORE UPDATE OR DELETE ON "PublishedContentVersions"
                    FOR EACH ROW EXECUTE FUNCTION reject_content_mutation();
                CREATE FUNCTION validate_content_snapshot() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE lesson jsonb; problem jsonb;
                BEGIN
                    IF jsonb_typeof(NEW."ContentJson") IS DISTINCT FROM 'object'
                        OR jsonb_typeof(NEW."ContentJson"->'title') IS DISTINCT FROM 'string'
                        OR jsonb_typeof(NEW."ContentJson"->'lessons') IS DISTINCT FROM 'array' THEN
                        RAISE EXCEPTION 'Invalid content snapshot' USING ERRCODE = '23514';
                    END IF;
                    FOR lesson IN SELECT value FROM jsonb_array_elements(NEW."ContentJson"->'lessons') LOOP
                        IF jsonb_typeof(lesson->'id') IS DISTINCT FROM 'string'
                            OR jsonb_typeof(lesson->'title') IS DISTINCT FROM 'string'
                            OR jsonb_typeof(lesson->'order') IS DISTINCT FROM 'number'
                            OR jsonb_typeof(lesson->'problems') IS DISTINCT FROM 'array' THEN
                            RAISE EXCEPTION 'Invalid lesson snapshot' USING ERRCODE = '23514';
                        END IF;
                        FOR problem IN SELECT value FROM jsonb_array_elements(lesson->'problems') LOOP
                            IF jsonb_typeof(problem->'id') IS DISTINCT FROM 'string'
                                OR jsonb_typeof(problem->'order') IS DISTINCT FROM 'number'
                                OR jsonb_typeof(problem->'statementMarkdown') IS DISTINCT FROM 'string' THEN
                                RAISE EXCEPTION 'Invalid problem snapshot' USING ERRCODE = '23514';
                            END IF;
                        END LOOP;
                    END LOOP;
                    IF EXISTS (SELECT 1 FROM jsonb_array_elements(NEW."ContentJson"->'lessons') l
                        GROUP BY l->>'id' HAVING count(*) > 1)
                        OR EXISTS (SELECT 1 FROM jsonb_array_elements(NEW."ContentJson"->'lessons') l,
                            LATERAL jsonb_array_elements(l->'problems') p GROUP BY p->>'id' HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'Duplicate content reference' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER validate_content BEFORE INSERT ON "PublishedContentVersions"
                    FOR EACH ROW EXECUTE FUNCTION validate_content_snapshot();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PublishedContentVersions");
            migrationBuilder.Sql("DROP FUNCTION reject_content_mutation(); DROP FUNCTION validate_content_snapshot();");
        }
    }
}
