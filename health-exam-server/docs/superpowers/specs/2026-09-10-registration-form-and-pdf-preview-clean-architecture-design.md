# Registration Form Step 2 & PDF Preview in Clean Architecture — Design Specification

## 1. Mục tiêu và Tổng quan

Tài liệu này đặc tả thiết kế và kiến trúc cho việc đưa toàn bộ cụm tính năng **Biểu mẫu đăng ký KSK Bước 2 (Step 2 Registration Form)** và **Xem trước PDF (PDF Preview)** từ nhánh cũ `feat/api-render-pdf` sang hệ thống mới theo chuẩn **Clean Architecture** (Onion Architecture).

### Mục tiêu chính:
1. **Quản lý biểu mẫu theo nhóm khám**: Cho phép tra cứu cấu hình biểu mẫu chuẩn hoá (gồm các phân đoạn `HISTORY` và `EXTRA_INFO`) dựa theo nhóm khám của người khám (`VariantCode`, ví dụ `DTK_03` -> mẫu `KSK-TREN18TUOI`).
2. **Quản lý và lưu giá trị biểu mẫu**: Cung cấp API đọc giá trị đã lưu (`GET`) và lưu từng phân đoạn (`PUT`) của hồ sơ KSK xuống hệ thống HIS EMR theo cơ chế **Read-Merge-Write** an toàn, tự động tạo lượt tiếp nhận HIS (`EnsureAdmission`) khi cần.
3. **Xem trước file PDF**: Cung cấp API render và trả về trực tiếp file PDF (`GET .../preview`) phản ánh snapshot mới nhất đã lưu trên HIS EMR, phục vụ hiển thị inline trên trình duyệt/FE.
4. **Tuân thủ chuẩn Clean Architecture**:
   - `HealthExam.Domain` và `HealthExam.Application` chỉ dùng .NET BCL (0 external NuGet packages).
   - Dedicated handler per use case (không dùng mediator hay event bus).
   - Tương thích 100% với hợp đồng API và migration DB hiện tại.

---

## 2. Phạm vi (Scope)

### Trong phạm vi (In-Scope)
- **Domain Layer**:
  - Bổ sung 4 trường trạng thái biểu mẫu HIS trên thực thể `ExamRecord`: `HisEmrDataID`, `HisFormTemplateID`, `HisFormSyncStatus`, `HisFormSyncError`.
  - Thực thể mới `ExamGroupFormMapping` và `ExamGroupFormSectionMapping` trong `HealthExam.Domain/ExamForms/`.
- **Infrastructure Layer**:
  - EF Core configurations cho 2 thực thể mới và mapping cho 4 trường mở rộng của `ExamRecord`.
  - Giữ nguyên 2 file migration DB đã có:
    - `20260909081824_AddExamGroupFormMappings`
    - `20260909102548_AddHisRegistrationFormState`
  - Hiện thực repository `RegistrationFormRepository`.
  - Mở rộng `IHisEmrClient` / `HisEmrClient` hỗ trợ: `RenderFormPdfAsync` (binary PDF), `ReadFormDataAsync` (`REMR`), `SaveFormDataAsync` (`CUEMR`), `CreateAdmissionAsync`.
- **Application Layer**:
  - Định nghĩa 4 use case handler độc lập:
    1. `IGetExamGroupRegistrationFormsHandler`
    2. `IGetRegistrationFormHandler`
    3. `ISaveRegistrationFormSectionHandler`
    4. `IPreviewRegistrationFormPdfHandler`
  - Các DTOs và Models nghiệp vụ thuần BCL.
- **API Layer**:
  - `ExamGroupController`: Endpoint `GET /v1/exam-groups/{groupCode}/registration-forms`.
  - `ExamRecordRegistrationFormController`:
    - `GET /v1/exam-records/{recordId}/registration-form`
    - `PUT /v1/exam-records/{recordId}/registration-form/sections/{sectionKind}`
    - `GET /v1/exam-records/{recordId}/registration-form/preview` (raw `application/pdf`)
- **Tests**:
  - Bộ kiểm thử tự động toàn diện: Unit tests, Handler tests, Controller endpoint tests, PostgreSQL integration tests.

### Ngoài phạm vi (Out-of-Scope)
- Không chỉnh sửa HIS EMR backend hoặc Report Server.
- Không triển khai giao diện Frontend (FE đã có client gọi API).
- Không tự động submit hoặc ký số biểu mẫu trong luồng preview PDF.
- Không đưa module `AuthController` (HIS Auth) vào phạm vi này vì IAM server quản lý phiên đăng nhập độc lập.

---

## 3. Kiến trúc các tầng (Clean Architecture Structure)

```text
┌─────────────────────────────────────────────────────────────┐
│                       HealthExam.API                        │
│  ExamGroupController, ExamRecordRegistrationFormController  │
│  Middleware: Division (X-Division-Id), AuthGuard, TraceId   │
└──────────────┬───────────────────────────────▲──────────────┘
               │ Calls Handlers                │ Returns Results / Files
┌──────────────▼───────────────────────────────┴──────────────┐
│                   HealthExam.Application                    │
│  Handlers: GetGroupForms, GetForm, SaveSection, PreviewPdf   │
│  Ports: IRegistrationFormRepository, IHisEmrClient          │
│  (0 External NuGet Packages, Pure BCL)                      │
└──────────────┬───────────────────────────────▲──────────────┘
               │ References Domain             │ Domain Model
┌──────────────▼───────────────────────────────┴──────────────┐
│                    HealthExam.Domain                        │
│  ExamRecord (HisEmrDataID, HisFormSyncStatus...),           │
│  ExamGroupFormMapping, ExamGroupFormSectionMapping          │
│  (0 External NuGet Packages, Pure BCL)                      │
└─────────────────────────────────────────────────────────────┘
                               ▲
                               │ Implements Ports & Persistence
┌──────────────────────────────┴──────────────────────────────┐
│                  HealthExam.Infrastructure                  │
│  RegistrationFormRepository (EF Core), Migrations           │
│  HisEmrClient (HTTP calls: VEMR binary PDF, REMR, CUEMR)     │
└─────────────────────────────────────────────────────────────┘
```

---

## 4. Chi tiết Thiết kế từng tầng

### 4.1. Tầng Domain (`HealthExam.Domain`)

#### A. Cập nhật `ExamRecord` (`HealthExam.Domain/ExamRecords/ExamRecord.cs`)
Bổ sung các thuộc tính theo dõi liên kết HIS EMR:
```csharp
// --- Biểu mẫu đăng ký KSK trên HIS EMR
public Guid? HisEmrDataID { get; set; }
public Guid? HisFormTemplateID { get; set; }
public string HisFormSyncStatus { get; set; } = "Pending";
public string HisFormSyncError { get; set; } = "";
```

#### B. Thực thể mới trong `HealthExam.Domain/ExamForms/`
1. `ExamGroupFormMapping`:
```csharp
public class ExamGroupFormMapping
{
    public Guid MappingID { get; set; }
    public string DivisionID { get; set; } = "";
    public string VariantCode { get; set; } = "";
    public string TemplateCode { get; set; } = "";
    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public long CreatedBy { get; set; }
    public short CreatedActorKind { get; set; }
    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
    public long ModifiedBy { get; set; }
    public short ModifiedActorKind { get; set; }

    public ICollection<ExamGroupFormSectionMapping> Sections { get; set; } = new List<ExamGroupFormSectionMapping>();
}
```

2. `ExamGroupFormSectionMapping`:
```csharp
public class ExamGroupFormSectionMapping
{
    public Guid SectionMappingID { get; set; }
    public Guid MappingID { get; set; }
    public string SectionKind { get; set; } = ""; // "HISTORY" hoặc "EXTRA_INFO"
    public int ItemGroupID { get; set; }

    public ExamGroupFormMapping Mapping { get; set; }
}
```

### 4.2. Tầng Infrastructure (`HealthExam.Infrastructure`)

#### A. EF Core Configurations
- `ExamGroupFormMappingConfiguration`:
  - Khóa chính `MappingID`.
  - Index Unique trên `(DivisionID, VariantCode)`.
- `ExamGroupFormSectionMappingConfiguration`:
  - Khóa chính `SectionMappingID`.
  - Khóa ngoại `MappingID` tới `HEX_ExamGroupFormMapping` với `DeleteBehavior.Cascade`.
  - Index Unique trên `(MappingID, SectionKind)`.
- `ExamRecordConfiguration`:
  - Map `HisEmrDataID` (uuid nullable).
  - Map `HisFormTemplateID` (uuid nullable).
  - Map `HisFormSyncStatus` (varchar 50, default 'Pending').
  - Map `HisFormSyncError` (varchar 1000, default '').

#### B. Migrations
Sao chép 2 migration chuẩn vào `HealthExam.Infrastructure/Persistence/Migrations/`:
1. `20260909081824_AddExamGroupFormMappings.cs`
2. `20260909102548_AddHisRegistrationFormState.cs`

#### C. `RegistrationFormRepository`
Hiện thực interface `IRegistrationFormRepository`:
```csharp
public class RegistrationFormRepository : IRegistrationFormRepository
{
    private readonly HealthExamDbContext _db;

    public RegistrationFormRepository(HealthExamDbContext db) => _db = db;

    public Task<ExamGroupFormMapping> GetActiveMappingAsync(string divisionId, string variantCode, CancellationToken ct = default)
        => _db.ExamGroupFormMappings
            .Include(m => m.Sections)
            .FirstOrDefaultAsync(m => m.DivisionID == divisionId && m.VariantCode == variantCode && m.IsActive, ct);

    public Task<ExamRecord> GetRecordAsync(string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default)
    {
        var query = forUpdate ? _db.ExamRecords.AsTracking() : _db.ExamRecords.AsNoTracking();
        return query.FirstOrDefaultAsync(r => r.RecordID == recordId && r.DivisionID == divisionId, ct);
    }
}
```

#### D. Mở rộng `HisEmrClient`
Thêm phương thức render PDF binary và trao đổi dữ liệu form:
- `RenderFormPdfAsync(Guid emrDataId, string credential, string traceId, string divisionId, CancellationToken ct)`:
  - Gọi HTTP `GET api/M03F10010/VEMR?EMRDataID={emrDataId}&IsJson=false`.
  - Forward header `Authorization`, `X-Trace-Id`, `X-Division-Id`.
  - Nhận HTTP 200 -> đọc `ReadAsByteArrayAsync()`.
  - Nếu body trống hoặc lỗi -> ánh xạ sang `HealthExamException` (`HisBadGateway` / `HisTimeout`).

### 4.3. Tầng Application (`HealthExam.Application/RegistrationForms/`)

#### A. Data Transfer Objects (`RegistrationFormModels.cs`)
```csharp
public sealed record RegistrationRecordForm(
    Guid RecordId,
    string PatientCode,
    string PatientName,
    string VariantCode,
    string TemplateCode,
    Guid? HisEmrDataId,
    string SyncStatus,
    string SyncError,
    IReadOnlyList<RegistrationFormSection> Sections);

public sealed record RegistrationFormSection(
    string SectionKind,
    int ItemGroupId,
    string Title,
    IReadOnlyList<RegistrationFormField> Fields);

public sealed record RegistrationFormField(
    Guid ItemId,
    string ItemCode,
    string ItemName,
    string DataType,
    string ControlStyle,
    bool ReadOnly,
    string Value,
    string Text,
    IReadOnlyList<RegistrationFormFieldOption> Options = null);

public sealed record RegistrationSectionSaveRequest(
    bool IsDraft,
    IReadOnlyList<RegistrationFormFieldValue> Fields);

public sealed record RegistrationFormFieldValue(
    Guid ItemId,
    string Value,
    string Text = null);

public sealed record ExamGroupRegistrationFormsResult(
    string VariantCode,
    string TemplateCode,
    IReadOnlyList<RegistrationFormSection> Sections);
```

#### B. Handlers

1. **`GetExamGroupRegistrationFormsHandler`**:
   - `List<RegistrationFormSection>` được trích xuất từ layout của `TemplateCode` (thông qua `IHisFormDefinitionCache`).
   - Lọc các node lá có `ItemGroupID` thuộc `HISTORY` (64) hoặc `EXTRA_INFO` (63).

2. **`GetRegistrationFormHandler`**:
   - Tải `ExamRecord` theo `(DivisionId, RecordId)`.
   - Tìm mapping `ExamGroupFormMapping`.
   - Nếu `record.HisEmrDataID` có giá trị: gọi HIS `REMR` để đọc các giá trị đã lưu (`Value`, `Text`) gắn vào các trường tương ứng.
   - Trả về `RegistrationRecordForm`.

3. **`SaveRegistrationFormSectionHandler`**:
   - Kiểm tra `ExamRecord` và `ExamGroupFormMapping`.
   - Chuẩn hoá `SectionKind` ("HISTORY" / "EXTRA_INFO").
   - Validate các trường gửi lên phải thuộc phân đoạn đó và không phải chỉ đọc.
   - **Đảm bảo lượt tiếp nhận**: Nếu `record.AdmissionID` chưa có, gọi HIS `CreateAdmission` để tạo lượt tiếp nhận và cập nhật vào `record.AdmissionID`.
   - **Read-Merge-Write**:
     - Gọi HIS `REMR` đọc toàn bộ chi tiết hiện có trên EMR.
     - Giữ nguyên các trường của phân đoạn khác, cập nhật các trường của phân đoạn hiện tại.
     - Gọi HIS `CUEMR` lưu toàn bộ payload đã merge.
     - Cập nhật `record.HisEmrDataID`, `HisFormSyncStatus = "Synced"`, `HisFormSyncError = ""`. Lưu vào DB.
     - Nếu lỗi: cập nhật `HisFormSyncStatus = "Failed"` và ném lỗi phù hợp.
   - Trả về `RegistrationRecordForm` mới nhất.

4. **`PreviewRegistrationFormPdfHandler`**:
   - Tải `ExamRecord`. Nếu không tìm thấy -> `NotFound`.
   - Nếu `!record.HisEmrDataID.HasValue` -> ném `InvalidState` ("Biểu mẫu chưa được lưu lên HIS EMR").
   - Gọi `IHisEmrClient.RenderFormPdfAsync(record.HisEmrDataID.Value, ...)`.
   - Trả về `byte[]` PDF thô.

### 4.4. Tầng API (`HealthExam.API`)

#### Endpoints

| Method | Route | Mô tả | Phản hồi thành công | Lỗi |
|---|---|---|---|---|
| `GET` | `/v1/exam-groups/{groupCode}/registration-forms` | Lấy biểu mẫu chuẩn theo nhóm khám | `200` `{ ErrorCode: 0, Data: ExamGroupRegistrationFormsResult }` | `4001`, `4040` |
| `GET` | `/v1/exam-records/{recordId}/registration-form` | Lấy biểu mẫu kèm giá trị đã lưu | `200` `{ ErrorCode: 0, Data: RegistrationRecordForm }` | `4001`, `4040` |
| `PUT` | `/v1/exam-records/{recordId}/registration-form/sections/{sectionKind}` | Lưu giá trị một phân đoạn form | `200` `{ ErrorCode: 0, Data: RegistrationRecordForm }` | `4001`, `4040`, `4090`, `5022` |
| `GET` | `/v1/exam-records/{recordId}/registration-form/preview` | Xem trước file PDF của form | `200` File stream `application/pdf` inline | `4001`, `4040`, `4090`, `5022` |

---

## 5. Chiến lược Kiểm thử (Testing Strategy)

1. **Domain Unit Tests (`HealthExam.Tests/Domain/`)**:
   - Kiểm tra khởi tạo thực thể `ExamGroupFormMapping`, `ExamGroupFormSectionMapping`.
   - Kiểm tra các thuộc tính mở rộng trên `ExamRecord`.
2. **Application Handler Tests (`HealthExam.Tests/Application/`)**:
   - `GetExamGroupRegistrationFormsHandlerTests`: Kiểm tra lấy form theo nhóm khám, lọc phân đoạn, xử lý nhóm khám chưa cấu hình.
   - `GetRegistrationFormHandlerTests`: Kiểm tra lấy form có hoặc không có dữ liệu lưu trước trên HIS.
   - `SaveRegistrationFormSectionHandlerTests`: Kiểm tra validate trường chỉ đọc, tự động liên kết tiếp nhận, merge dữ liệu 2 phân đoạn, cập nhật trạng thái `Synced` / `Failed`.
   - `PreviewRegistrationFormPdfHandlerTests`: Kiểm tra trả về bytes khi đã có `HisEmrDataID`, ném `InvalidState` khi chưa lưu, ném `NotFound` khi sai hồ sơ / tenant.
3. **Infrastructure & Persistence Tests (`HealthExam.Tests/Infrastructure/`)**:
   - `RegistrationFormPersistenceTests`: Kiểm tra truy vấn mapping và lưu trạng thái trên PostgreSQL thật.
   - `HisEmrClientPdfTests`: Kiểm tra gọi HIS endpoint `M03F10010/VEMR?IsJson=false` và xử lý bytes/lỗi.
4. **API Surface & Endpoint Tests (`HealthExam.Tests/API/`)**:
   - `RegistrationFormValueEndpointTests`: Kiểm tra các route API, middleware auth, `X-Division-Id`, content type `application/pdf`.
   - `ApiContractSurfaceTests`: Khẳng định Swagger document có đủ 4 endpoint mới.
5. **Architectural Dependency Rule Tests**:
   - Kiểm tra `HealthExam.Domain` và `HealthExam.Application` không tham chiếu thư viện ngoài và không để lộ `IQueryable`.
