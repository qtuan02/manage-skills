using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHisAdmissionId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AdmissionID",
                table: "HEX_ExamRecord",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdmissionID",
                table: "HEX_ExamRecord");
        }
    }
}
