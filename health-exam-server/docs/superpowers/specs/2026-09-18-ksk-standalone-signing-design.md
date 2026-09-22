# Ký số KSK độc lập — snapshot theo mục khám, ký dồn qua sign-server

**Ngày:** 2026-09-18
**Phạm vi:** `health-exam-server` + `turbo-web`
**Trạng thái:** Đã duyệt trong brainstorming

## 1. Mục tiêu

Tách luồng ký số KSK khỏi máy quy trình ký (`SWT`) của his-server. Bác sĩ bấm "Ký số"
ở từng mục khám lâm sàng theo thứ tự bất kỳ; mỗi lần bấm chỉ ghi **snapshot** cục bộ.
Tới bước kết luận, health-exam render PDF một lần rồi gọi thẳng sign-server ký lần lượt
tất cả marker `##{Sn}##` theo snapshot đã có.

Hồ sơ KSK **không cần hiện trên EMR** của his-server. his-server chỉ còn là nguồn dữ liệu
biểu mẫu/EMR và danh tính nhân viên.

## 2. Quyết định đã chốt

| Vấn đề | Quyết định |
|---|---|
| Map mục khám → bước ký | Bảng cấu hình trong DB KSK |
| Bước không có snapshot | Chặn — không cho ký kết luận |
| Sửa mục đã ký | Khóa read-only, muốn sửa phải Hủy ký |
| Kiểm chứng thư số | Tại thời điểm bấm Ký số |
| Thời điểm ký thật | Snapshot khi bấm, ký dồn lúc kết luận |
| Vai trò his-server | Bỏ khỏi đường ký; giữ cho biểu mẫu/EMR/danh tính |

## 3. Phạm vi

Trong phạm vi (nhóm 1 + 2):

- Bỏ `M02F30000/SubmitFile`, `GetFileSign`, `SignFiles`, `Sign/ViewFile`
- Bỏ `M02F01500/SubmitEMR`, `SignEMR`, `CancleEMR`
- Thay `Util/CertInfo` bằng gọi thẳng ssm-server
- Chuyển cấu hình quy trình ký (`M02F01500/RSWByDocTypeID`) vào DB KSK
- Ký qua `sign-server` `POST api/SIGN/Sign`
- health-exam tự lưu PDF đã ký (MinIO)
- FE: khóa mục khám sau khi ký, thêm nút Hủy ký

Ngoài phạm vi:

- Nội dung khám vẫn ở his-server (`M03F10010/REMR|CUEMR|VEMR`), định nghĩa biểu mẫu vẫn ở
  `M03F00030`, render PDF vẫn qua `M02F01500/VEMRs`. Đây là phần form-server đang làm, tách phase sau.
- Đăng nhập, `GetAdmissionInfo`, `CreatePatient`, `CreateAdmission` giữ nguyên qua his-server.
- Hủy ký **kết luận** sau khi PDF đã ký xong.
- Sửa cấu hình quy trình ký trên WFM của HIS.

## 4. Kiến trúc

```
health-exam-server
  ├─ his-server      : biểu mẫu (M03F00030), dữ liệu EMR (M03F10010), render PDF (VEMRs),
  │                    đăng nhập (Auth/*), lượt khám (M02F00000),
  │                    vai trò ký của nhân viên (M02F30000/GetPermissionGroup?empID=)
  ├─ ssm-server      : chứng thư số — GET /api/SSM/SignInfo?userCode={EmpCode}
  ├─ sign-server     : ký PDF        — POST api/SIGN/Sign
  └─ MinIO           : lưu PDF đã ký
```

his-server không còn nằm trên đường ký. Nó vẫn là **nguồn vai trò ký**: `SWRoleID` của nhân viên
đọc từ `M02F30000/GetPermissionGroup` (token IAM không mang claim vai trò). Hai lệnh ký fail-closed
(502) khi HIS không trả được vai trò; eligibility (§6.3) xuống cấp thành `CanSignConclusion=false`.

### Ghi chú tích hợp

- **ssm-server**: header `Authorization: Bearer {SECRET_INTER}`. `SECRET_INTER` đã có sẵn
  trong cấu hình health-exam. Response là `CertInfo`
  (`Name`, `CertName`, `Pin`, `UserID`, `CertID`, `Company`, `Serial`, `From`, `Valid`,
  `PfxID`, `IDCard`, `CoPCode`). **Không bao giờ log response này** — nó chứa PIN.
- **sign-server**: `SIGN.Core/Helpers/AuthorizeAttribute.OnAuthorization` đang bị comment
  toàn bộ, nên API không xác thực, chỉ dựa vào mạng nội bộ. Ghi nhận như rủi ro hạ tầng;
  không xử lý trong đợt này.
- `api/SIGN/Sign` nhận `multipart/form-data`. Gửi `file` mà **không** gửi `filePath` thì
  trả về bytes PDF đã ký. `SIGNService` dùng `UseAppendMode()` và đặt tên chữ ký
  `sig{n+1}`, nên ký chồng nhiều lần giữ nguyên chữ ký trước và không phụ thuộc thứ tự.

## 5. Mô hình dữ liệu

### 5.1. `HEX_SignStepMap` — bảng mới

Không dùng `HEX_ExamGroupFormSectionMapping`: bảng đó map các phần của **phiếu đăng ký**
(`HISTORY`, `EXTRA_INFO`), còn mục khám lâm sàng được địa chỉ hóa bằng `ItemGroupID` của cây
biểu mẫu HIS (`ExamRecordHisFormController.cs:86`). Hai khái niệm khác nhau, không gộp.

| Cột | Kiểu | Ý nghĩa |
| --- | --- | --- |
| `ID` | `uuid` | Khóa |
| `DivisionID` | `varchar(20)` | Tenant |
| `VariantCode` | `varchar(50)` | Bộ biểu mẫu KSK, khớp `HEX_ExamRecord.VariantCode` |
| `SWStep` | `int` | Số bước ký |
| `ItemGroupID` | `int null` | Mục khám lâm sàng; `null` cho bước kết luận |
| `StepName` | `varchar(255)` | Tên bước, hiển thị |
| `SignTitle` | `varchar(255)` | Tiêu đề in cạnh chữ ký |
| `SWRoleID` | `bigint` | Vai trò được ký bước này |
| `SignType` | `int` | 1 ký số, 2 ký điện tử, 3 đóng dấu |
| `SLType` | `int` | 1 vị trí chính xác, 2 theo chuỗi tìm kiếm, 3 người dùng chọn |
| `SearchPattern` | `varchar(50)` | `##{S1}##` … |
| `SLPage` | `int` | Trang ký, dùng khi `SLType = 1` |
| `SLX`, `SLY` | `real` | Toạ độ, dùng khi `SLType = 1` |
| `IsConclusionStep` | `bool` | Đánh dấu bước kết luận |
| `IsActive` | `bool` | Tắt bước không dùng nữa mà không xóa |

Ràng buộc:

- unique `(DivisionID, VariantCode, SWStep)`
- unique `(DivisionID, VariantCode, ItemGroupID)` với `ItemGroupID IS NOT NULL`
- mỗi `(DivisionID, VariantCode)` có đúng một dòng `IsConclusionStep = true`, và dòng đó là
  dòng duy nhất được phép `ItemGroupID IS NULL`

Quan hệ mục khám ↔ bước ký là **1:1**. Một mục khám không sinh hai chữ ký, hai mục khám không
dùng chung một marker.

Seed bằng migration, sao chép từ cấu hình WFM hiện có của từng `FileDocTypeID`.

**Bảng này giờ chính là quy trình ký.** Không còn HIS ép phải đủ số bước, nên bước nào của
workflow cũ không có mục khám tương ứng thì đơn giản là không khai — ví dụ `##{S1}##`
"Bác sĩ đánh giá tâm thần" và `##{S7}##` "Bác sĩ trả kết quả CLS" của KSK02. Quyết định
"chặn khi thiếu snapshot" vẫn giữ nguyên nhưng chỉ áp cho các bước **đã khai**.

Hệ quả: marker không khai vẫn nằm trong `.repx`, nên PDF sẽ có ô chữ ký trống ở đó. Nếu
không chấp nhận được thì phải sửa biểu mẫu, không phải sửa code.

### 5.2. `HEX_ExamRecordSignStep` — sửa

- `SWTID`: **bỏ cột**. Không còn transaction của HIS.
- `FileDocTypeID` → thay bằng `VariantCode varchar(50)`.
- Thêm `ItemGroupID int null` — mục khám sinh ra snapshot này; `null` ở bước kết luận.
- Thêm `SignedByEmployeeCode varchar(50)` — sign-server nhận `empCode`, và cert tra theo mã
  nhân viên chứ không theo ID.
- `Status`: `Snapshot` | `Signed` | `Failed`. Bỏ `Pending`/`InProcessing`/`Cancelled`.

Unique mới: `(DivisionID, RecordID, VariantCode, SWStep)`.

`VariantCode` thay `FileDocTypeID` vì sau khi bỏ HIS khỏi đường ký thì `FileDocTypeID` không
còn nghĩa gì trong KSK, trong khi `VariantCode` mới là định danh bộ biểu mẫu của KSK. Vai trò
trong unique key giữ nguyên: phân biệt hai loại tài liệu cùng ký trên một hồ sơ.

Ý nghĩa mới của một dòng: "bác sĩ X đã bấm ký mục Y lúc Z, sẽ ký vào marker `##{Sn}##`".
Dòng chỉ sinh ra khi có người bấm ký, không đổ sẵn theo workflow.

**Khóa mục khám là trạng thái suy ra, không phải cột mới**: một mục bị khóa khi và chỉ khi
tồn tại dòng snapshot cho `(RecordID, ItemGroupID)`. Hủy ký xóa dòng đó là mở khóa.

### 5.3. `HEX_ExamRecord` — sửa

- Bỏ `HisSignTransactionID`, `HisSignKeyID`, `HisSignedFileDocID`.
- `HisSignedFilePath` → đổi tên `SignedFilePath`, trỏ vào MinIO của KSK.
- `HisSignStatus` → đổi tên `SignStatus`: `New` | `Signed` | `Failed`.
- Giữ `HisSignedByEmployeeID`, `HisSignedAt` (người kết luận và thời điểm hoàn tất).

## 6. API

### 6.1. Ký một mục khám

```http
POST /v1/exam-records/{recordId}/sections/{itemGroupId}/sign
```

`itemGroupId` là `int`, cùng định danh mà FE đã dùng ở `GET .../his-form/sections/{itemGroupId}`.
Body rỗng. Nhân viên, khoa, phân hệ lấy từ JWT.

1. Tra `HEX_SignStepMap` theo `(DivisionID, record.VariantCode, itemGroupId)` ra `SWStep`,
   `SWRoleID`, `SearchPattern`. Không có dòng map → `422`, lỗi cấu hình.
2. Kiểm nhân viên có `SWRoleID` của bước đó → không có thì `403`.
3. Gọi ssm-server lấy cert. Không có cert hoặc hết hạn (`Valid` đã qua) → `422`, báo rõ
   "chưa có chứng thư số" để bác sĩ đi cấp.
4. Ghi/ghi đè snapshot `{VariantCode, ItemGroupID, SWStep, SWRoleID, EmployeeID,
   EmployeeCode, SignedAt}`, `Status = Snapshot`.
5. Khóa mục khám read-only.

### 6.2. Hủy ký một mục khám

```http
POST /v1/exam-records/{recordId}/sections/{itemGroupId}/sign/cancel
```

Xóa snapshot, mở khóa mục. Từ chối nếu hồ sơ đã `SignStatus = Signed`.

### 6.3. Điều kiện ký kết luận

```http
GET /v1/exam-records/{recordId}/conclusion-eligibility
```

Trả điều kiện y khoa như hiện tại, cộng thêm:

- `Steps`: mọi bước trong bảng map, kèm trạng thái snapshot, người ký, thời điểm
- `MissingSteps`: các bước chưa có snapshot
- `CanSignConclusion`: đủ điều kiện y khoa **và** `MissingSteps` rỗng **và** nhân viên hiện tại
  giữ `SWRoleID` của bước kết luận

Toàn bộ đọc từ DB KSK, không gọi service ngoài.

### 6.4. Ký kết luận

```http
POST /v1/exam-records/{recordId}/conclusion/sign
```

Body rỗng.

### 6.5. Xem PDF

```http
GET /v1/exam-records/{recordId}/registration-form/preview
```

Giữ route preview có sẵn thay vì mở `conclusion/pdf` mới — FE đã gọi nó. Đã ký thì trả file
từ MinIO. Chưa ký thì trả bản nháp render từ `VEMRs`. Đã `Signed` mà không lấy được file → lỗi,
không bao giờ trả bản nháp thay thế.

## 7. Luồng ký kết luận

1. Khóa hồ sơ bằng row lock.
2. `SignStatus = Signed` → trả trạng thái đã lưu, không làm gì thêm.
3. Tính lại điều kiện y khoa.
4. Đối chiếu `HEX_SignStepMap` theo `VariantCode`: mọi bước `IsActive` phải có snapshot.
   Thiếu → `422` kèm danh sách bước thiếu.
5. Ghi snapshot cho bước kết luận (nhân viên gọi API), kiểm role và cert như mục 6.1.
6. Render PDF: `GET M02F01500/VEMRs` → bytes.
7. Với mỗi snapshot, sắp theo `SWStep` tăng dần:
   - lấy cert theo `EmployeeCode` của snapshot
   - `POST api/SIGN/Sign` với `file` = bytes hiện tại, `searchPattern`, `signLocationType`,
     `signType`, `title` = `SignTitle`, `name` = tên nhân viên, `date` = `SignedAt` của
     snapshot định dạng `yyyy-MM-dd HH:mm`, `empCode`, `company`, `certName`, `copCode`, `pin`
   - nhận bytes mới, dùng cho lượt kế tiếp
8. Upload bytes cuối lên MinIO, ghi `SignedFilePath`.
9. Đặt `SignStatus = Signed`, `HisSignedByEmployeeID`, `HisSignedAt`; mọi snapshot chuyển
   `Status = Signed`. Ghi audit.

Thứ tự lặp theo `SWStep` là để chữ ký xuất hiện theo trật tự dễ đọc, không phải ràng buộc
kỹ thuật — sign-server không quan tâm thứ tự.

**PDF chỉ nằm trong bộ nhớ suốt vòng lặp.** Chỉ một lượt ghi MinIO ở cuối.

## 8. Lưu trữ PDF

health-exam hiện không có client MinIO nào. Thêm package `Minio` và một
`IExamFileStore` với `UploadAsync` / `DownloadAsync`.

Bucket riêng cho KSK, đường dẫn `{DivisionID}/{yyyy}/{MM}/{RecordID}.pdf`.

Không lưu PDF trong Postgres: mỗi hồ sơ vài MB, khối lượng theo ngày sẽ làm phình DB và
backup.

## 9. Lỗi và idempotency

- **Ký giữa chừng thất bại** (cert hết hạn, sign-server lỗi): không upload gì, `SignStatus`
  giữ `New`, snapshot giữ nguyên. Gọi lại thì chạy lại từ đầu với PDF mới render. An toàn
  vì chưa có gì được ghi ra ngoài.
- **Upload MinIO thất bại sau khi ký xong**: `SignStatus = Failed`, lưu lỗi. Gọi lại ký lại
  toàn bộ. Không có trạng thái nửa vời.
- **Gọi lại khi đã `Signed`**: trả trạng thái đã lưu, không ký lại.
- **Không tìm thấy marker trong PDF**: sign-server trả lỗi; dừng cả lượt, báo rõ bước nào.
  Đây là lệch giữa bảng map và biểu mẫu `.repx`, phải sửa cấu hình.
- **Gọi đồng thời**: row lock trên `HEX_ExamRecord`. EF change tracking không đủ.

## 10. Xóa bỏ

- `HealthExam.Application/His/SubmitHisSection.cs`, `SignHisSection.cs`,
  `CancelHisSectionSignature.cs`, `GetHisSectionSigningContexts.cs`,
  `HisSectionSigningContextResolver.cs`, `GetSignWorkflow.cs`
- 3 endpoint trong `ExamRecordHisFormController`: `sections/{sectionKey}/submit`,
  `/sign`, `/sign/cancel`
- Trong `HisEmrClient`: `SubmitPdfForSigningAsync`, `SubmitAndSignPdfAsync`,
  `SignPdfStepAsync`, `GetPendingPdfSignStepsAsync`, `GetPdfSignWorkflowAsync`,
  `ViewSignedFileAsync` và các route hằng tương ứng
- Env `HIS_EMR_SECTION_SIGNING_ENABLED`

FE chưa từng gọi các endpoint này (`packages/api/src/health-exam/` không có hàm ký nào),
nên xóa thẳng, không cần vòng deprecate.

**Bán kính ảnh hưởng của việc đổi tên cột ở §5.3** rộng hơn danh sách xóa trên. Các chỗ đọc
`HisSignStatus` / `HisSignedFilePath` / `HisSignTransactionID` phải sửa cùng lượt, nếu không
sẽ vỡ build:

- `HealthExam.Application/Paraclinical/GetConclusionEligibility.cs`
- `HealthExam.Application/Paraclinical/SignConclusion.cs`
- `HealthExam.Application/RegistrationForms/PreviewRegistrationFormPdf.cs`
- `HealthExam.Infrastructure/Persistence/Repositories/ExamRecordRepository.cs`
  (ba nhánh truy vấn có `.Include(x => x.SignSteps)`)
- `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs`

## 11. Cấu hình mới

```
SSM_BASE_URL=
MINIO_ENDPOINT=
MINIO_ACCESS_KEY=
MINIO_SECRET_KEY=
MINIO_BUCKET_HEALTH_EXAM=
SIGN_SERVER_BASE_URL=
SIGN_SERVER_TIMEOUT_SECONDS=60
```

`SECRET_INTER` đã có, dùng lại để gọi ssm-server.

## 12. Frontend

- Mục khám đã có snapshot: tắt nút Chỉnh sửa và Xóa, hiện tên người ký và thời điểm
- Thêm nút Hủy ký trên mục đã ký
- Tab Kết luận: hiện danh sách bước kèm trạng thái; bước thiếu snapshot nêu rõ tên
- Nút Ký kết luận chỉ bật khi `CanSignConclusion = true`

## 13. Acceptance criteria

- Bác sĩ ký các mục theo thứ tự bất kỳ, PDF cuối có đủ chữ ký đúng vị trí marker
- Mục đã ký không sửa được cho tới khi hủy ký
- Bác sĩ không giữ `SWRoleID` của bước không ký được mục đó
- Bác sĩ chưa có chứng thư số bị chặn ngay lúc bấm Ký số, kèm thông báo rõ
- Thiếu bất kỳ bước nào thì không ký kết luận được, API nêu đúng bước thiếu
- Gọi ký kết luận hai lần không tạo file thứ hai, không ký chồng lần hai
- Ký hỏng giữa chừng không để lại file hay trạng thái nửa vời
- Luồng ký không gọi route nào của `M02F30000` hay `M02F01500` ngoài `VEMRs` và
  `M02F30000/GetPermissionGroup` (tra vai trò ký — đọc, không ghi)
- Mục đã ký / hồ sơ đã `Signed` bị chặn sửa ở **server** (409), không chỉ ở FE
- Migration chạy được trên DB hiện tại

## 14. Rủi ro

- **Chữ ký mang dấu thời gian lúc kết luận.** Ô hiển thị in `SignedAt` của snapshot, nhưng
  timestamp mật mã bên trong chữ ký là thời điểm ký thật. Hai giá trị lệch nhau. Cần xác nhận
  với bên pháp chế rằng chấp nhận được.
- **sign-server không xác thực.** Chỉ được bảo vệ bằng mạng nội bộ.
- **Bảng map phải khớp `.repx`.** Marker trong bảng map mà biểu mẫu không có thì lỗi chỉ lộ
  ra lúc ký kết luận. Nên viết một lệnh kiểm tra đối chiếu map với PDF render thử
  (`api/File/DetectPDF` của sign-server nhận PDF và chuỗi cần tìm).
- **Nội dung khám vẫn ở his-server.** KSK chưa thật sự độc lập cho tới khi form-server thay
  xong `M03F10010`.
