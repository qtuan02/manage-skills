using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthExam.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Bộ đếm cấp mã hồ sơ theo từng đợt. Thay cho cách đếm hồ sơ hiện có rồi cộng một —
    /// cách cũ cho hai request song song cùng một số, và chỉ mục UNIQUE (DivisionID,
    /// RecordCode) là thứ duy nhất chặn lại, tức là bên thua cuộc ăn lỗi.
    /// </summary>
    public partial class AddSessionRecordCounter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastRecordNo",
                table: "HEX_ExamSession",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill BẮT BUỘC: đợt đang chạy đã có sẵn hồ sơ -0001..-00NN. Để bộ đếm ở 0
            // thì ngay sau khi triển khai, hồ sơ tiếp theo của đợt đó lại được cấp -0001 và
            // đâm thẳng vào chỉ mục UNIQUE — cả đợt không đăng ký thêm được ai.
            //
            // Lấy COUNT(*) chứ không lấy số lớn nhất tách từ đuôi mã: mã hồ sơ có thể do
            // client tự truyền vào theo quy ước khác hẳn, tách chuỗi là đoán mò. COUNT(*)
            // khớp đúng với công thức mà cách cũ đã dùng để sinh ra các mã hiện có.
            // Đếm cả hồ sơ đã hủy — bộ đếm là "số đã cấp phát", không phải "số đang dùng".
            migrationBuilder.Sql("""
                UPDATE "HEX_ExamSession" s
                   SET "LastRecordNo" = (SELECT COUNT(*) FROM "HEX_ExamRecord" r
                                          WHERE r."SessionID" = s."SessionID");
                """);
            // Dấu ";" cuối câu là BẮT BUỘC dù chỉ có một câu lệnh.
            // `dotnet ef database update` chạy từng câu nên thiếu nó vẫn trót lọt — đó là
            // lý do lỗi này sống sót qua mọi lần chạy trên DEV. Nhưng
            // `dotnet ef migrations script --idempotent` bọc câu này vào một khối
            // DO $EF$ ... IF NOT EXISTS(...) THEN <câu> END IF; END $EF$;
            // và lúc đó thiếu ";" thành "…s.\"SessionID\") END IF" — PostgreSQL báo
            // `syntax error at or near "END"` và DỪNG CẢ SCRIPT.
            // Hệ quả: Job dựng schema trên DHTesting tạo được 6/9 bảng rồi chết giữa chừng.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastRecordNo",
                table: "HEX_ExamSession");
        }
    }
}
