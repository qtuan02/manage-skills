using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHisSignedFileInfoToExamRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HisSignedFileDocID",
                table: "HEX_ExamRecord",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HisSignedFilePath",
                table: "HEX_ExamRecord",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HisSignedFileDocID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "HisSignedFilePath",
                table: "HEX_ExamRecord");
        }
    }
}
