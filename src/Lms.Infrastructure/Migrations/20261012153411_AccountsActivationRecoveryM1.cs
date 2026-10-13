using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountsActivationRecoveryM1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RequestedAt",
                table: "RecoveryRequests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.CreateTable(
                name: "AccountLinkRequests",
                columns: table => new
                {
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    InitiatorActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalRosterKey = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    EducationalRole = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    VerificationRef = table.Column<string>(type: "text", nullable: true),
                    ApproverActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountLinkRequests", x => x.RequestId);
                    table.CheckConstraint("CK_AccountLink_State", "\"State\" IN ('Requested','Approved','Rejected','Suspended') AND \"EducationalRole\" IN ('Student','Teacher') AND \"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_AccountLinkRequests_Actors_ApproverActorId",
                        column: x => x.ApproverActorId,
                        principalTable: "Actors",
                        principalColumn: "ActorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountLinkRequests_Actors_InitiatorActorId",
                        column: x => x.InitiatorActorId,
                        principalTable: "Actors",
                        principalColumn: "ActorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountLinkRequests_Actors_TargetActorId",
                        column: x => x.TargetActorId,
                        principalTable: "Actors",
                        principalColumn: "ActorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountLinkRequests_AspNetUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountLinkRequests_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationParticipantProfiles",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalRosterKey = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationParticipantProfiles", x => new { x.OrganizationId, x.ActorId });
                    table.CheckConstraint("CK_Participant_Profile", "char_length(btrim(\"ExternalRosterKey\")) BETWEEN 3 AND 128 AND char_length(btrim(\"DisplayName\")) BETWEEN 2 AND 160");
                    table.ForeignKey(
                        name: "FK_OrganizationParticipantProfiles_Actors_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Actors",
                        principalColumn: "ActorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationParticipantProfiles_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountLinkRequests_ApproverActorId",
                table: "AccountLinkRequests",
                column: "ApproverActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountLinkRequests_InitiatorActorId",
                table: "AccountLinkRequests",
                column: "InitiatorActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountLinkRequests_OrganizationId_ExternalRosterKey",
                table: "AccountLinkRequests",
                columns: new[] { "OrganizationId", "ExternalRosterKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountLinkRequests_TargetActorId",
                table: "AccountLinkRequests",
                column: "TargetActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountLinkRequests_TargetUserId",
                table: "AccountLinkRequests",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationParticipantProfiles_ActorId",
                table: "OrganizationParticipantProfiles",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationParticipantProfiles_OrganizationId_ExternalRost~",
                table: "OrganizationParticipantProfiles",
                columns: new[] { "OrganizationId", "ExternalRosterKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("M1-04 security evidence uses forward-only migrations.");
    }
}
