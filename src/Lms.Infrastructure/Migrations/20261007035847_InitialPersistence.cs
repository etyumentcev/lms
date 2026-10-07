using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProblemAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<string>(type: "text", nullable: false),
                    CourseVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LessonId = table.Column<string>(type: "text", nullable: false),
                    ProblemId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProblemAssignments", x => x.Id);
                    table.UniqueConstraint("AK_ProblemAssignments_Id_StudentId", x => new { x.Id, x.StudentId });
                    table.CheckConstraint("CK_Assignment_Status", "\"Status\" IN ('Active', 'Withdrawn')");
                });

            migrationBuilder.CreateTable(
                name: "ProblemWorks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProblemAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProblemWorks", x => x.Id);
                    table.CheckConstraint("CK_Work_Status", "\"Status\" IN ('NotStarted', 'Submitted', 'NeedsRevision', 'Accepted')");
                    table.CheckConstraint("CK_Work_Version", "\"Version\" >= 0");
                    table.ForeignKey(
                        name: "FK_ProblemWorks_ProblemAssignments_ProblemAssignmentId_Student~",
                        columns: x => new { x.ProblemAssignmentId, x.StudentId },
                        principalTable: "ProblemAssignments",
                        principalColumns: new[] { "Id", "StudentId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DomainEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProblemWorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DomainEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DomainEvents_ProblemWorks_ProblemWorkId",
                        column: x => x.ProblemWorkId,
                        principalTable: "ProblemWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProblemWorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorRole = table.Column<string>(type: "text", nullable: false),
                    BodyMarkdown = table.Column<string>(type: "text", nullable: false),
                    ReplyToMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttachmentIds = table.Column<Guid[]>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.UniqueConstraint("AK_Messages_ProblemWorkId_Id", x => new { x.ProblemWorkId, x.Id });
                    table.CheckConstraint("CK_Message_Role", "\"AuthorRole\" IN ('Student', 'Teacher')");
                    table.ForeignKey(
                        name: "FK_Messages_Messages_ProblemWorkId_ReplyToMessageId",
                        columns: x => new { x.ProblemWorkId, x.ReplyToMessageId },
                        principalTable: "Messages",
                        principalColumns: new[] { "ProblemWorkId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Messages_ProblemWorks_ProblemWorkId",
                        column: x => x.ProblemWorkId,
                        principalTable: "ProblemWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Submissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProblemWorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    BodyMarkdown = table.Column<string>(type: "text", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    AttachmentIds = table.Column<Guid[]>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Submissions", x => x.Id);
                    table.UniqueConstraint("AK_Submissions_ProblemWorkId_Id", x => new { x.ProblemWorkId, x.Id });
                    table.CheckConstraint("CK_Submission_Attempt", "\"AttemptNumber\" > 0");
                    table.ForeignKey(
                        name: "FK_Submissions_ProblemWorks_ProblemWorkId",
                        column: x => x.ProblemWorkId,
                        principalTable: "ProblemWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProblemWorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<string>(type: "text", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reviews", x => x.Id);
                    table.CheckConstraint("CK_Review_Decision", "\"Decision\" IN ('Accepted', 'NeedsRevision')");
                    table.ForeignKey(
                        name: "FK_Reviews_Messages_ProblemWorkId_MessageId",
                        columns: x => new { x.ProblemWorkId, x.MessageId },
                        principalTable: "Messages",
                        principalColumns: new[] { "ProblemWorkId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reviews_ProblemWorks_ProblemWorkId",
                        column: x => x.ProblemWorkId,
                        principalTable: "ProblemWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reviews_Submissions_ProblemWorkId_SubmissionId",
                        columns: x => new { x.ProblemWorkId, x.SubmissionId },
                        principalTable: "Submissions",
                        principalColumns: new[] { "ProblemWorkId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DomainEvents_ProblemWorkId_Version_Ordinal",
                table: "DomainEvents",
                columns: new[] { "ProblemWorkId", "Version", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ProblemWorkId_ReplyToMessageId",
                table: "Messages",
                columns: new[] { "ProblemWorkId", "ReplyToMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProblemWorks_ProblemAssignmentId",
                table: "ProblemWorks",
                column: "ProblemAssignmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProblemWorks_ProblemAssignmentId_StudentId",
                table: "ProblemWorks",
                columns: new[] { "ProblemAssignmentId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_ProblemWorkId_MessageId",
                table: "Reviews",
                columns: new[] { "ProblemWorkId", "MessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_ProblemWorkId_SubmissionId",
                table: "Reviews",
                columns: new[] { "ProblemWorkId", "SubmissionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_SubmissionId",
                table: "Reviews",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ProblemWorkId_AttemptNumber",
                table: "Submissions",
                columns: new[] { "ProblemWorkId", "AttemptNumber" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION reject_history_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'History is immutable' USING ERRCODE = '23514';
                END;
                $$;
                CREATE TRIGGER immutable_submission BEFORE UPDATE OR DELETE ON "Submissions"
                    FOR EACH ROW EXECUTE FUNCTION reject_history_change();
                CREATE TRIGGER immutable_review BEFORE UPDATE OR DELETE ON "Reviews"
                    FOR EACH ROW EXECUTE FUNCTION reject_history_change();
                CREATE TRIGGER immutable_message BEFORE UPDATE OR DELETE ON "Messages"
                    FOR EACH ROW EXECUTE FUNCTION reject_history_change();
                CREATE TRIGGER immutable_event BEFORE UPDATE OR DELETE ON "DomainEvents"
                    FOR EACH ROW EXECUTE FUNCTION reject_history_change();
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION protect_assignment_reference() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF (NEW."StudentId", NEW."CourseId", NEW."CourseVersionId", NEW."LessonId", NEW."ProblemId")
                       IS DISTINCT FROM
                       (OLD."StudentId", OLD."CourseId", OLD."CourseVersionId", OLD."LessonId", OLD."ProblemId") THEN
                        RAISE EXCEPTION 'Assignment reference is immutable' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER immutable_assignment_reference BEFORE UPDATE ON "ProblemAssignments"
                    FOR EACH ROW EXECUTE FUNCTION protect_assignment_reference();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DomainEvents");

            migrationBuilder.DropTable(
                name: "Reviews");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "Submissions");

            migrationBuilder.DropTable(
                name: "ProblemWorks");

            migrationBuilder.DropTable(
                name: "ProblemAssignments");
            migrationBuilder.Sql("DROP FUNCTION reject_history_change(); DROP FUNCTION protect_assignment_reference();");
        }
    }
}
