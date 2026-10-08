using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AttachmentStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoredAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    MediaType = table.Column<string>(type: "text", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredAttachments", x => x.Id);
                    table.CheckConstraint("CK_StoredAttachment_Link", "NOT (\"SubmissionId\" IS NOT NULL AND \"MessageId\" IS NOT NULL) AND (NOT \"Deleted\" OR (\"SubmissionId\" IS NULL AND \"MessageId\" IS NULL))");
                    table.CheckConstraint("CK_StoredAttachment_Size", "\"Size\" > 0");
                    table.ForeignKey(
                        name: "FK_StoredAttachments_AccessAttachments_Id",
                        column: x => x.Id,
                        principalTable: "AccessAttachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoredAttachments_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoredAttachments_Submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "Submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoredAttachments_MessageId",
                table: "StoredAttachments",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_StoredAttachments_StorageKey",
                table: "StoredAttachments",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredAttachments_SubmissionId",
                table: "StoredAttachments",
                column: "SubmissionId");
            // Existing C1 history snapshots are unchanged. Claim real stored resources atomically
            // on INSERT, including existing Message attachment semantics.
            migrationBuilder.Sql("""
                CREATE FUNCTION claim_stored_attachment() RETURNS trigger AS $$
                DECLARE attachment_id uuid; stored "StoredAttachments"%ROWTYPE; owner_id uuid;
                BEGIN
                  IF TG_TABLE_NAME = 'Submissions' THEN owner_id := NEW."SubmittedBy";
                  ELSE owner_id := NEW."AuthorId"; END IF;
                  FOR attachment_id IN SELECT DISTINCT unnest(NEW."AttachmentIds") ORDER BY 1 LOOP
                    SELECT * INTO stored FROM "StoredAttachments" WHERE "Id" = attachment_id FOR UPDATE;
                    IF NOT FOUND THEN CONTINUE; END IF;
                    IF stored."OwnerId" <> owner_id OR stored."Deleted" THEN
                      RAISE EXCEPTION 'AttachmentNotOwned' USING ERRCODE = 'P0001';
                    END IF;
                    IF stored."SubmissionId" IS NOT NULL OR stored."MessageId" IS NOT NULL THEN
                      RAISE EXCEPTION 'AttachmentAlreadyAttached' USING ERRCODE = 'P0001';
                    END IF;
                    IF TG_TABLE_NAME = 'Submissions' THEN
                      UPDATE "StoredAttachments" SET "SubmissionId" = NEW."Id" WHERE "Id" = attachment_id;
                    ELSE
                      UPDATE "StoredAttachments" SET "MessageId" = NEW."Id" WHERE "Id" = attachment_id;
                    END IF;
                  END LOOP;
                  RETURN NEW;
                END $$ LANGUAGE plpgsql;
                CREATE TRIGGER claim_submission_attachments AFTER INSERT ON "Submissions"
                  FOR EACH ROW EXECUTE FUNCTION claim_stored_attachment();
                CREATE TRIGGER claim_message_attachments AFTER INSERT ON "Messages"
                  FOR EACH ROW EXECUTE FUNCTION claim_stored_attachment();
                CREATE FUNCTION protect_stored_attachment() RETURNS trigger AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'Stored attachment metadata is retained';
                  END IF;
                  IF (NEW."Id", NEW."OwnerId", NEW."FileName", NEW."MediaType", NEW."Size", NEW."StorageKey", NEW."CreatedAt")
                     IS DISTINCT FROM
                     (OLD."Id", OLD."OwnerId", OLD."FileName", OLD."MediaType", OLD."Size", OLD."StorageKey", OLD."CreatedAt")
                     OR (OLD."Deleted" AND NOT NEW."Deleted")
                     OR ((OLD."SubmissionId" IS NOT NULL OR OLD."MessageId" IS NOT NULL) AND NEW IS DISTINCT FROM OLD) THEN
                    RAISE EXCEPTION 'Stored attachment is immutable';
                  END IF;
                  RETURN NEW;
                END $$ LANGUAGE plpgsql;
                CREATE TRIGGER protect_stored_attachment BEFORE UPDATE OR DELETE ON "StoredAttachments"
                  FOR EACH ROW EXECUTE FUNCTION protect_stored_attachment();
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER claim_submission_attachments ON "Submissions";
                DROP TRIGGER claim_message_attachments ON "Messages";
                DROP FUNCTION claim_stored_attachment();
                DROP TRIGGER protect_stored_attachment ON "StoredAttachments";
                DROP FUNCTION protect_stored_attachment();
                """);

            migrationBuilder.DropTable(
                name: "StoredAttachments");
        }
    }
}
