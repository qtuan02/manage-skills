using HealthExam.Infrastructure.Persistence;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Infrastructure.Integrations.FormServer;
using HealthExam.Infrastructure.Persistence.Repositories;
using HealthExam.Server.Service;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// Hồi quy cho hai lỗi TƯƠNG TÁC giữa H-3 (cầu nối form-server) và phần đăng ký sẵn có.
/// Cả hai chỉ lộ ra khi hai nhánh nằm cạnh nhau, nên không track nào tự bắt được.
/// </summary>
public class FormBindingRegressionTests
{
    /// <summary>
    /// Đổi Nhóm khám phải BỎ FormID đã chốt của nhóm cũ.
    ///
    /// Nếu không: FormCode bị ghi đè theo quy ước chuỗi, nhưng FormID vẫn của nhóm cũ — mà
    /// GET /form-draft ưu tiên FormID đã lưu (dùng lại, không resolve lại). Người dùng đổi
    /// nhóm khám sẽ nhận lại đúng biểu mẫu cũ, im lặng, không lỗi nào báo ra.
    /// </summary>
    [Fact]
    public async Task Doi_nhom_kham_thi_bo_FormID_da_chot_cua_nhom_cu()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(ExamSessionState.Open);
        var record = db.SeedRecord(session.SessionID);

        // Giả lập hồ sơ đã đi qua bước 2: form-server đã trả về biểu mẫu thật của DTK_01.
        var formCuaNhomCu = Guid.NewGuid();
        await StampFormAsync(db.Db, record.RecordID, formCuaNhomCu, "KSK-V1-DTK_01-V2");

        await db.Records.UpdateAsync(record.RecordID,
            new ExamRecordSaveRequest { VariantCode = "DTK_05" });

        var sau = await ReloadAsync(db.Db, record.RecordID);
        Assert.Equal("DTK_05", sau.VariantCode);
        Assert.Null(sau.FormID);          // ← chốt chính: không còn trỏ vào biểu mẫu nhóm cũ
        Assert.Null(sau.SubmissionID);
    }

    /// <summary>Giữ NGUYÊN Nhóm khám thì KHÔNG được đụng vào biểu mẫu đã chốt.</summary>
    [Fact]
    public async Task Sua_ho_so_ma_giu_nguyen_nhom_kham_thi_khong_mat_FormID()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(ExamSessionState.Open);
        var record = db.SeedRecord(session.SessionID);

        var formDaChot = Guid.NewGuid();
        await StampFormAsync(db.Db, record.RecordID, formDaChot, "KSK-V1-DTK_01-V2");

        // Sửa tên, gửi lại ĐÚNG nhóm khám cũ — đúng cách FE gửi nguyên form khi lưu.
        await db.Records.UpdateAsync(record.RecordID,
            new ExamRecordSaveRequest { FullName = "Trần Thị B", VariantCode = "DTK_01" });

        var sau = await ReloadAsync(db.Db, record.RecordID);
        Assert.Equal(formDaChot, sau.FormID);
        Assert.Equal("Trần Thị B", sau.Patient?.FullName);
    }

    /// <summary>
    /// Chốt biểu mẫu cho hồ sơ LÀ một đường ghi vào đợt, nên đợt đã đóng phải chặn — 4091.
    ///
    /// Đây là cửa ghi khó thấy nhất vì nó nằm sau một endpoint tên là GET /form-draft.
    /// Test dựng FormServerClient KHÔNG gọi được (BaseUrl rỗng): nếu chốt chặn đợt bị gỡ,
    /// luồng sẽ đi tiếp tới form-server và ném 5020 thay vì 4091 — test vẫn đỏ, nhưng đỏ
    /// bằng mã khác, và mã khác đó chính là bằng chứng chốt chặn đã mất.
    /// </summary>
    [Theory]
    [InlineData(ExamSessionState.Closed)]
    [InlineData(ExamSessionState.Cancelled)]
    public async Task Dot_da_dong_thi_khong_chot_duoc_bieu_mau_moi(ExamSessionState state)
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(state);
        var record = db.SeedRecord(session.SessionID);   // FormID còn NULL → buộc phải resolve

        var draft = NewDraftHandler(db);

        var result = await draft.HandleAsync(new GetExamFormDraftQuery(db.Ctx.DivisionId, record.RecordID));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.SessionClosed, result.Failure.Code);
    }

    /// <summary>
    /// Đợt đã đóng nhưng hồ sơ ĐÃ có FormID thì vẫn xem lại được: khoá đường GHI, không
    /// khoá đường ĐỌC. Nếu khoá cả đọc thì mọi hồ sơ của đợt đã đóng thành không mở nổi.
    /// </summary>
    [Fact]
    public async Task Dot_da_dong_nhung_ho_so_da_co_FormID_thi_khong_bi_chan_boi_4091()
    {
        using var db = new InMemoryTestDb();
        var session = db.SeedSession(ExamSessionState.Closed);
        var record = db.SeedRecord(session.SessionID);
        await StampFormAsync(db.Db, record.RecordID, Guid.NewGuid(), "KSK-V1-DTK_01-V2");

        var draft = NewDraftHandler(db);

        var ex = await Assert.ThrowsAsync<HealthExamException>(
            () => draft.HandleAsync(new GetExamFormDraftQuery(db.Ctx.DivisionId, record.RecordID)));

        // Đi qua được chốt chặn đợt và chết ở bước gọi form-server (chưa cấu hình) — 5020,
        // KHÔNG phải 4091. Đó là bằng chứng đường đọc không bị khoá nhầm.
        Assert.Equal(ErrorCodes.DependencyUnavailable, ex.ErrorCode);
    }

    /// <summary>FormServerClient chưa cấu hình BaseUrl — mọi lời gọi ra ngoài ném 5020.</summary>
    private static GetExamFormDraftHandler NewDraftHandler(InMemoryTestDb db)
    {
        var client = new FormServerClient(
            new HttpClient(),
            new FormServerOptions(),
            db.Ctx,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FormServerClient>.Instance);

        var recordRepo = new ExamRecordRepository(db.Db);
        var sessionRepo = new ExamSessionRepository(db.Db);
        var uowClean = new HealthExam.Infrastructure.Persistence.UnitOfWork(db.Db);
        var clock = new HealthExam.Application.Common.SystemClock();

        return new GetExamFormDraftHandler(recordRepo, sessionRepo, client, uowClean, clock);
    }

    private static async Task StampFormAsync(HealthExamDbContext db, Guid recordId, Guid formId, string formCode)
    {
        // .AsTracking() BẮT BUỘC: InMemoryTestDb đặt NoTracking làm mặc định (chép đúng cấu
        // hình DI thật), nên thiếu nó thì SaveChanges lặng lẽ không ghi gì và test đỏ vì đồ
        // nghề chứ không vì code sai.
        var record = await db.ExamRecords.AsTracking().FirstAsync(x => x.RecordID == recordId);
        record.FormID = formId;
        record.FormCode = formCode;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task<ExamRecord> ReloadAsync(HealthExamDbContext db, Guid recordId)
        => await db.ExamRecords.AsNoTracking().Include(x => x.Patient).FirstAsync(x => x.RecordID == recordId);
}
