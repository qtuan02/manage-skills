using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    // Migration viết tay: snapshot EF trên main đang lệch với module Paraclinical (ngoài phạm vi
    // PROJ-2374), `dotnet ef migrations add` sinh lẫn cả phần đó. Cùng khuôn với 20260918110000.
    /// <inheritdoc />
    public partial class AddSignStepPerformedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_schema = CURRENT_SCHEMA() 
          AND table_name = 'HEX_ExamRecordSignStep' 
          AND column_name = 'PerformedByEmployeeID'
    ) THEN
        ALTER TABLE ""HEX_ExamRecordSignStep"" ADD ""PerformedByEmployeeID"" bigint NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_schema = CURRENT_SCHEMA() 
          AND table_name = 'HEX_ExamRecordSignStep' 
          AND column_name = 'PerformedByEmployeeName'
    ) THEN
        ALTER TABLE ""HEX_ExamRecordSignStep"" ADD ""PerformedByEmployeeName"" character varying(255) NOT NULL DEFAULT '';
    END IF;
END $$;
");

            // Trước PROJ-2374 người ký luôn là người bấm ký, nên dòng cũ lấy SignedBy* làm PerformedBy*.
            // Idempotent: chỉ đụng dòng còn trống.
            migrationBuilder.Sql(
                "UPDATE \"HEX_ExamRecordSignStep\" " +
                "SET \"PerformedByEmployeeID\" = \"SignedByEmployeeID\", " +
                "    \"PerformedByEmployeeName\" = \"SignedByEmployeeName\" " +
                "WHERE \"PerformedByEmployeeID\" IS NULL AND \"SignedByEmployeeID\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PerformedByEmployeeID",
                table: "HEX_ExamRecordSignStep");

            migrationBuilder.DropColumn(
                name: "PerformedByEmployeeName",
                table: "HEX_ExamRecordSignStep");
        }
    }
}
