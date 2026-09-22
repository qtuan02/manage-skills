# Ký mục khám: chọn Người xác nhận (ký thay) — thiết kế

**Ngày:** 2026-09-19 · **Jira:** [PROJ-2374](https://jira.mdsco.vn/browse/PROJ-2374) · **Mockup:** `PRODUCT/[KHÁM SỨC KHỎE]/1. Giao diện/3. Trả kết quả Khám sức khỏe/mh5-hifi.html` (modal "Ký số nội dung khám?")

## 1. Mục tiêu

Ở bước **Ký số một mục khám** (MH5, tab Khám lâm sàng), bác sĩ đang thao tác (BS A) chọn **Người xác nhận** (BS B) trong danh sách nhân viên có vai trò ký của bước và nhập **Thời gian thực hiện**. Chữ ký của bước = BS B, thời gian ký = thời gian thực hiện. Lúc ký kết luận, PDF được đóng chữ ký bằng chứng thư của BS B.

Quyết định đã chốt với product (19/09):

- Người xác nhận **là người ký thật**: `HEX_ExamRecordSignStep.SignedBy*` = BS B; vòng ký PDF ở kết luận không đổi.
- **Ai lưu được mục khám thì bấm ký được** — bỏ gate vai trò trên người bấm; chỉ người xác nhận phải có vai trò ký của bước + chứng thư.
- **Không có bước Lưu riêng** cho 2 trường này; chọn ngay trong modal ký.
- `SignedByEmployeeID/Code/Name` + `SignedAt` sẵn có = người xác nhận + thời gian thực hiện. **Thêm 2 cột** `PerformedByEmployeeID bigint NULL`, `PerformedByEmployeeName varchar(255) NOT NULL DEFAULT ''` trên `HEX_ExamRecordSignStep` = **người bấm ký** (BS A) để dòng meta hiện "Người thực hiện" như Jira bước 5 (quyết định 19/09, sau khi xác nhận không có nguồn "người tạo" theo mục nào khác: HIS `M03_EMRData.CreatedAccID` là của cả phiếu). 1 EF migration `AddSignStepPerformedBy`.

## 2. Phạm vi

| Repo | Thay đổi |
|---|---|
| `health-exam-server` | `POST .../sections/{itemGroupId}/sign` nhận body; `GET .../his-form/sections/{itemGroupId}` trả danh sách người ký + tên người đã ký; HIS client thêm 1 lượt gọi; migration 2 cột `PerformedBy*`; `Steps[]` (eligibility + kết luận) thêm `PerformedBy*` |
| `turbo-web/apps/health-exam` | Thay `MOCK_DOCTORS` bằng `Signers[]` từ GET section; dòng meta đọc Người thực hiện/Người xác nhận từ BE |

Không đổi: luật hủy ký mục, luật eligibility, vòng ký PDF ở kết luận, `SaveHisFormSection`, `HEX_SignStepMap`. (`GetConclusionEligibility`/`SignConclusion` chỉ thêm 2 trường vào mapping `Steps[]`.)

## 3. API

### 3.1 `POST /v1/exam-records/{recordId}/sections/{itemGroupId}/sign`

Body **tùy chọn** (FE `ExamSectionSignPayload` đã gửi từ 19/09 — giữ nguyên tên trường):

```json
{ "ConfirmedByEmployeeID": 40, "SignedAt": "2026-09-19T01:30:00.000Z" }
```

- Cả hai trường nullable. Không body / body rỗng → hành vi cũ (người ký = người bấm, giờ = now). Controller khai `[FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ExamSectionSignRequest? request`; endpoint test phải chứng minh POST không body **không** bị 415.
- `SignedAt` ISO 8601 có offset/Z; server lưu UTC.

Luật xử lý (trong `SignExamSectionHandler`, thứ tự như hiện tại: actor là nhân viên → row lock → hồ sơ chưa ký kết luận → bước có trong `HEX_SignStepMap`):

1. `signerId = ConfirmedByEmployeeID ?? actor.EmployeeId`.
2. Gọi HIS `GET api/GetCodeList?key=EmployeeRole&filterCode={step.SWRoleID}&amount=0` → danh sách nhân viên giữ vai trò ký của bước. **Phải truyền `amount=0`** (HIS mặc định cắt 20).
   - HIS lỗi/không cấu hình → `HisBadGateway` (502), fail-closed như hiện tại. Không ký với vai trò chưa xác định.
   - `signerId` không có trong danh sách → `Forbidden` `"Người xác nhận không có vai trò ký của bước \"{StepName}\""`. Áp cho cả nhánh fallback actor (một luật cho mọi người).
   - `EmployeeCode`/`EmployeeName` lấy từ dòng HIS khớp — không tin FE.
3. `SignedAt = command.SignedAt ?? now`. Nếu `> now + 5 phút` → `BadRequest` `"Thời gian ký không được ở tương lai"`. Không chặn quá khứ (product: ký cho ca đã khám).
4. Kiểm chứng thư theo `EmployeeCode` của **người ký** (`ICertificateGateway.GetAsync`), thông điệp lỗi như hiện tại.
5. Snapshot: `SignedByEmployeeID/Code/Name = người ký`, `SignedAt = giá trị bước 3`, `PerformedByEmployeeID/Name = actor` (người bấm), `Status = Signed`. Row lock, `UniqueConflict`, commit giữ nguyên. Hai lượt gọi ngoài (HIS danh sách, SSM cert) đều nằm trong tx `FOR UPDATE` — chấp nhận: ký đồng thời trên cùng một hồ sơ là ca hiếm, và cert check vốn đã ở đó.
6. Audit `SECTION_SIGN_SNAPSHOT` thêm `ActorEmployeeID`, `SignerEmployeeID`, `SignedAt` để phân biệt người bấm và người ký.

Response `ExamSectionSignResult` thêm `SignedByEmployeeName`, `PerformedByEmployeeID`, `PerformedByEmployeeName` (FE hiện ngay không cần refetch). Các trường cũ giữ nguyên.

Controller **bỏ** `ResolveSignRoleIdsAsync` ở endpoint này; `SignExamSectionCommand` bỏ `RoleIds`, thêm `ConfirmedByEmployeeId`, `SignedAt`, `Credential`, `TraceId` (handler cần credential gọi HIS — cùng khuôn `SignConclusionCommand`).

### 3.2 `GET /v1/exam-records/{recordId}/his-form/sections/{itemGroupId}`

`ExamFormSection` thêm:

```json
{
  "CurrentStepSignedByEmployeeName": "BS. Nguyễn Văn An",
  "Signers": [
    { "EmployeeID": 40, "EmployeeCode": "40", "EmployeeName": "BS. Nguyễn Văn An" }
  ]
}
```

- `Signers` = danh sách HIS `EmployeeRole` theo `SWRoleID` của bước ứng với `itemGroupId` (`amount=0`), **chỉ nạp khi bước chưa `Signed`** (đã ký thì hộp ký không mở — khỏi tốn một lượt HIS trả cả bệnh viện mỗi lần mở mục). Bước không cấu hình / đã ký → `[]`. HIS lỗi → `[]` + log warning, **không** fail request (cùng chính sách với đọc `REMR` trong handler này); FE hiện "Không tải được danh sách người xác nhận — sẽ ký bằng tài khoản của bạn" khi rỗng.
- `CurrentStepSignedByEmployeeName` = `snapshot.SignedByEmployeeName` (đã lưu từ trước, chưa trả).
- `PUT` save section trả cùng DTO qua `GetHisRecordSectionHandler` nên tự có 2 trường mới.

### 3.3 HIS client

`IHisEmrClient.ListSignRoleEmployeesAsync(long roleId, HisCallContext context, ct)` → `HisClientResult<IReadOnlyList<SignRoleEmployee>>` với `SignRoleEmployee(long EmployeeID, string EmployeeCode, string EmployeeName)`.

- Route: `RouteGetCodeList` + `?key=EmployeeRole&filterCode={roleId}&amount=0`. Không lọc `deptID` (danh sách toàn đơn vị; product chưa yêu cầu lọc khoa).
- Parse: `Data.Data[]` (`ResponseFilterData`) hoặc `Data[]`, mỗi dòng `CodeID`→EmployeeID, `Code`→EmployeeCode, `CodeName`/`DisplayName`→EmployeeName; bỏ dòng `CodeID<=0`; khử trùng theo EmployeeID. Mirror `ParseIcd10Choices`.
- `HisOperation.ListSignRoleEmployees` mới, thêm vào tập `retryConnectionOnce` cạnh `ListSignRoles`.
- Default body trên interface trả `NotFound "Not implemented"` như các method khác để fake test cũ không vỡ.

### 3.4 `Steps[]` của `GET .../conclusion-eligibility` và `POST .../conclusion/sign`

`ConclusionSignStepResult` thêm `PerformedByEmployeeID: long?`, `PerformedByEmployeeName: string` (rỗng khi chưa ký). FE header đã bind `Steps[]` nên "Người thực hiện" đọc từ đây.

## 4. FE (`turbo-web/apps/health-exam`)

- `packages/types/src/health-exam/his-form.ts` (`HisFormSection`): thêm `CurrentStepSignedByEmployeeName: string | null`, `Signers: SignerChoice[]` (`EmployeeID`, `EmployeeCode`, `EmployeeName`).
- `category-sign-form.tsx`: bỏ `MOCK_DOCTORS` (xóa `~/constants/mock-doctors.ts`), nhận `signers` từ cache section (`healthExamHisFormQueryKeys.bySection`, `CategoryPanel` đã tải trước khi [Ký số] bấm được); mặc định `confirmedBy` = `EmployeeID` của `useCurrentUserQuery()` nếu có trong `Signers`, không có thì để trống (bắt buộc chọn khi có danh sách). **`Signers` rỗng/không có (HIS lỗi hoặc BE cũ) → chế độ khoan dung**: ẩn/khoá ô chọn, hiện "Không tải được danh sách người xác nhận — sẽ ký bằng tài khoản của bạn", nút [Xác nhận] vẫn bật, payload **không** gửi `ConfirmedByEmployeeID` (BE fallback = người bấm). Nhờ vậy FE mới chạy được với BE cũ.
- `packages/types` `ExamSectionSignPayload.ConfirmedByEmployeeID: number | null`.
- `category-header.tsx`: giữ 3 ô — **Người thực hiện** = `Steps[].PerformedByEmployeeName`, **Người xác nhận** = `Steps[].SignedByEmployeeName` (fallback `confirmedByName` trong phiên cho tới khi eligibility refetch), **Thời gian** = `SignedAt`.

## 5. Ràng buộc & rủi ro

- **Ký thay là chủ đích**: server dùng chứng thư BS B khi BS B không thao tác. Audit giữ cả actor lẫn signer để truy vết.
- **Ngày ký trên PDF** = `SignedAt` do người dùng nhập (`SignConclusion.cs:177` đã dùng `snap.SignedAt`); dấu thời gian mật mã của sign-server vẫn là giờ ký thật. Chấp nhận theo Jira.
- Chứng thư BS B kiểm ngay lúc ký mục (bước 4) để không kẹt tới ký kết luận.
- Hủy ký vẫn `Remove(snapshot)` → chọn lại người xác nhận khi ký lại; khớp mockup "xóa BS + giờ".
- **Thứ tự deploy: FE trước (hoặc cùng lúc), KHÔNG deploy BE trước.** FE đang chạy trên main gửi `ConfirmedByEmployeeID` từ `MOCK_DOCTORS` (1274/4210/9999); BE mới sẽ kiểm ID đó với HIS → 403 mọi lượt ký. FE mới + BE cũ an toàn nhờ chế độ khoan dung (§4). Client không body vẫn chạy.
- Migration `AddSignStepPerformedBy` phải áp trước khi bump tag BE (`dotnet ef database update --project HealthExam.Infrastructure --startup-project HealthExam.API`, xem README).

## 6. Kiểm thử

Backend (xUnit, `HealthExam.Tests`):

- `SignExamSectionTests`: (a) chọn signer hợp lệ → `SignedBy*` = signer, `SignedAt` = giá trị gửi, `PerformedBy*` = actor, actor không cần role; (b) signer không trong danh sách → `Forbidden`; (c) HIS `EmployeeRole` lỗi → `HisBadGateway`, không ghi snapshot; (d) không body, actor có trong danh sách → signer = actor, `SignedAt` = now; actor không trong danh sách → `Forbidden`; (e) `SignedAt` tương lai → `BadRequest`; (f) signer không có cert → `SignPrecondition`.
- `SignExamSectionEndpointTests`: POST có body JSON và POST không body/không Content-Type đều đi tới handler (không 415).
- `HisEmrClient` parse `EmployeeRole` (shape `ResponseFilterData` lồng `Data.Data`, khử trùng, `amount=0` trong URL).
- `GetHisRecordSectionSignersTests` (mới): GET section trả `Signers` theo `SWRoleID` của bước khi bước chưa ký; bước đã `Signed` → không gọi HIS, `Signers=[]`, có `CurrentStepSignedByEmployeeName`; HIS lỗi → `Signers=[]` và vẫn thành công; mục không có bước → `Signers=[]`.
- `ConclusionEligibilityFromMapTests`: `Steps[]` mang `PerformedByEmployeeID/Name` sau khi ký mục.
- Migration: `dotnet ef migrations add AddSignStepPerformedBy` sinh đúng 2 cột; `HealthExamDbContextModelSnapshot` cập nhật.
- `ApiContractSurfaceTests`: field mới của `ExamFormSection` (BE DTO), `ExamSectionSignResult`, `ExamSectionSignRequest`.
- Cập nhật fake `IHisEmrClient` trong test có sẵn nếu compile yêu cầu.

Frontend (vitest): `category-sign-form` dùng `Signers` từ props, default = nhân viên đăng nhập, rỗng → chế độ khoan dung (thông báo, vẫn submit, payload không có `ConfirmedByEmployeeID`); `category-header` 3 ô đọc `PerformedByEmployeeName`/`SignedByEmployeeName`; `health-exam-signing-service` gửi body đúng tên trường; `clinical-tab` hiện tên người ký.

Docs: `docs/api/his-section-signing-api.md` (body + luật 3.1), `docs/api/conclusion-pdf-signing-guide.md` (ghi chú `SignedAt` là thời gian thực hiện do người dùng nhập).
