using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExamTimestampEstimatedFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ExamFinishedAtEstimated",
                table: "HEX_ExamRecord",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExamStartedAtEstimated",
                table: "HEX_ExamRecord",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExamFinishedAtEstimated",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "ExamStartedAtEstimated",
                table: "HEX_ExamRecord");
        }
    }
}
