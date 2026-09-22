using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHisPdfSignTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "HisSignTransactionID",
                table: "HEX_ExamRecord",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HisSignStatus",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HisSignKeyID",
                table: "HEX_ExamRecord",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "HisSignedByEmployeeID",
                table: "HEX_ExamRecord",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HisSignedAt",
                table: "HEX_ExamRecord",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Record_HisSignTransaction",
                table: "HEX_ExamRecord",
                columns: new[] { "DivisionID", "RecordID", "HisSignKeyID" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HEX_Record_HisSignTransaction",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "HisSignTransactionID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "HisSignStatus",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "HisSignKeyID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "HisSignedByEmployeeID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "HisSignedAt",
                table: "HEX_ExamRecord");
        }
    }
}
