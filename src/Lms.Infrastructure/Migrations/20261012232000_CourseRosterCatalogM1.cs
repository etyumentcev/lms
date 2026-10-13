using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Migrations;

[DbContext(typeof(LmsDbContext))]
[Migration("20261012232000_CourseRosterCatalogM1")]
public sealed class CourseRosterCatalogM1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CourseCatalog",
            columns: table => new
            {
                CourseId = table.Column<string>(type: "text", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                State = table.Column<string>(type: "text", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CourseCatalog", x => x.CourseId);
                table.CheckConstraint("CK_CourseCatalog_State", "\"State\" = 'Draft' AND \"Version\" > 0");
                table.CheckConstraint("CK_CourseCatalog_Title", "char_length(btrim(\"Title\")) BETWEEN 2 AND 160");
                table.ForeignKey("FK_CourseCatalog_CourseRegistry_CourseId", x => x.CourseId,
                    "CourseRegistry", "CourseId", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CourseCatalog_Organizations_OrganizationId", x => x.OrganizationId,
                    "Organizations", "OrganizationId", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CourseGroupCatalog",
            columns: table => new
            {
                CourseId = table.Column<string>(type: "text", nullable: false),
                GroupId = table.Column<string>(type: "text", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CourseGroupCatalog", x => new { x.CourseId, x.GroupId });
                table.CheckConstraint("CK_CourseGroupCatalog_Name", "char_length(btrim(\"Name\")) BETWEEN 2 AND 120");
                table.CheckConstraint("CK_CourseGroupCatalog_Version", "\"Version\" > 0");
                table.ForeignKey("FK_CourseGroupCatalog_CourseGroups_CourseId_GroupId",
                    x => new { x.CourseId, x.GroupId }, "CourseGroups", new[] { "CourseId", "GroupId" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CourseRosterCommandReceipts",
            columns: table => new
            {
                AdministratorActorId = table.Column<Guid>(type: "uuid", nullable: false),
                IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "text", nullable: false),
                PayloadDigest = table.Column<string>(type: "text", nullable: false),
                ResultRef = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CourseRosterCommandReceipts", x => new { x.AdministratorActorId, x.IdempotencyKey });
                table.CheckConstraint("CK_CourseRosterReceipt_Digest", "char_length(\"PayloadDigest\") = 64");
                table.ForeignKey("FK_CourseRosterCommandReceipts_OrganizationMemberships_OrganizationId_AdministratorActorId",
                    x => new { x.OrganizationId, x.AdministratorActorId }, "OrganizationMemberships",
                    new[] { "OrganizationId", "ActorId" }, onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_CourseCatalog_OrganizationId_CreatedAt", "CourseCatalog",
            new[] { "OrganizationId", "CreatedAt" });
        migrationBuilder.CreateIndex("IX_CourseGroupCatalog_CourseId_Name", "CourseGroupCatalog",
            new[] { "CourseId", "Name" }, unique: true);
        migrationBuilder.CreateIndex("IX_CourseRosterCommandReceipts_OrganizationId_AdministratorActorId",
            "CourseRosterCommandReceipts", new[] { "OrganizationId", "AdministratorActorId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("M1-05 course roster history and receipts require a reviewed forward migration.");
}
