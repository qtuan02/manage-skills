using System.Net;
using System.Text;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.API.Contracts;
using HealthExam.Application.Webhooks;
using HealthExam.Infrastructure.Integrations.FormServer;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Application.Signing;
using HealthExam.Domain.ExamForms;
using HealthExam.Server.Service;
using HealthExam.Tests.Signing;
using Newtonsoft.Json;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// H3-04 + H3-05 — điều kiện (B), AND với (A), và lệnh ký kết luận.
///
/// (B) là một câu ĐẾM chứ không phải cột cờ, nên bộ test này canh đúng thứ đó: đổi trạng thái
/// một dòng dịch vụ là (B) đổi NGAY, không cần job nào chạy và không có cờ nào phải hạ.
/// </summary>
public class ConclusionEligibilityTests
{
    private static readonly Guid SubmissionId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000009");
    private const long ConclusionSectionId = 777;

    private static (InMemoryTestDb Db, ExamRecord Record) Fixture(bool withSubmission = true)
    {
        var db = new InMemoryTestDb();
        var session = db.SeedSession();
        var record = db.SeedRecord(session.SessionID, ExamRecordState.InProgress,
            submissionId: withSubmission ? SubmissionId : null);
        return (db, record);
    }

    // ───── Điều kiện (A) giờ đọc từ ISignStepMapRepository + snapshot, không còn qua HIS ─────
    // GetConclusionEligibilityHandler bỏ IResolveHisConclusionContextHandler (Task 8). Các test
    // (B) dưới đây chỉ muốn cô lập (B), nên seed sẵn (A) = đủ bằng một map 1-bước-lâm-sàng +
    // 1-bước-kết-luận rồi ký số bước lâm sàng, thay vì test HIS mock cũ.
    private const long ClinicalRoleId = 45;
    private const long ConclusionRoleId = 60;
    private const int ClinicalItemGroupId = 101;

    private static FakeSignStepMapRepository BuildMap(string divisionId, string variantCode) => new()
    {
        Steps =
        {
            new SignStepMap
            {
                ID = Guid.NewGuid(), DivisionID = divisionId, VariantCode = variantCode,
                SWStep = 1, ItemGroupID = ClinicalItemGroupId, StepName = "Khám lâm sàng",
                SignTitle = "Khám lâm sàng", SWRoleID = ClinicalRoleId, SignType = 1, SLType = 2,
                SearchPattern = "##{S1}##", IsConclusionStep = false, IsActive = true
            },
            new SignStepMap
            {
                ID = Guid.NewGuid(), DivisionID = divisionId, VariantCode = variantCode,
                SWStep = 2, ItemGroupID = null, StepName = "Kết luận", SignTitle = "Kết luận",
                SWRoleID = ConclusionRoleId, SignType = 1, SLType = 2,
                SearchPattern = "##{S2}##", IsConclusionStep = true, IsActive = true
            }
        }
    };

    /// <summary>Dựng map (A) rồi ký số bước lâm sàng — dùng khi test chỉ muốn cô lập (B).</summary>
    private static async Task<FakeSignStepMapRepository> SeedSatisfiedConditionA(InMemoryTestDb db, ExamRecord record)
    {
        var map = BuildMap(record.DivisionID, record.VariantCode);
        var cert = new FakeCertificateGateway();
        cert.WithCertificate.Add("NV001");
        var his = new HealthExam.Tests.Signing.FakeSignRoleEmployeesHisClient().Add(ClinicalRoleId, 1274, "NV001", "BS A");
        await db.SignExamSection(map, cert, his).HandleAsync(new SignExamSectionCommand(
            record.DivisionID, record.RecordID, ClinicalItemGroupId, 1274, "NV001", "BS A",
            ActorKind.Employee, "Bearer t", "trace"));
        return map;
    }

    private static FormSubmissionProgressDto ProgressDone => new()
    {
        Sections = new FormProgressCount { Completed = 17, Total = 17 },
        SubmissionState = FormSubmissionStates.InProgress,
        CanSignConclusion = true
    };

    private static FormSubmissionProgressDto ProgressPartial => new()
    {
        Sections = new FormProgressCount { Completed = 3, Total = 17 },
        SubmissionState = FormSubmissionStates.InProgress,
        CanSignConclusion = false
    };

    // ───────────────────────────── Điều kiện (B) là truy vấn ──────────────────────────────

    /// <summary>
    /// Gate H3-04: hồ sơ còn 2 dịch vụ chưa KQ → (B) sai và ĐẾM ĐÚNG 2; trả kết quả nốt 2
    /// dòng đó → (B) đúng ngay, không job nào chạy ở giữa.
    /// </summary>
    [Fact]
    public async Task Dieu_kien_B_dem_dung_so_dich_vu_con_thieu_va_doi_ngay()
    {
        var (db, record) = Fixture();
        using var _db = db;

        var order = db.SeedOrder(record, items: new[]
        {
            (100001L, "XN_CTM", ParaclinicalItemState.Done),
            (100002L, "XN_NT", ParaclinicalItemState.Waiting),
            (100003L, "XN_SH", ParaclinicalItemState.InProgress)
        });

        var map = await SeedSatisfiedConditionA(db, record);
        var conclusion = db.Conclusion(map: map);

        var (satisfied, pending, total) = await conclusion.EvaluateConditionBAsync(record.RecordID);
        Assert.False(satisfied);
        Assert.Equal(2, pending);
        Assert.Equal(3, total);

        var before = await conclusion.GetEligibilityAsync(record.RecordID, roleIds: new[] { ConclusionRoleId });
        Assert.False(before.CanSignConclusion);
        Assert.Contains("Còn 2 chỉ định", before.Conditions.Single(x => x.Code == "B").Detail);

        // Trả nốt kết quả 2 dòng còn lại.
        await db.Orders.ChangeStateAsync(order.OrderID, new ParaclinicalStateRequest
        {
            State = (short)ParaclinicalItemState.Done
        });

        var after = await conclusion.GetEligibilityAsync(record.RecordID, roleIds: new[] { ConclusionRoleId });
        Assert.True(after.CanSignConclusion);
        Assert.True(after.Conditions.Single(x => x.Code == "B").Satisfied);
    }

    /// <summary>Huỷ 2 dịch vụ chưa KQ cũng đóng được (B): dòng đã huỷ không còn ai chờ.</summary>
    [Fact]
    public async Task Huy_dich_vu_chua_co_KQ_thi_dieu_kien_B_du()
    {
        var (db, record) = Fixture();
        using var _db = db;

        var order = db.SeedOrder(record, items: new[]
        {
            (100001L, "XN_CTM", ParaclinicalItemState.Done),
            (100002L, "XN_NT", ParaclinicalItemState.Waiting)
        });

        var conclusion = db.Conclusion();
        Assert.False((await conclusion.EvaluateConditionBAsync(record.RecordID)).Satisfied);

        var victim = db.ItemsOf(record.RecordID).Single(x => x.ServiceCode == "XN_NT");
        await db.Orders.CancelAsync(order.OrderID, new ParaclinicalCancelRequest
        {
            OrderItemIDs = new List<Guid> { victim.OrderItemID },
            Reason = "Không cần nữa"
        });

        Assert.True((await conclusion.EvaluateConditionBAsync(record.RecordID)).Satisfied);
    }

    /// <summary>
    /// GATE của PROJ-2278, đúng hình mô tả trong ticket: chỉ định 5 dịch vụ, trả KQ 4 ⇒ (B) sai;
    /// trả nốt cái thứ 5 ⇒ (B) đúng.
    /// </summary>
    [Fact]
    public async Task Nam_dich_vu_tra_KQ_bon_thi_B_sai_tra_not_thi_B_dung()
    {
        var (db, record) = Fixture();
        using var _db = db;

        var order = db.SeedOrder(record, items: new[]
        {
            (100001L, "XN_CTM", ParaclinicalItemState.Waiting),
            (100002L, "XN_NT", ParaclinicalItemState.Waiting),
            (100003L, "XN_SH", ParaclinicalItemState.Waiting),
            (100004L, "CDHA_XQ", ParaclinicalItemState.Waiting),
            (100005L, "TDCN_DTD", ParaclinicalItemState.Waiting)
        });

        var map = await SeedSatisfiedConditionA(db, record);
        var conclusion = db.Conclusion(map: map);

        // Trả KQ 4 dòng đầu, mỗi lần một dòng.
        foreach (var code in new[] { "XN_CTM", "XN_NT", "XN_SH", "CDHA_XQ" })
        {
            var item = db.ItemsOf(record.RecordID).Single(x => x.ServiceCode == code);
            await db.Orders.ChangeStateAsync(order.OrderID, new ParaclinicalStateRequest
            {
                OrderItemIDs = new List<Guid> { item.OrderItemID },
                State = (short)ParaclinicalItemState.Done
            });
        }

        var four = await conclusion.EvaluateConditionBAsync(record.RecordID);
        Assert.False(four.Satisfied);
        Assert.Equal(1, four.Pending);
        Assert.Equal(5, four.Total);
        Assert.False((await conclusion.GetEligibilityAsync(record.RecordID, roleIds: new[] { ConclusionRoleId })).CanSignConclusion);

        // Trả nốt dòng thứ 5.
        var last = db.ItemsOf(record.RecordID).Single(x => x.ServiceCode == "TDCN_DTD");
        await db.Orders.ChangeStateAsync(order.OrderID, new ParaclinicalStateRequest
        {
            OrderItemIDs = new List<Guid> { last.OrderItemID },
            State = (short)ParaclinicalItemState.Done
        });

        var five = await conclusion.EvaluateConditionBAsync(record.RecordID);
        Assert.True(five.Satisfied);
        Assert.Equal(0, five.Pending);

        var after = await conclusion.GetEligibilityAsync(record.RecordID, roleIds: new[] { ConclusionRoleId });
        Assert.True(after.CanSignConclusion);
        Assert.True(after.Conditions.Single(x => x.Code == "B").Satisfied);
    }

    /// <summary>Hồ sơ không có chỉ định CLS nào ⇒ (B) đủ. Không có gì để chờ thì không chờ.</summary>
    [Fact]
    public async Task Khong_co_chi_dinh_nao_thi_dieu_kien_B_du()
    {
        var (db, record) = Fixture();
        using var _db = db;

        var map = await SeedSatisfiedConditionA(db, record);
        var eligibility = await db.Conclusion(map: map)
            .GetEligibilityAsync(record.RecordID, roleIds: new[] { ConclusionRoleId });

        Assert.True(eligibility.CanSignConclusion);
        Assert.Equal("Không có chỉ định cận lâm sàng", eligibility.Conditions.Single(x => x.Code == "B").Detail);
    }

    // ───────────────────────────── AND (A) và (B) ─────────────────────────────────────────

    /// <summary>Gate H3-11 nhìn từ phía server: nút mở/đóng đúng ở CẢ BỐN tổ hợp (A,B).</summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public async Task Bon_to_hop_AB_cho_ra_dung_CanSignConclusion(bool a, bool b, bool expected)
    {
        var (db, record) = Fixture();
        using var _db = db;

        db.SeedOrder(record, items: new[]
        {
            (100001L, "XN_CTM", b ? ParaclinicalItemState.Done : ParaclinicalItemState.Waiting)
        });

        // (A) giờ là "mọi bước lâm sàng trong map đã có snapshot" — dựng map cố định rồi chỉ
        // ký (hoặc không ký) bước lâm sàng duy nhất để lật đúng cờ (A) cần cho tổ hợp này.
        var map = BuildMap(record.DivisionID, record.VariantCode);
        if (a)
        {
            var cert = new FakeCertificateGateway();
            cert.WithCertificate.Add("NV001");
            var his = new HealthExam.Tests.Signing.FakeSignRoleEmployeesHisClient().Add(ClinicalRoleId, 1274, "NV001", "BS A");
            await db.SignExamSection(map, cert, his).HandleAsync(new SignExamSectionCommand(
                record.DivisionID, record.RecordID, ClinicalItemGroupId, 1274, "NV001", "BS A",
                ActorKind.Employee, "Bearer t", "trace"));
        }

        var eligibility = await db.Conclusion(map: map)
            .GetEligibilityAsync(record.RecordID, roleIds: new[] { ConclusionRoleId });

        Assert.Equal(expected, eligibility.CanSignConclusion);
        Assert.Equal(a, eligibility.Conditions.Single(x => x.Code == "A").Satisfied);
        Assert.Equal(b, eligibility.Conditions.Single(x => x.Code == "B").Satisfied);
    }

    // ───────────────────────────── Lệnh ký kết luận ───────────────────────────────────────

    /// <summary>
    /// ★ Gate H3-05: thiếu (B) ⇒ 4221 kèm Data.Conditions đúng shape §3.7, và KHÔNG render PDF
    /// hay gọi sign-server — (B) được tính lại ở server trước khi chạm tới bất kỳ cổng ngoài nào.
    /// </summary>
    [Fact]
    public async Task Ky_ket_luan_khi_thieu_dieu_kien_B_tra_4221_va_khong_goi_sign_server()
    {
        var (db, record) = Fixture();
        using var _db = db;

        db.SeedOrder(record, items: new[]
        {
            (100001L, "XN_CTM", ParaclinicalItemState.Waiting)
        });

        var map = await SeedSatisfiedConditionA(db, record);
        var signer = new FakePdfSigner();
        var his = new FakeRenderingHisClient();

        var ex = await Assert.ThrowsAsync<HealthExamException>(() =>
            db.Conclusion(map: map, signer: signer, hisEmrClient: his)
              .SignAsync(record.RecordID, roleIds: new[] { ConclusionRoleId }));

        Assert.Equal(ErrorCodes.SignPrecondition, ex.ErrorCode);
        Assert.Equal(422, ErrorCodes.ToHttpStatus(ex.ErrorCode));

        var payload = Assert.IsType<ConclusionSignResult>(ex.Payload);
        var conditionB = Assert.Single(payload.Conditions);
        Assert.Equal("B", conditionB.Code);
        Assert.False(conditionB.Satisfied);
        Assert.Equal("health-exam-server", conditionB.Source);

        Assert.Empty(signer.Requests);
        Assert.Equal(0, his.RenderCount);
    }
}

/// <summary>
/// form-server giả phân nhánh theo ĐƯỜNG DẪN: lệnh ký và lời gọi tiến độ đi hai đường khác
/// nhau, mà bài toán ở đây chính là "có gọi lệnh ký hay không" — một handler trả cùng một
/// thứ cho mọi request thì không phân biệt được.
/// </summary>
public sealed class RoutedFormServer : HttpMessageHandler
{
    private readonly FormSubmissionProgressDto _progress;

    public int ProgressCalls { get; private set; }
    public int SignCalls { get; private set; }
    public string LastSignUrl { get; private set; } = "";
    public string LastSignBody { get; private set; } = "";

    public RoutedFormServer(FormSubmissionProgressDto progress) => _progress = progress;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri?.ToString() ?? "";

        if (url.Contains("/sign"))
        {
            SignCalls++;
            LastSignUrl = url;
            LastSignBody = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            return Envelope(new SectionSignDto { SectionID = 777, State = 3, SignStatus = 1 });
        }

        ProgressCalls++;
        return Envelope(_progress);
    }

    private static HttpResponseMessage Envelope(object data)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(new { ErrorCode = 0, Message = "", Data = data, TraceID = "FORM-TRACE" }),
                Encoding.UTF8, "application/json")
        };
}
