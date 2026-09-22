using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientProfileVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HEX_Patient_Identity",
                table: "HEX_Patient");

            migrationBuilder.DropIndex(
                name: "UX_HEX_Patient_Division_HisPatientID",
                table: "HEX_Patient");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "HEX_Patient",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousPatientRefID",
                table: "HEX_Patient",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"
WITH ranked AS (
    SELECT p.""PatientRefID"",
           ROW_NUMBER() OVER (
             PARTITION BY p.""DivisionID"", p.""IdentityNumber""
             ORDER BY MAX(er.""ModifiedDate"") DESC NULLS LAST,
                      p.""ModifiedDate"" DESC,
                      p.""PatientRefID"") AS rn
    FROM ""HEX_Patient"" p
    LEFT JOIN ""HEX_ExamRecord"" er ON er.""PatientRefID"" = p.""PatientRefID""
    WHERE p.""IdentityNumber"" <> ''
    GROUP BY p.""PatientRefID"", p.""DivisionID"", p.""IdentityNumber"", p.""ModifiedDate""
)
UPDATE ""HEX_Patient"" p
SET ""IsActive"" = (ranked.rn = 1)
FROM ranked
WHERE p.""PatientRefID"" = ranked.""PatientRefID"";
");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Patient_PreviousPatientRefID",
                table: "HEX_Patient",
                column: "PreviousPatientRefID");

            migrationBuilder.CreateIndex(
                name: "UX_HEX_Patient_Division_ActiveIdentity",
                table: "HEX_Patient",
                columns: new[] { "DivisionID", "IdentityNumber" },
                unique: true,
                filter: "\"IsActive\" AND \"IdentityNumber\" <> ''");

            migrationBuilder.CreateIndex(
                name: "UX_HEX_Patient_Division_HisPatientID",
                table: "HEX_Patient",
                columns: new[] { "DivisionID", "HisPatientID" },
                unique: true,
                filter: "\"IsActive\" AND \"HisPatientID\" IS NOT NULL AND \"HisPatientID\" > 0");

            migrationBuilder.AddForeignKey(
                name: "FK_HEX_Patient_HEX_Patient_PreviousPatientRefID",
                table: "HEX_Patient",
                column: "PreviousPatientRefID",
                principalTable: "HEX_Patient",
                principalColumn: "PatientRefID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HEX_Patient_HEX_Patient_PreviousPatientRefID",
                table: "HEX_Patient");

            migrationBuilder.DropIndex(
                name: "IX_HEX_Patient_PreviousPatientRefID",
                table: "HEX_Patient");

            migrationBuilder.DropIndex(
                name: "UX_HEX_Patient_Division_ActiveIdentity",
                table: "HEX_Patient");

            migrationBuilder.DropIndex(
                name: "UX_HEX_Patient_Division_HisPatientID",
                table: "HEX_Patient");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "HEX_Patient");

            migrationBuilder.DropColumn(
                name: "PreviousPatientRefID",
                table: "HEX_Patient");

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Patient_Identity",
                table: "HEX_Patient",
                columns: new[] { "DivisionID", "IdentityNumber" });

            migrationBuilder.CreateIndex(
                name: "UX_HEX_Patient_Division_HisPatientID",
                table: "HEX_Patient",
                columns: new[] { "DivisionID", "HisPatientID" },
                unique: true,
                filter: "\"HisPatientID\" IS NOT NULL AND \"HisPatientID\" > 0");
        }
    }
}
