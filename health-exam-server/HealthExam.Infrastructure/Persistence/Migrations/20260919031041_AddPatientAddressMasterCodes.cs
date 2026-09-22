using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations;

public partial class AddPatientAddressMasterCodes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("ProvinceCode", "HEX_Patient", type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("WardCode", "HEX_Patient", type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "");
        migrationBuilder.Sql("""
            UPDATE "HEX_Patient" p
            SET "ProvinceCode" = COALESCE(latest."ProvinceCode", ''),
                "WardCode" = COALESCE(latest."WardCode", '')
            FROM (
                SELECT DISTINCT ON ("PatientRefID") "PatientRefID", "ProvinceCode", "WardCode"
                FROM "HEX_ExamRecord"
                WHERE COALESCE("ProvinceCode", '') <> '' OR COALESCE("WardCode", '') <> ''
                ORDER BY "PatientRefID", "CreatedDate" DESC, "RecordID" DESC
            ) latest
            WHERE p."PatientRefID" = latest."PatientRefID";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("ProvinceCode", "HEX_Patient");
        migrationBuilder.DropColumn("WardCode", "HEX_Patient");
    }
}
