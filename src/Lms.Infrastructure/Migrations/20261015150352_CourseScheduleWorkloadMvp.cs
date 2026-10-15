using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CourseScheduleWorkloadMvp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_CourseCatalog_CourseId_OrganizationId",
                table: "CourseCatalog",
                columns: new[] { "CourseId", "OrganizationId" });

            migrationBuilder.CreateTable(
                name: "CourseScheduleAudit",
                columns: table => new
                {
                    AuditId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    ResourceId = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    TraceId = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseScheduleAudit", x => x.AuditId);
                    table.ForeignKey(
                        name: "FK_CourseScheduleAudit_Actors_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Actors",
                        principalColumn: "ActorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseScheduleAudit_CourseCatalog_CourseId_OrganizationId",
                        columns: x => new { x.CourseId, x.OrganizationId },
                        principalTable: "CourseCatalog",
                        principalColumns: new[] { "CourseId", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseScheduleAudit_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourseScheduleCommandReceipts",
                columns: table => new
                {
                    AdministratorActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    PayloadDigest = table.Column<string>(type: "text", nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseScheduleCommandReceipts", x => new { x.AdministratorActorId, x.IdempotencyKey });
                    table.ForeignKey(
                        name: "FK_CourseScheduleCommandReceipts_CourseCatalog_CourseId_Organi~",
                        columns: x => new { x.CourseId, x.OrganizationId },
                        principalTable: "CourseCatalog",
                        principalColumns: new[] { "CourseId", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseScheduleCommandReceipts_OrganizationMemberships_Organ~",
                        columns: x => new { x.OrganizationId, x.AdministratorActorId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "OrganizationId", "ActorId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourseScheduleRules",
                columns: table => new
                {
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsoDayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    AcademicHours = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: false),
                    TimeZoneId = table.Column<string>(type: "text", nullable: false),
                    DeliveryMode = table.Column<string>(type: "text", nullable: false),
                    Location = table.Column<string>(type: "text", nullable: true),
                    OnlineUrl = table.Column<string>(type: "text", nullable: true),
                    EventKind = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    LessonId = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseScheduleRules", x => x.RuleId);
                    table.CheckConstraint("CK_CourseScheduleRule_Day", "\"IsoDayOfWeek\" BETWEEN 1 AND 7");
                    table.CheckConstraint("CK_CourseScheduleRule_Hours", "\"AcademicHours\" BETWEEN 1 AND 24");
                    table.CheckConstraint("CK_CourseScheduleRule_Kind", "\"EventKind\" IN ('Lesson','Consultation','ExtraLesson')");
                    table.CheckConstraint("CK_CourseScheduleRule_Mode", "\"DeliveryMode\" IN ('InPerson','Online','Hybrid')");
                    table.CheckConstraint("CK_CourseScheduleRule_Period", "\"EffectiveTo\" >= \"EffectiveFrom\" AND \"EffectiveTo\" <= \"EffectiveFrom\" + 731");
                    table.CheckConstraint("CK_CourseScheduleRule_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_CourseScheduleRules_Actors_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Actors",
                        principalColumn: "ActorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseScheduleRules_CourseCatalog_CourseId_OrganizationId",
                        columns: x => new { x.CourseId, x.OrganizationId },
                        principalTable: "CourseCatalog",
                        principalColumns: new[] { "CourseId", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseScheduleRules_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourseWorkloads",
                columns: table => new
                {
                    CourseId = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalAcademicHours = table.Column<int>(type: "integer", nullable: false),
                    ScheduledAcademicHours = table.Column<int>(type: "integer", nullable: false),
                    IndependentAcademicHours = table.Column<int>(type: "integer", nullable: false),
                    AcademicHourMinutes = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseWorkloads", x => x.CourseId);
                    table.CheckConstraint("CK_CourseWorkload_Hours", "\"TotalAcademicHours\" >= 0 AND \"ScheduledAcademicHours\" >= 0 AND \"IndependentAcademicHours\" >= 0 AND \"TotalAcademicHours\" = \"ScheduledAcademicHours\" + \"IndependentAcademicHours\"");
                    table.CheckConstraint("CK_CourseWorkload_Minutes", "\"AcademicHourMinutes\" BETWEEN 1 AND 180");
                    table.CheckConstraint("CK_CourseWorkload_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_CourseWorkloads_Actors_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Actors",
                        principalColumn: "ActorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseWorkloads_CourseCatalog_CourseId_OrganizationId",
                        columns: x => new { x.CourseId, x.OrganizationId },
                        principalTable: "CourseCatalog",
                        principalColumns: new[] { "CourseId", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseWorkloads_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourseScheduleExceptions",
                columns: table => new
                {
                    ExceptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    NewDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NewStartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    NewEndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    AcademicHours = table.Column<int>(type: "integer", nullable: true),
                    DeliveryMode = table.Column<string>(type: "text", nullable: true),
                    Location = table.Column<string>(type: "text", nullable: true),
                    OnlineUrl = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseScheduleExceptions", x => x.ExceptionId);
                    table.CheckConstraint("CK_CourseScheduleException_Move", "(\"Status\" = 'Cancelled' AND \"NewDate\" IS NULL AND \"NewStartTime\" IS NULL AND \"NewEndTime\" IS NULL) OR (\"Status\" = 'Moved' AND \"NewDate\" IS NOT NULL AND \"NewStartTime\" IS NOT NULL AND \"NewEndTime\" IS NOT NULL)");
                    table.CheckConstraint("CK_CourseScheduleException_Status", "\"Status\" IN ('Moved','Cancelled')");
                    table.CheckConstraint("CK_CourseScheduleException_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_CourseScheduleExceptions_Actors_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Actors",
                        principalColumn: "ActorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseScheduleExceptions_CourseCatalog_CourseId_Organizatio~",
                        columns: x => new { x.CourseId, x.OrganizationId },
                        principalTable: "CourseCatalog",
                        principalColumns: new[] { "CourseId", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseScheduleExceptions_CourseScheduleRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "CourseScheduleRules",
                        principalColumn: "RuleId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseScheduleExceptions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseScheduleAudit_ActorId",
                table: "CourseScheduleAudit",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseScheduleAudit_CourseId_OrganizationId",
                table: "CourseScheduleAudit",
                columns: new[] { "CourseId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseScheduleAudit_OrganizationId_CourseId_OccurredAt",
                table: "CourseScheduleAudit",
                columns: new[] { "OrganizationId", "CourseId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseScheduleCommandReceipts_CourseId_OrganizationId",
                table: "CourseScheduleCommandReceipts",
                columns: new[] { "CourseId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseScheduleCommandReceipts_OrganizationId_AdministratorA~",
                table: "CourseScheduleCommandReceipts",
                columns: new[] { "OrganizationId", "AdministratorActorId" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseScheduleExceptions_CourseId_OrganizationId",
                table: "CourseScheduleExceptions",
                columns: new[] { "CourseId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseScheduleExceptions_OrganizationId",
                table: "CourseScheduleExceptions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseScheduleExceptions_RuleId_OriginalDate",
                table: "CourseScheduleExceptions",
                columns: new[] { "RuleId", "OriginalDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseScheduleExceptions_UpdatedBy",
                table: "CourseScheduleExceptions",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CourseScheduleRules_CourseId_IsActive_EffectiveFrom_Effecti~",
                table: "CourseScheduleRules",
                columns: new[] { "CourseId", "IsActive", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseScheduleRules_CourseId_OrganizationId",
                table: "CourseScheduleRules",
                columns: new[] { "CourseId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseScheduleRules_OrganizationId",
                table: "CourseScheduleRules",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseScheduleRules_UpdatedBy",
                table: "CourseScheduleRules",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CourseWorkloads_CourseId_OrganizationId",
                table: "CourseWorkloads",
                columns: new[] { "CourseId", "OrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseWorkloads_OrganizationId",
                table: "CourseWorkloads",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseWorkloads_UpdatedBy",
                table: "CourseWorkloads",
                column: "UpdatedBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseScheduleAudit");

            migrationBuilder.DropTable(
                name: "CourseScheduleCommandReceipts");

            migrationBuilder.DropTable(
                name: "CourseScheduleExceptions");

            migrationBuilder.DropTable(
                name: "CourseWorkloads");

            migrationBuilder.DropTable(
                name: "CourseScheduleRules");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CourseCatalog_CourseId_OrganizationId",
                table: "CourseCatalog");
        }
    }
}
