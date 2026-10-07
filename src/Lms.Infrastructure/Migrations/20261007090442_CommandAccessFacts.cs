using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CommandAccessFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEligible = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessAttachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccessEnrollments",
                columns: table => new
                {
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<string>(type: "text", nullable: false),
                    GroupId = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessEnrollments", x => new { x.StudentId, x.CourseId });
                });

            migrationBuilder.CreateTable(
                name: "AccessLessons",
                columns: table => new
                {
                    CourseId = table.Column<string>(type: "text", nullable: false),
                    CourseVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LessonId = table.Column<string>(type: "text", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    AvailableFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessLessons", x => new { x.CourseId, x.CourseVersionId, x.LessonId });
                });

            migrationBuilder.CreateTable(
                name: "AccessTeachers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<string>(type: "text", nullable: false),
                    GroupId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessTeachers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessTeachers_TeacherId_CourseId",
                table: "AccessTeachers",
                columns: new[] { "TeacherId", "CourseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessAttachments");

            migrationBuilder.DropTable(
                name: "AccessEnrollments");

            migrationBuilder.DropTable(
                name: "AccessLessons");

            migrationBuilder.DropTable(
                name: "AccessTeachers");
        }
    }
}
