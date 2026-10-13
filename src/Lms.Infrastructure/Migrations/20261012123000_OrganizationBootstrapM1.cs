using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lms.Infrastructure.Migrations;

// M1-03 is additive. Existing organizations remain identifiable by UUID and may
// keep a null display name; only the controlled bootstrap writer creates receipts.
[DbContext(typeof(LmsDbContext))]
[Migration("20261012123000_OrganizationBootstrapM1")]
public sealed class OrganizationBootstrapM1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "OrganizationProfiles", columns: table => new
        {
            OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
            Name = table.Column<string>(type: "text", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_OrganizationProfiles", x => x.OrganizationId);
            table.CheckConstraint("CK_OrganizationProfile_Name", "char_length(btrim(\"Name\")) BETWEEN 2 AND 160");
            table.ForeignKey("FK_OrganizationProfiles_Organizations", x => x.OrganizationId,
                "Organizations", "OrganizationId", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateTable(name: "OrganizationBootstrapReceipts", columns: table => new
        {
            CommandId = table.Column<Guid>(type: "uuid", nullable: false),
            PayloadDigest = table.Column<string>(type: "text", nullable: false),
            OperatorSubjectHash = table.Column<string>(type: "text", nullable: false),
            OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
            AdminUserId = table.Column<Guid>(type: "uuid", nullable: false),
            AdminActorId = table.Column<Guid>(type: "uuid", nullable: false),
            ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
            CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_OrganizationBootstrapReceipts", x => x.CommandId);
            table.CheckConstraint("CK_BootstrapReceipt_Digests",
                "char_length(\"PayloadDigest\") = 64 AND char_length(\"OperatorSubjectHash\") = 64");
            table.ForeignKey("FK_BootstrapReceipt_Organization", x => x.OrganizationId,
                "Organizations", "OrganizationId", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_BootstrapReceipt_User", x => x.AdminUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_BootstrapReceipt_Actor", x => x.AdminActorId,
                "Actors", "ActorId", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_BootstrapReceipt_Challenge", x => x.ChallengeId,
                "AccessChallenges", "ChallengeId", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex(name: "IX_OrganizationBootstrapReceipts_OrganizationId",
            table: "OrganizationBootstrapReceipts", column: "OrganizationId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_OrganizationBootstrapReceipts_AdminUserId",
            table: "OrganizationBootstrapReceipts", column: "AdminUserId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_OrganizationBootstrapReceipts_AdminActorId",
            table: "OrganizationBootstrapReceipts", column: "AdminActorId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_OrganizationBootstrapReceipts_ChallengeId",
            table: "OrganizationBootstrapReceipts", column: "ChallengeId", unique: true);
        migrationBuilder.Sql("""
            CREATE TRIGGER m1_bootstrap_receipt_immutable
            BEFORE UPDATE OR DELETE ON public."OrganizationBootstrapReceipts"
            FOR EACH ROW EXECUTE FUNCTION public.s1_immutable_row();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Bootstrap receipts are immutable security evidence; use a reviewed forward fix.");
}
