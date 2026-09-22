using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientProfileVersionNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProfileLineageID",
                table: "HEX_Patient",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VersionNumber",
                table: "HEX_Patient",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(@"
WITH RECURSIVE lineage AS (
    SELECT p.""PatientRefID"" AS node_id,
           p.""PatientRefID"" AS root_id
    FROM ""HEX_Patient"" p
    WHERE p.""PreviousPatientRefID"" IS NULL

    UNION ALL

    SELECT child.""PatientRefID"" AS node_id,
           lineage.root_id
    FROM ""HEX_Patient"" child
    JOIN lineage ON child.""PreviousPatientRefID"" = lineage.node_id
), ranked AS (
    SELECT p.""PatientRefID"",
           COALESCE(lineage.root_id, p.""PatientRefID"") AS root_id,
           ROW_NUMBER() OVER (
               PARTITION BY p.""DivisionID"", COALESCE(lineage.root_id, p.""PatientRefID"")
               ORDER BY p.""CreatedDate"", p.""PatientRefID"") AS version_number
    FROM ""HEX_Patient"" p
    LEFT JOIN lineage ON lineage.node_id = p.""PatientRefID""
)
UPDATE ""HEX_Patient"" p
SET ""ProfileLineageID"" = ranked.root_id,
    ""VersionNumber"" = ranked.version_number
FROM ranked
WHERE p.""PatientRefID"" = ranked.""PatientRefID"";
");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProfileLineageID",
                table: "HEX_Patient",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "VersionNumber",
                table: "HEX_Patient",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_HEX_Patient_Division_Lineage_Version",
                table: "HEX_Patient",
                columns: new[] { "DivisionID", "ProfileLineageID", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_HEX_Patient_Division_Lineage_Version",
                table: "HEX_Patient");

            migrationBuilder.DropColumn(
                name: "ProfileLineageID",
                table: "HEX_Patient");

            migrationBuilder.DropColumn(
                name: "VersionNumber",
                table: "HEX_Patient");
        }
    }
}
