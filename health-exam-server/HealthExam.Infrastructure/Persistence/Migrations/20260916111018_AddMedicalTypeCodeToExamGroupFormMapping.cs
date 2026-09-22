using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicalTypeCodeToExamGroupFormMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MedicalTypeCode",
                table: "HEX_ExamGroupFormMapping",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "HEX_ExamGroupFormMapping",
                keyColumn: "MappingID",
                keyValue: new Guid("f0000001-0000-0000-0000-000000000001"),
                column: "MedicalTypeCode",
                value: "KSK03");

            migrationBuilder.UpdateData(
                table: "HEX_ExamGroupFormMapping",
                keyColumn: "MappingID",
                keyValue: new Guid("f0000001-0000-0000-0000-000000000004"),
                column: "MedicalTypeCode",
                value: "KSK03");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MedicalTypeCode",
                table: "HEX_ExamGroupFormMapping");
        }
    }
}
