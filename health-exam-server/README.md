# health-exam-server

Service nghiệp vụ **Khám sức khoẻ** (PROJ-2221). Kiến trúc, middleware, envelope và bảng mã
lỗi bắt chước nguyên `form-server` — hai service này FE gọi trong cùng một màn hình, nên
chúng phải cư xử giống nhau.

| Mục | Giá trị |
|---|---|
| Stack | .NET 8, EF Core 8, Npgsql, Serilog |
| Cổng | **9920** (form-server: 9910) |
| Connection string | biến môi trường **`HEALTHEXAM_DB`** — cố ý KHÔNG để trong appsettings |
| DB | mặc định `hos_health_exam`; DEV: `DEV_S2_HEALTHEXAM` |
| Tiền tố bảng | `HEX_` |
| Mã hệ thống | `ModuleCode = HEALTH_EXAM` · `HostRefType = HEALTH_EXAM_SESSION` |

## Cấu trúc dự án (Clean Architecture)

Hệ thống được tổ chức theo mô hình Clean Architecture phân tách rõ ràng các tầng trách nhiệm:

- **`HealthExam.Domain`**: Thực thể nghiệp vụ thuần (`ExamRecord`, `ExamSession`, `ImportBatch`, `ParaclinicalOrder`...), máy trạng thái và quy tắc chuyển tiếp, mã lỗi domain. **0 thư viện ngoài (chỉ dùng .NET BCL)**.
- **`HealthExam.Application`**: Các use case nghiệp vụ (Commands/Queries), interface handler chuyên biệt theo từng use case (`IConfirmExamRecordHandler`, `ICommitImportHandler`...), DTO, trừu tượng Repository và Unit of Work. **0 thư viện ngoài (chỉ dùng .NET BCL)**, không để lộ `IQueryable`.
- **`HealthExam.Infrastructure`**: Hiện thực kỹ thuật: EF Core `HealthExamDbContext`, migration, repository PostgreSQL, bộ cấp số chuỗi nguyên tử, bộ đọc/ghi Excel EPPlus, tích hợp HTTP ngoài (`HisEmrClient`, `FormServerClient`), và worker nền `IntegrationOutboxWorker`.
- **`HealthExam.API`**: Điểm vào HTTP: ASP.NET Core controllers, middleware pipeline (xác thực, phân quyền đơn vị, HMAC webhook, mapper kết quả), model hợp đồng request/response, và composition root `Program.cs`.
- **`HealthExam.Tests`**: Bộ kiểm thử tự động toàn diện (826 tests): Domain unit tests, Application handler tests, Infrastructure integration tests chạy trên PostgreSQL thật, kiểm thử bề mặt hợp đồng API / Swagger, và kiến trúc dependency rule gate.

## Trạng thái: ĐĂNG KÝ + NẠP EXCEL + NỐI FORM-SERVER

Phủ P1 (đợt khám, hồ sơ, nạp Excel), P2 (cầu nối form-server, webhook, đối soát) và endpoint
nội bộ cho `iam-server`. Xem mục "Đã cắt" bên dưới — đừng đọc thiếu rồi tưởng tính năng bị lỗi.

## Chạy

```bash
export HEALTHEXAM_DB='Host=14.225.211.37;Port=13300;Database=DEV_S2_HEALTHEXAM;User ID=emr;Password=***;'

# migrate (startup-project là API, migrations lưu tại Infrastructure)
dotnet ef database update --project HealthExam.Infrastructure --startup-project HealthExam.API

# seed danh mục + đợt/hồ sơ mẫu (idempotent, in ra current_database trước khi ghi)
PGPASSWORD=*** psql -h 14.225.211.37 -p 13300 -U emr -d DEV_S2_HEALTHEXAM \
    -f Deploy/seed/health-exam-seed.sql

ASPNETCORE_URLS=http://127.0.0.1:9920 ENABLE_SWAGGER=true dotnet run --project HealthExam.API

# Chạy toàn bộ test suite (826 test)
export HEALTHEXAM_TEST_DB='Host=127.0.0.1;Port=54329;Database=healthexam_test;User ID=postgres;Password=postgres'
TZ=Asia/Ho_Chi_Minh dotnet test HealthExamServer.sln
```

### Biến môi trường

| Biến | Bắt buộc | Dùng vào |
|---|---|---|
| `HEALTHEXAM_DB` | ✅ | Connection string. **Rỗng thì hai worker nền KHÔNG được đăng ký** (host test không có DB) |
| `SECRET_KEY` · `SECRET_ISSUER` | ✅ | Xác thực JWT nhân viên của IAM — ký `SHA256(SECRET_KEY)`, `aud=DHS`, giống `his-server` |
| `SECRET_INTER` | ✅ | **Một bí mật, ba chỗ dùng**: token `iam-server` gọi vào `/v1/internal`, token service gọi RA `form-server`, và khoá ký HMAC của webhook. Cố ý không đẻ thêm biến thứ hai — thêm một thứ có thể quên xoay |
| `FORM_SERVER_BASE_URL` | ⛔ nếu không dùng biểu mẫu | Cầu nối `form-server`. Thiếu → mọi endpoint đụng biểu mẫu trả `5020` chỉ thẳng tên biến |
| `HEALTH_EXAM_DOCTYPE_ID` | — | Gáy hồ sơ KSK bên `form-server`, mặc định `990001`. Chuỗi rác thì trả `5020`, KHÔNG rơi êm về mặc định |
| `HEALTHEXAM_RECONCILE_INTERVAL_MINUTES` | — | Chu kỳ job đối soát kéo, mặc định `10` |
| `HIS_EMR_ENABLED` | — | Bật/tắt adapter HIS EMR (`true`/`false`), mặc định `false`. Tắt → endpoint HIS trả lỗi 5022 (HisBadGateway) |
| `HIS_EMR_BASE_URL` | ⛔ nếu bật HIS | URL gốc của HIS EMR (ví dụ `https://his.example.internal`). Phải dùng scheme `http` hoặc `https` |
| `HIS_EMR_TIMEOUT_SECONDS` | — | Timeout gọi HIS (giây), mặc định `10` |
| `HIS_EMR_CREDENTIAL_HEADER` | — | Header chuyển tiếp thông tin xác thực sang HIS, mặc định `Authorization` |
| `HIS_EMR_DEFINITION_CACHE_SECONDS` | — | Thời gian cache định nghĩa form HIS (giây), mặc định `300` |
| `SIGN_SERVER_BASE_URL` | ⛔ nếu bật HIS | URL sign-server dùng để ký số PDF. Thiếu lúc `HIS_EMR_ENABLED=true` → ném lỗi ngay lúc khởi động (fail-fast, không đợi tới lượt gọi ký đầu tiên) |
| `SIGN_SERVER_TIMEOUT_SECONDS` | — | Timeout mỗi lượt gọi sign-server (giây), mặc định `60`. Ký kết luận gọi tuần tự một lượt cho mỗi bước ký |
| `SSM_BASE_URL` | ⛔ nếu bật HIS | URL ssm-server dùng để tra chứng thư số của nhân viên ký (Bearer `SECRET_INTER`, timeout cố định 10s). Cùng cơ chế fail-fast như trên |
| `MINIO_PATH` | ⛔ nếu bật HIS | Endpoint MinIO lưu PDF đã ký (`host[:port]`, không scheme). Cùng tên với `minio-cm` dùng chung của cụm — pod chỉ cần `envFrom` `minio-cm` + `minio-secret`. Cùng cơ chế fail-fast như trên |
| `MINIO_USERNAME` · `MINIO_PASS` | ⛔ nếu bật HIS | Cặp khoá MinIO (tên theo `minio-secret` dùng chung). Cùng cơ chế fail-fast như trên |
| `MINIO_BUCKET_HEALTH_EXAM` | ⛔ nếu bật HIS | Bucket MinIO của health-exam-server. **Bucket phải tạo sẵn** — service không tự tạo. Cùng cơ chế fail-fast như trên |
| `MINIO_SSL` | — | `true` để nối MinIO qua TLS, mặc định `false`. Không có port trong `MINIO_PATH` thì SDK lấy 80/443 theo cờ này — sai cờ là kết nối bị nuốt (treo), không bị từ chối |
| `MINIO_SKIP_CERT_VALIDATION` | — | `true` để chấp nhận cert tự ký của MinIO trên cụm, mặc định `false` |

MinIO client dựng **lazy** ở lượt ghi/đọc PDF đầu tiên, nên khi `HIS_EMR_ENABLED=false` các biến
MinIO có thể để trống và mọi endpoint hồ sơ vẫn phục vụ bình thường.

## Hợp đồng gọi

Mọi request nghiệp vụ **bắt buộc** header `X-Division-Id`; thiếu → `4001` chặn ngay ở
middleware. Miễn kiểm: `/health`, `/swagger`, `/`.

Phản hồi luôn là envelope PascalCase `{ErrorCode, Message, Data, TraceID}`.

### Ba đường vào, ba kiểu xác thực

| Nhánh | Ai gọi | Xác thực | Từ chối |
|---|---|---|---|
| `/v1/*` nghiệp vụ | FE, nhân viên | JWT của IAM (`SECRET_KEY`, `aud=DHS`), `ActorID` đọc từ claim `EmployeeID` | `4010` |
| `/v1/internal/*` | `iam-server` | `Authorization: Bearer {SECRET_INTER}`. **Token nhân viên hợp lệ vẫn bị chặn `4030`** — đây là đường tra định danh theo Mã NB, mở cho mọi nhân viên là mở một đường dò dữ liệu người bệnh | `4010` / `4030` |
| `/v1/hooks/*` | `form-server` | `X-Signature: sha256={HMAC-SHA256(nguyên văn thân gói, SECRET_INTER)}`. Token **không** thay được chữ ký: token trả lời "ai gọi", chữ ký trả lời "thân gói có bị sửa không" | `4030` |

🔴 **Reverse proxy phải chặn `/v1/internal/*` và `/v1/hooks/*` khỏi lưu lượng công cộng.**
Hai tiền tố được tách riêng chính là để chặn bằng một luật thay vì liệt kê từng đường. Việc
này nằm ở repo triển khai — **không cụm nào sở hữu nó**, phải có người nhận.

### `ActorID` chỉ đến từ token đã ký

Không bao giờ từ body, không bao giờ từ header. Ai đang thao tác là thứ quyết định mọi kiểm
tra chủ sở hữu; để client tự khai là bỏ ngỏ toàn bộ hàng rào đó.

### Con trỏ sang form-server — đừng đọc nhầm hai ô

`HostRefID` = **`SessionCode`** (mã ĐỢT) · `FRM_Submission.SubjectID` = **`RecordCode`**
(mã HỒ SƠ) · `SubjectCode` = `PatientCode`.

`form-server` so **cả hai** vế, nên cách ly người bệnh trong cùng một đợt đến từ `SubjectID`,
không phải từ `HostRefID`. Đề xuất cũ "đổi `HostRefID` sang `RecordCode`"
(`01-db-model §9.3`, `03-task` Q-DB-05) **đã bị bác bằng phép đo** — xem
`medviet-ai/docs/handoff/20260824-236-chot-h3b-va-subjectid.md §3`.
`ExamFormDraftService.HostRefIdOf` đúng như đang viết, **đừng sửa**.

⚠️ `SubjectID` do **FE** truyền lúc gọi `POST /v1/submissions` của `form-server`
(`/submissions/draft` không ghi gì xuống DB). health-exam-server không sửa gì — phản hồi
`/form-draft` đã mang sẵn `RecordCode`. Đây là **thay đổi duy nhất còn lại để cổng NB chạy
được**, và nó không thuộc repo nào của hai cụm.

## Endpoint

| Method | Path |
|---|---|
| GET | `/health` |
| GET | `/v1/info` · `/v1/meta/ping` |
| GET | `/v1/organizations` — `keyword`, `isActive`, `page`, `size` |
| GET | `/v1/exam-packages` — `keyword`, `variantCode`, `isActive`, `page`, `size` |
| GET | `/v1/exam-groups` — 10 Nhóm khám DTK_01..DTK_10 (hằng số, không cần seed) |
| GET · POST · PUT | `/v1/exam-sessions` · `/v1/exam-sessions/{sessionId}` |
| POST | `/v1/exam-sessions/{sessionId}/close` · `/reopen` (lý do bắt buộc, vào audit) |
| GET | `/v1/exam-sessions/{sessionId}/records` |
| GET | `/v1/exam-sessions/{sessionId}/progress` — tiến độ toàn đợt, đọc **cache**, 0 lời gọi ra ngoài |
| GET | `/v1/exam-records` · `/v1/exam-records/{recordId}` |
| POST · PUT | `/v1/exam-records` · `/v1/exam-records/{recordId}` — phase 1 không nhận `SessionID`; backend tự gắn vào đợt mặc định |
| GET | `/v1/exam-records` — filter riêng `recordCode`, `patientCode`, `fullName`, `identityNumber`, `phoneNumber`, `state`, `variantCode`, `sessionId`; khoảng ngày đăng ký inclusive bằng `fromDate`/`toDate` (`YYYY-MM-DD`), cùng `page`, `size` và `keyword` cũ; sắp xếp hồ sơ mới tạo lên đầu |
| GET | `/v1/exam-records/filter-options` — option trạng thái hồ sơ và đối tượng khám cho FE |
| POST | `/v1/exam-records/{recordId}/confirm` — chốt đăng ký, `Chưa đăng ký(0) → Chờ khám(1)` |
| POST | `/v1/exam-records/{recordId}/cancel` — hủy đăng ký (`0 → 4`) hoặc hủy khám (`1/2 → 5`), lý do bắt buộc |
| GET | `/v1/exam-records/{recordId}/form-draft` — layout + prefill từ form-server, kèm 4 khoá `ContextValues` |
| GET | `/v1/master-data/registration-options` — danh mục đăng ký (tĩnh: nhóm máu, quan hệ, giới tính; động: dân tộc, nghề nghiệp...) |
| GET | `/v1/master-data/provinces` — danh mục tỉnh / thành phố (`keyword`) |
| GET | `/v1/master-data/wards` — danh mục phường / xã theo tỉnh (`provinceCode`, `keyword`) |
| GET | `/v1/patients/search` — tìm hồ sơ người bệnh đang hiệu lực theo CCCD (`identityNumber`) trong đơn vị |
| POST · GET | `/v1/imports/*` — nạp Excel hai pha (upload → commit idempotent theo `ImportID`) |
| GET | `/v1/his-forms/{templateCode}` — Đọc định nghĩa biểu mẫu HIS (chỉ hỗ trợ mã `KSK-TREN18TUOI`, cache 300s) |
| GET | `/v1/exam-records/{recordId}/his-form` — Đọc định nghĩa biểu mẫu kèm `AdmissionID` liên kết của hồ sơ |
| GET | `/v1/exam-records/{recordId}/his-form/processes` — Danh sách tiến trình khám EMR của lượt khám tương ứng `AdmissionID` |
| GET | `/v1/exam-records/{recordId}/his-form/processes/{processId}` — Chi tiết tiến trình khám và các nhóm chuyên khoa (`M02_MedicalProcessDetail`) |
| GET | `/v1/exam-records/{recordId}/his-form/processes/{processId}/sign-workflow` — Cấu hình luồng ký số của tiến trình khám |
| POST | `/v1/exam-records/{recordId}/his-form/processes/{processId}/sections/{sectionKey}/submit` — Trình ký chuyên khoa (`sectionKey` = `ItemGroupID`) |
| POST | `/v1/exam-records/{recordId}/his-form/processes/{processId}/sections/{sectionKey}/sign` — Ký số chuyên khoa theo vai trò và bước duyệt |
| POST | `/v1/exam-records/{recordId}/his-form/processes/{processId}/sections/{sectionKey}/sign/cancel` — Hủy ký chuyên khoa (kèm lý do bắt buộc) |
| **POST** | **`/v1/internal/verify-portal-credentials`** — đối chiếu dữ kiện đăng nhập cho `iam-server`; chỉ trả dữ kiện, **KHÔNG đúc token** |
| **POST** | **`/v1/internal/reconcile-progress`** — chạy tay một lượt đối soát kéo |
| **GET** | **`/v1/internal/webhook-metrics`** — bộ đếm đường webhook (chuyển tiếp lệch, gói trùng, gói đến muộn) |
| **POST** | **`/v1/hooks/form-server`** — điểm nhận **duy nhất** cho mọi sự kiện của form-server |

⚠️ Tài liệu `02-api-spec` gọi tài nguyên này là `exam-profiles`; đường dẫn chốt với FE là
**`exam-records`** (khớp tên bảng `HEX_ExamRecord`). Đã chọn theo FE.

### Đợt kỹ thuật mặc định trong phase 1

FE tạo hoặc sửa hồ sơ qua `POST /v1/exam-records` và `PUT /v1/exam-records/{recordId}`
không gửi `SessionID`. Lần tạo hồ sơ đầu tiên của mỗi `DivisionID`, backend tự tạo đợt mở có
mã `PHASE1-DEFAULT`; các hồ sơ sau của cùng đơn vị dùng lại đợt này. Quan hệ `SessionID` trong
DB vẫn được giữ để phase quản lý đợt sau có thể bật lại mà không đổi mô hình dữ liệu.

Luồng import Excel vẫn chọn đợt rõ ràng và tiếp tục dùng `SessionID` nội bộ của lô import;
không bị chuyển sang `PHASE1-DEFAULT`.

### Trạng thái hồ sơ KSK (`ExamRecordState`)

| Mã | Tên trạng thái | Diễn giải |
|---|---|---|
| `0` | Chưa đăng ký (`NotRegistered`) | Hồ sơ vừa tạo từ danh sách Excel hoặc tạo tay tại quầy |
| `1` | Chờ khám (`Waiting`) | Đã xác nhận đăng ký tại quầy (`/confirm`), chờ vào các phòng khám |
| `2` | Đang khám (`InProgress`) | Đang thực hiện các nội dung khám (webhook cập nhật từ form-server) |
| `3` | Đã khám (`Completed`) | Đã khám xong và có kết luận (webhook cập nhật từ form-server) |
| `4` | Hủy đăng ký (`RegistrationCancelled`) | Hủy hồ sơ khi chưa bắt đầu khám (chuyển tiếp từ `0`) |
| `5` | Hủy khám (`ExamCancelled`) | Hủy hồ sơ khi đang chờ khám hoặc đang khám dở (chuyển tiếp từ `1` hoặc `2`) |

## Mã lỗi riêng của KSK

`4002` file Excel sai định dạng / thiếu cột bắt buộc · `4091` đợt khám đã đóng · `4092` hồ sơ
không thuộc đợt · `4093` người bệnh đã có hồ sơ trong đợt · `5020` phụ thuộc không phản hồi
(kèm `Data.Dependency`). Các mã kế thừa (`4001/4010/4030/4031/4040/4090/4221/5000`) giữ
nguyên nghĩa của form-server.

## Webhook từ form-server

Hợp đồng đầy đủ: `medviet-ai/docs/handoff/20260824-h2-hop-dong-webhook-form-server.md`.

⚠️ **Bộ phát bên form-server CHƯA tồn tại** — bên nhận đang phát biểu hợp đồng chứ không tiêu
thụ một hợp đồng đã chốt. Test được nhưng chưa nghiệm thu đầu-cuối được.

Ba lớp chống lệch nằm ba chỗ khác nhau, cố ý:

1. **Trùng** — `UNIQUE(EventID)` ở PostgreSQL. Gói trùng trả **200** kèm `Duplicated`,
   **không** phải 409: với bên phát, 4xx là "gửi hỏng, gửi lại".
2. **Đến muộn** — so `OccurredAt` với `HEX_ExamRecord.LastEventAt`. Mốc chỉ nhích khi sự kiện
   **được áp**, không nhích khi bỏ qua.
3. **Chuyển tiếp sai** — máy trạng thái kiểm **trạng thái nguồn**; sai thì bỏ qua và tăng
   `InvalidTransition` (đọc qua `/v1/internal/webhook-metrics`).

Nhận → ghi `HEX_WebhookInbox` → trả 200 ngay → worker áp hệ quả sau. Worker giữ khoá hàng bằng
`SELECT … FOR UPDATE SKIP LOCKED` trong transaction, hàng lỗi thử lại tối đa 5 lượt rồi nằm
lại làm dead-letter tại chỗ. Job đối soát kéo tự chữa khi mất hẳn sự kiện — webhook là đường
đẩy, đường đẩy nào cũng mất gói.

## Tích hợp biểu mẫu HIS EMR (`KSK-TREN18TUOI`)

Kiến trúc tích hợp trực tiếp biểu mẫu khám sức khỏe người lớn từ hệ thống HIS EMR (không qua `form-server`):
- **Nguồn dữ liệu chuẩn (Authoritative Source)**: HIS chịu trách nhiệm toàn bộ về định nghĩa biểu mẫu (`Details`, `Layout`), dữ liệu phiếu khám, tiến trình khám (`M02_MedicalProcess`), luồng ký (`SignatoryFlows`) và trạng thái chữ ký số. `health-exam-server` chỉ lưu `AdmissionID` (nullable) trên `HEX_ExamRecord` để liên kết lượt khám HIS tương ứng.
- **Dữ liệu phiếu khám ở Phase 1 là Read-Only**: Không ghi/sửa dữ liệu form, không gọi `CUEMR` hay API cập nhật mẫu.
- **Ký số độc lập từng chuyên khoa**: Mỗi chuyên khoa có mã định danh `sectionKey` (tương ứng `M02_MedicalProcessDetail.ItemGroupID`), được trình ký, ký số và hủy ký độc lập; không có thao tác ký gộp hàng loạt.
- **Ủy nhiệm và bảo mật**: Header xác thực được chuyển tiếp sang HIS theo cấu hình (`HIS_EMR_CREDENTIAL_HEADER`, mặc định `Authorization`). Service kiểm soát chặt chẽ danh tính người ký (`EmployeeId == context.ActorId`). Thông tin xác thực không bao giờ bị ghi log, cache hay trả về client.

### Cấu hình biến môi trường

```dotenv
HIS_EMR_ENABLED=false
HIS_EMR_BASE_URL=https://his.example.internal
HIS_EMR_TIMEOUT_SECONDS=10
HIS_EMR_CREDENTIAL_HEADER=Authorization
HIS_EMR_DEFINITION_CACHE_SECONDS=300

# Ký số KSK — bắt buộc khi HIS_EMR_ENABLED=true, thiếu là crash lúc khởi động
SIGN_SERVER_BASE_URL=http://sign-server:9940
SIGN_SERVER_TIMEOUT_SECONDS=60
SSM_BASE_URL=http://ssm-server:9930
MINIO_PATH=minio:9000                  # tên biến theo minio-cm/minio-secret dùng chung của cụm
MINIO_USERNAME=
MINIO_PASS=
MINIO_BUCKET_HEALTH_EXAM=health-exam   # bucket phải tạo sẵn
MINIO_SSL=false
MINIO_SKIP_CERT_VALIDATION=false
```

### Trình tự triển khai (Rollout Sequence)

1. **Chạy EF Migration**: Áp dụng migration `20260908102936_AddHisAdmissionId` để bổ sung cột `AdmissionID` vào bảng `HEX_ExamRecord`:
   ```bash
   dotnet ef database update --project HealthExam.Infrastructure --startup-project HealthExam.API
   ```
2. **Deploy Backend (tắt tính năng)**: Triển khai backend với cấu hình `HIS_EMR_ENABLED=false`. Mọi endpoint cũ hoạt động bình thường; các endpoint `/his-forms` và `/his-form` trả về `5022 HisBadGateway` một cách an toàn nếu được gọi.
3. **Cấu hình môi trường HIS Non-Production**: Đặt `HIS_EMR_BASE_URL` trỏ tới endpoint HIS nội bộ, cấu hình `HIS_EMR_CREDENTIAL_HEADER` (mặc định `Authorization`), đặt `HIS_EMR_ENABLED=true`.
4. **Kiểm tra đọc định nghĩa & tiến trình**:
   - Gọi `GET /v1/his-forms/KSK-TREN18TUOI` xác nhận nhận đủ metadata và layout cây (được cache 300s).
   - Chọn một hồ sơ test có liên kết `AdmissionID` hợp lệ, gọi `GET /v1/exam-records/{recordId}/his-form/processes` xác nhận lấy được danh sách tiến trình khám.
5. **Kiểm tra chu trình ký số chuyên khoa**:
   - Dùng tài khoản nhân viên thật trên môi trường test, thực hiện trình ký (`POST .../sections/{sectionKey}/submit`), ký số (`POST .../sections/{sectionKey}/sign`), và hủy ký (`POST .../sections/{sectionKey}/sign/cancel`).
   - Xác nhận HIS cập nhật trạng thái ký chính xác và phản hồi trả về mã thành công `0`.
6. **Bật Feature Flag trên Frontend**: Khi việc kiểm thử thành công, kích hoạt cờ tính năng biểu mẫu HIS KSK trên giao diện người dùng.

## Đã cắt (chưa làm)

- Bảng: `HEX_ParaclinicalOrder(/Item)`, `HEX_IntegrationOutbox` — **P3**, chỉ định CLS.
  Hai sự kiện `submission.attachment.added/removed` đã được **nhận biết và bỏ qua có ghi
  chú**, không coi là sự kiện lạ; khi P3 vào thì sửa đúng nhánh đó trong `WebhookProcessor`.
- `HEX_PatientPortal*`, `/portal/*`, đúc token cổng người bệnh — **đã chuyển sang
  `iam-server`** (cổng 9930). Service này chỉ giữ **dữ liệu định danh** và mở
  `/v1/internal/verify-portal-credentials`. Đây là ranh giới cố ý: module nghiệp vụ giữ dữ
  liệu, service xác thực giữ chính sách và khoá ký.
- Endpoint: `DELETE` đợt (soft-delete); `audit`/`DELETE` của hồ sơ (riêng `POST /v1/exam-records/{id}/cancel` đã được hiện thực);
  chỉ định CLS; điều kiện ký kết luận.
- Ghi danh mục (`POST/PUT/DELETE` organizations & exam-packages) — dữ liệu vào bằng seed.
- **Không có circuit breaker** ở `FormServerClient` dù gate H2-01 có nhắc: cả service chỉ có
  ba lời gọi ra ngoài và luật thử lại đúng một dòng. Nêu ra để biết đây là cố ý.
- `/progress` của form-server **chưa trả `SubmissionState`**, nên nhánh *Đang khám → Đã khám*
  của job đối soát nằm im và được đếm vào `ReconcileResult.Undecidable` — số nhóm khám đã xong
  KHÔNG nói được phiếu đã ký kết luận hay chưa, mà đoán là đẩy hồ sơ sang *Đã khám* trước khi
  bác sĩ ký. Đã gửi REQUEST sang cụm 236.
