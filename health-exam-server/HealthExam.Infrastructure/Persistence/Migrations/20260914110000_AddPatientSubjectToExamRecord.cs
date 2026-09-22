using System;
using HealthExam.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(HealthExamDbContext))]
    [Migration("20260914110000_AddPatientSubjectToExamRecord")]
    public partial class AddPatientSubjectToExamRecord : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PatientSubjectOptionID",
                table: "HEX_ExamRecord",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HEX_ExamRecord_PatientSubjectOptionID",
                table: "HEX_ExamRecord",
                column: "PatientSubjectOptionID");

            migrationBuilder.AddForeignKey(
                name: "FK_HEX_ExamRecord_HEX_MasterDataOption_PatientSubjectOptionID",
                table: "HEX_ExamRecord",
                column: "PatientSubjectOptionID",
                principalTable: "HEX_MasterDataOption",
                principalColumn: "OptionID",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HEX_ExamRecord_HEX_MasterDataOption_PatientSubjectOptionID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropIndex(
                name: "IX_HEX_ExamRecord_PatientSubjectOptionID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "PatientSubjectOptionID",
                table: "HEX_ExamRecord");
        }
    }
}
