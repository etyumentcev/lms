using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Migrations;

public partial class M1ContentImportPublication : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "CourseCatalog" DROP CONSTRAINT "CK_CourseCatalog_State";
            ALTER TABLE "CourseCatalog" ADD CONSTRAINT "CK_CourseCatalog_State"
              CHECK ("State" IN ('Draft','Published') AND "Version" > 0);

            CREATE TABLE "ContentImportAttempts" (
              "ImportId" uuid PRIMARY KEY,
              "OrganizationId" uuid NOT NULL REFERENCES "Organizations"("OrganizationId") ON DELETE RESTRICT,
              "CourseId" text NOT NULL REFERENCES "CourseRegistry"("CourseId") ON DELETE RESTRICT,
              "OwnerActorId" uuid NOT NULL REFERENCES "Actors"("ActorId") ON DELETE RESTRICT,
              "IdempotencyKey" text NOT NULL,
              "RequestDigest" text NOT NULL,
              "SourceRevision" text NOT NULL,
              "State" text NOT NULL,
              "Version" bigint NOT NULL,
              "CreatedAt" timestamptz NOT NULL,
              "ExpiresAt" timestamptz NOT NULL,
              "DiagnosticsJson" jsonb NOT NULL,
              "StudentArtifactJson" jsonb NULL,
              "TeacherArtifactJson" jsonb NULL,
              "ReceiptJson" jsonb NULL,
              "StudentProjectionHash" text NULL,
              "PackageHash" text NULL,
              "TeacherArtifactHash" text NULL,
              "PublishedVersionId" uuid NULL,
              CONSTRAINT "CK_ContentImport_State" CHECK ("State" IN ('Validating','Ready','Invalid','Failed')),
              CONSTRAINT "CK_ContentImport_Version" CHECK ("Version" > 0 AND "ExpiresAt" > "CreatedAt"),
              CONSTRAINT "CK_ContentImport_Digests" CHECK (char_length("RequestDigest") = 64 AND
                ("State" <> 'Ready' OR (char_length("StudentProjectionHash") = 64 AND
                 char_length("PackageHash") = 64 AND char_length("TeacherArtifactHash") = 64))),
              CONSTRAINT "CK_ContentImport_Result" CHECK (
                ("State" = 'Ready' AND "StudentArtifactJson" IS NOT NULL AND "TeacherArtifactJson" IS NOT NULL AND "ReceiptJson" IS NOT NULL) OR
                ("State" <> 'Ready' AND "StudentArtifactJson" IS NULL AND "TeacherArtifactJson" IS NULL AND "ReceiptJson" IS NULL AND "PublishedVersionId" IS NULL)),
              CONSTRAINT "FK_M106_Import_CourseOrg" FOREIGN KEY ("CourseId","OrganizationId")
                REFERENCES "CourseRegistry"("CourseId","OrganizationId") ON DELETE RESTRICT DEFERRABLE INITIALLY IMMEDIATE
            );
            CREATE UNIQUE INDEX "IX_ContentImportAttempts_OwnerActorId_IdempotencyKey"
              ON "ContentImportAttempts"("OwnerActorId","IdempotencyKey");
            CREATE INDEX "IX_ContentImportAttempts_CourseId" ON "ContentImportAttempts"("CourseId");
            CREATE INDEX "IX_ContentImportAttempts_OrganizationId" ON "ContentImportAttempts"("OrganizationId");

            CREATE TABLE "ContentImportAssets" (
              "ImportId" uuid NOT NULL REFERENCES "ContentImportAttempts"("ImportId") ON DELETE RESTRICT,
              "Audience" text NOT NULL,
              "AssetPath" text NOT NULL,
              "MediaType" text NOT NULL,
              "Sha256" text NOT NULL,
              "Bytes" bytea NOT NULL,
              PRIMARY KEY ("ImportId","Audience","AssetPath"),
              CONSTRAINT "CK_ContentImportAsset_Audience" CHECK ("Audience" IN ('student','teacher')),
              CONSTRAINT "CK_ContentImportAsset_Hash" CHECK (char_length("Sha256") = 64 AND octet_length("Bytes") > 0)
            );

            CREATE TABLE "PublishedContentArtifacts" (
              "ContentVersionId" uuid PRIMARY KEY REFERENCES "PublishedContentVersions"("Id") ON DELETE RESTRICT,
              "ImportId" uuid NOT NULL REFERENCES "ContentImportAttempts"("ImportId") ON DELETE RESTRICT,
              "StudentArtifactJson" jsonb NOT NULL,
              "TeacherArtifactJson" jsonb NOT NULL,
              "StudentProjectionHash" text NOT NULL,
              "PackageHash" text NOT NULL,
              "TeacherArtifactHash" text NOT NULL,
              CONSTRAINT "CK_PublishedContentArtifact_Hashes" CHECK (char_length("StudentProjectionHash") = 64 AND
                char_length("PackageHash") = 64 AND char_length("TeacherArtifactHash") = 64)
            );
            CREATE UNIQUE INDEX "IX_PublishedContentArtifacts_ImportId" ON "PublishedContentArtifacts"("ImportId");

            CREATE TABLE "PublishedContentAssets" (
              "ContentVersionId" uuid NOT NULL REFERENCES "PublishedContentVersions"("Id") ON DELETE RESTRICT,
              "Audience" text NOT NULL,
              "AssetPath" text NOT NULL,
              "MediaType" text NOT NULL,
              "Sha256" text NOT NULL,
              "Bytes" bytea NOT NULL,
              PRIMARY KEY ("ContentVersionId","Audience","AssetPath"),
              CONSTRAINT "CK_PublishedContentAsset_Audience" CHECK ("Audience" IN ('student','teacher')),
              CONSTRAINT "CK_PublishedContentAsset_Hash" CHECK (char_length("Sha256") = 64 AND octet_length("Bytes") > 0)
            );

            CREATE TABLE "LessonGroupOpenings" (
              "CourseId" text NOT NULL,
              "GroupId" text NOT NULL,
              "ContentVersionId" uuid NOT NULL REFERENCES "PublishedContentVersions"("Id") ON DELETE RESTRICT,
              "LessonId" text NOT NULL,
              "OrganizationId" uuid NOT NULL REFERENCES "Organizations"("OrganizationId") ON DELETE RESTRICT,
              "OpenedAt" timestamptz NOT NULL,
              "OpenedBy" uuid NOT NULL REFERENCES "Actors"("ActorId") ON DELETE RESTRICT,
              PRIMARY KEY ("CourseId","GroupId","ContentVersionId","LessonId"),
              CONSTRAINT "FK_LessonGroupOpenings_CourseGroups_CourseId_GroupId"
                FOREIGN KEY ("CourseId","GroupId") REFERENCES "CourseGroups"("CourseId","GroupId") ON DELETE RESTRICT,
              CONSTRAINT "FK_M106_Opening_CourseOrg" FOREIGN KEY ("CourseId","OrganizationId")
                REFERENCES "CourseRegistry"("CourseId","OrganizationId") ON DELETE RESTRICT DEFERRABLE INITIALLY IMMEDIATE
            );
            CREATE INDEX "IX_LessonGroupOpenings_ContentVersionId" ON "LessonGroupOpenings"("ContentVersionId");
            CREATE INDEX "IX_LessonGroupOpenings_OpenedBy" ON "LessonGroupOpenings"("OpenedBy");
            CREATE INDEX "IX_LessonGroupOpenings_OrganizationId" ON "LessonGroupOpenings"("OrganizationId");

            CREATE TABLE "ContentOperationReceipts" (
              "ActorId" uuid NOT NULL REFERENCES "Actors"("ActorId") ON DELETE RESTRICT,
              "IdempotencyKey" text NOT NULL,
              "OrganizationId" uuid NOT NULL REFERENCES "Organizations"("OrganizationId") ON DELETE RESTRICT,
              "CourseId" text NOT NULL REFERENCES "CourseRegistry"("CourseId") ON DELETE RESTRICT,
              "Kind" text NOT NULL,
              "PayloadDigest" text NOT NULL,
              "ResultJson" jsonb NOT NULL,
              "CreatedAt" timestamptz NOT NULL,
              PRIMARY KEY ("ActorId","IdempotencyKey"),
              CONSTRAINT "CK_ContentOperationReceipt_Digest" CHECK (char_length("PayloadDigest") = 64),
              CONSTRAINT "FK_M106_Receipt_CourseOrg" FOREIGN KEY ("CourseId","OrganizationId")
                REFERENCES "CourseRegistry"("CourseId","OrganizationId") ON DELETE RESTRICT DEFERRABLE INITIALLY IMMEDIATE
            );
            CREATE INDEX "IX_ContentOperationReceipts_CourseId" ON "ContentOperationReceipts"("CourseId");
            CREATE INDEX "IX_ContentOperationReceipts_OrganizationId" ON "ContentOperationReceipts"("OrganizationId");

            CREATE FUNCTION m106_immutable() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
              RAISE EXCEPTION 'immutable M1-06 history' USING ERRCODE='55000';
            END $fn$;
            CREATE TRIGGER m106_import_assets_immutable BEFORE UPDATE OR DELETE ON "ContentImportAssets"
              FOR EACH ROW EXECUTE FUNCTION m106_immutable();
            CREATE TRIGGER m106_published_artifact_immutable BEFORE UPDATE OR DELETE ON "PublishedContentArtifacts"
              FOR EACH ROW EXECUTE FUNCTION m106_immutable();
            CREATE TRIGGER m106_published_assets_immutable BEFORE UPDATE OR DELETE ON "PublishedContentAssets"
              FOR EACH ROW EXECUTE FUNCTION m106_immutable();
            CREATE TRIGGER m106_opening_immutable BEFORE UPDATE OR DELETE ON "LessonGroupOpenings"
              FOR EACH ROW EXECUTE FUNCTION m106_immutable();
            CREATE TRIGGER m106_receipt_immutable BEFORE UPDATE OR DELETE ON "ContentOperationReceipts"
              FOR EACH ROW EXECUTE FUNCTION m106_immutable();

            CREATE FUNCTION m106_import_transition() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
              IF TG_OP='DELETE' THEN RAISE EXCEPTION 'immutable import attempt' USING ERRCODE='55000'; END IF;
              IF (NEW."ImportId",NEW."OrganizationId",NEW."CourseId",NEW."OwnerActorId",NEW."IdempotencyKey",
                  NEW."RequestDigest",NEW."SourceRevision",NEW."CreatedAt",NEW."ExpiresAt") IS DISTINCT FROM
                 (OLD."ImportId",OLD."OrganizationId",OLD."CourseId",OLD."OwnerActorId",OLD."IdempotencyKey",
                  OLD."RequestDigest",OLD."SourceRevision",OLD."CreatedAt",OLD."ExpiresAt")
              THEN RAISE EXCEPTION 'immutable import identity' USING ERRCODE='55000'; END IF;
              IF OLD."State"='Validating' AND NEW."State" IN ('Ready','Invalid','Failed') AND NEW."Version"=OLD."Version"+1
              THEN RETURN NEW; END IF;
              IF OLD."State"='Ready' AND NEW."State"='Ready' AND OLD."PublishedVersionId" IS NULL AND
                 NEW."PublishedVersionId" IS NOT NULL AND NEW."Version"=OLD."Version"+1 AND
                 (NEW."DiagnosticsJson",NEW."StudentArtifactJson",NEW."TeacherArtifactJson",NEW."ReceiptJson",
                  NEW."StudentProjectionHash",NEW."PackageHash",NEW."TeacherArtifactHash") IS NOT DISTINCT FROM
                 (OLD."DiagnosticsJson",OLD."StudentArtifactJson",OLD."TeacherArtifactJson",OLD."ReceiptJson",
                  OLD."StudentProjectionHash",OLD."PackageHash",OLD."TeacherArtifactHash")
              THEN RETURN NEW; END IF;
              RAISE EXCEPTION 'invalid import transition' USING ERRCODE='55000';
            END $fn$;
            CREATE TRIGGER m106_import_transition BEFORE UPDATE OR DELETE ON "ContentImportAttempts"
              FOR EACH ROW EXECUTE FUNCTION m106_import_transition();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS m106_import_transition ON "ContentImportAttempts";
            DROP FUNCTION IF EXISTS m106_import_transition();
            DROP TRIGGER IF EXISTS m106_import_assets_immutable ON "ContentImportAssets";
            DROP TRIGGER IF EXISTS m106_published_artifact_immutable ON "PublishedContentArtifacts";
            DROP TRIGGER IF EXISTS m106_published_assets_immutable ON "PublishedContentAssets";
            DROP TRIGGER IF EXISTS m106_opening_immutable ON "LessonGroupOpenings";
            DROP TRIGGER IF EXISTS m106_receipt_immutable ON "ContentOperationReceipts";
            DROP FUNCTION IF EXISTS m106_immutable();
            DROP TABLE "ContentOperationReceipts";
            DROP TABLE "LessonGroupOpenings";
            DROP TABLE "PublishedContentAssets";
            DROP TABLE "PublishedContentArtifacts";
            DROP TABLE "ContentImportAssets";
            DROP TABLE "ContentImportAttempts";
            ALTER TABLE "CourseCatalog" DROP CONSTRAINT "CK_CourseCatalog_State";
            ALTER TABLE "CourseCatalog" ADD CONSTRAINT "CK_CourseCatalog_State"
              CHECK ("State" = 'Draft' AND "Version" > 0);
            """);
    }
}
