using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHisRegistrationFormState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "HisEmrDataID",
                table: "HEX_ExamRecord",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HisFormSyncError",
                table: "HEX_ExamRecord",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HisFormSyncStatus",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<Guid>(
                name: "HisFormTemplateID",
                table: "HEX_ExamRecord",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HisEmrDataID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "HisFormSyncError",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "HisFormSyncStatus",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "HisFormTemplateID",
                table: "HEX_ExamRecord");
        }
    }
}
