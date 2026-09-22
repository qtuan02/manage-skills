using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReshapeSignStepSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dòng HEX_ExamRecordSignStep cũ mang nghĩa "HIS đã tạo transaction ký" (SWTID) —
            // spec §5.2 đổi nghĩa dòng thành "bác sĩ đã bấm ký". Hai nghĩa không tương thích và
            // SWTID/FileDocTypeID cũ vô nghĩa với schema mới, nên bỏ toàn bộ dữ liệu HIS-era ở
            // đây thay vì cố map sang. Không ảnh hưởng nghiệp vụ: dòng mới chỉ sinh khi có người
            // bấm ký lại từ đầu sau khi lên bản deploy này.
            migrationBuilder.Sql("DELETE FROM \"HEX_ExamRecordSignStep\";");

            migrationBuilder.DropIndex(
                name: "UX_HEX_RecordSignStep_Step",
                table: "HEX_ExamRecordSignStep");

            migrationBuilder.DropIndex(
                name: "IX_HEX_Record_HisSignTransaction",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "FileDocTypeID",
                table: "HEX_ExamRecordSignStep");

            migrationBuilder.DropColumn(
                name: "LastObservedAt",
                table: "HEX_ExamRecordSignStep");

            migrationBuilder.DropColumn(
                name: "SWTID",
                table: "HEX_ExamRecordSignStep");

            migrationBuilder.DropColumn(
                name: "HisSignKeyID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "HisSignTransactionID",
                table: "HEX_ExamRecord");

            migrationBuilder.DropColumn(
                name: "HisSignedFileDocID",
                table: "HEX_ExamRecord");

            migrationBuilder.RenameColumn(
                name: "HisSignedFilePath",
                table: "HEX_ExamRecord",
                newName: "SignedFilePath");

            migrationBuilder.RenameColumn(
                name: "HisSignStatus",
                table: "HEX_ExamRecord",
                newName: "SignStatus");

            // HIS-era "InProcessing" thuộc transaction đã bị bỏ (dòng snapshot của nó vừa bị xoá
            // ở trên) nên gộp về New; HIS-era "Signed" giữ nguyên vì hồ sơ đã thực sự ký xong.
            migrationBuilder.Sql(
                "UPDATE \"HEX_ExamRecord\" SET \"SignStatus\" = 'New' WHERE \"SignStatus\" IS NULL OR \"SignStatus\" NOT IN ('Signed');");

            migrationBuilder.AlterColumn<string>(
                name: "SignStatus",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "New",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true,
                oldDefaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "HEX_ExamRecordSignStep",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Snapshot",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "Pending");

            migrationBuilder.AlterColumn<string>(
                name: "DivisionID",
                table: "HEX_ExamRecordSignStep",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<int>(
                name: "ItemGroupID",
                table: "HEX_ExamRecordSignStep",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignedByEmployeeCode",
                table: "HEX_ExamRecordSignStep",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SignedByEmployeeName",
                table: "HEX_ExamRecordSignStep",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VariantCode",
                table: "HEX_ExamRecordSignStep",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "UX_HEX_RecordSignStep_Step",
                table: "HEX_ExamRecordSignStep",
                columns: new[] { "DivisionID", "RecordID", "VariantCode", "SWStep" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down() tái tạo cột SWTID NOT NULL DEFAULT 0 và unique cũ (DivisionID, RecordID,
            // SWTID, SWStep) — cả hai đều có thể vỡ trên dữ liệu snapshot mới (sinh từ nghĩa
            // "bác sĩ đã bấm ký", không mang SWTID thật) nên dọn bảng trước, như Up() đã làm.
            migrationBuilder.Sql("DELETE FROM \"HEX_ExamRecordSignStep\";");

            migrationBuilder.DropIndex(
                name: "UX_HEX_RecordSignStep_Step",
                table: "HEX_ExamRecordSignStep");

            migrationBuilder.DropColumn(
                name: "ItemGroupID",
                table: "HEX_ExamRecordSignStep");

            migrationBuilder.DropColumn(
                name: "SignedByEmployeeCode",
                table: "HEX_ExamRecordSignStep");

            migrationBuilder.DropColumn(
                name: "SignedByEmployeeName",
                table: "HEX_ExamRecordSignStep");

            migrationBuilder.DropColumn(
                name: "VariantCode",
                table: "HEX_ExamRecordSignStep");

            migrationBuilder.RenameColumn(
                name: "SignedFilePath",
                table: "HEX_ExamRecord",
                newName: "HisSignedFilePath");

            migrationBuilder.RenameColumn(
                name: "SignStatus",
                table: "HEX_ExamRecord",
                newName: "HisSignStatus");

            migrationBuilder.AlterColumn<string>(
                name: "HisSignStatus",
                table: "HEX_ExamRecord",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: false,
                oldDefaultValue: "New");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "HEX_ExamRecordSignStep",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Pending",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "Snapshot");

            migrationBuilder.AlterColumn<string>(
                name: "DivisionID",
                table: "HEX_ExamRecordSignStep",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "FileDocTypeID",
                table: "HEX_ExamRecordSignStep",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastObservedAt",
                table: "HEX_ExamRecordSignStep",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<long>(
                name: "SWTID",
                table: "HEX_ExamRecordSignStep",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "HisSignKeyID",
                table: "HEX_ExamRecord",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "HisSignTransactionID",
                table: "HEX_ExamRecord",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HisSignedFileDocID",
                table: "HEX_ExamRecord",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_HEX_RecordSignStep_Step",
                table: "HEX_ExamRecordSignStep",
                columns: new[] { "DivisionID", "RecordID", "SWTID", "SWStep" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HEX_Record_HisSignTransaction",
                table: "HEX_ExamRecord",
                columns: new[] { "DivisionID", "RecordID", "HisSignKeyID" });
        }
    }
}
