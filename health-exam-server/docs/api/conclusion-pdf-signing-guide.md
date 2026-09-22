# Ký số KSK — hợp đồng API cho Frontend

Luồng ký độc lập (spec `docs/superpowers/specs/2026-09-18-ksk-standalone-signing-design.md`):
bác sĩ bấm **Ký số** ở từng mục khám → server chỉ ghi snapshot cục bộ và khóa mục đó; tới bước
**Ký kết luận** server render PDF một lần, ký lần lượt mọi marker qua sign-server, lưu MinIO.
his-server không nằm trên đường ký — chỉ còn là nguồn vai trò ký (`M02F30000/GetPermissionGroup`),
biểu mẫu và render PDF nháp.

Mọi request cần `Authorization: Bearer <token nhân viên>` và `X-Division-Id`. Phản hồi là envelope
PascalCase `{ErrorCode, Message, Data, TraceID}`; PDF trả `application/pdf` thuần.

## 1. Bốn endpoint

| # | Endpoint | Body | Trả về |
|---|---|---|---|
| 1 | `POST /v1/exam-records/{recordId}/sections/{itemGroupId}/sign` | tùy chọn `{ConfirmedByEmployeeID?, SignedAt?}` | `ExamSectionSignResult` |
| 2 | `POST /v1/exam-records/{recordId}/sections/{itemGroupId}/sign/cancel` | rỗng | `true` |
| 3 | `GET  /v1/exam-records/{recordId}/conclusion-eligibility` | — | `ConclusionEligibilityResult` |
| 4 | `POST /v1/exam-records/{recordId}/conclusion/sign` | **rỗng** | `ConclusionSignResult` |

`itemGroupId` là `int`, cùng định danh FE đã dùng ở `GET .../his-form/sections/{itemGroupId}`.

**Body `POST sections/{itemGroupId}/sign` (PROJ-2374, tùy chọn):**

```json
{ "ConfirmedByEmployeeID": 40, "SignedAt": "2026-09-19T01:30:00.000Z" }
```

- `ConfirmedByEmployeeID` — Người xác nhận, **là người ký thật**: snapshot ghi `SignedBy*` = người này và chứng thư của họ đóng lên PDF ở kết luận. Phải nằm trong `Signers[]` của `GET his-form/sections/{itemGroupId}` (HIS `GetCodeList?key=EmployeeRole&filterCode={SWRoleID}&amount=0`); không thuộc ⇒ `403` "Người xác nhận không có vai trò ký của bước …". Bỏ trống/`null` ⇒ người ký = người bấm (vẫn phải có vai trò).
- `SignedAt` — thời gian thực hiện, ghi vào `SignedAt` và là **ngày ký in trên PDF**. Tương lai quá 5 phút ⇒ `400`. Bỏ trống ⇒ now.
- Người bấm KHÔNG cần vai trò ký; được ghi vào `PerformedByEmployeeID/Name`. HIS không trả được danh sách ⇒ `502`, không ghi snapshot. Không body/không Content-Type vẫn hợp lệ.
- **Deploy:** FE trước hoặc cùng lúc BE. FE cũ gửi `ConfirmedByEmployeeID` mock nên BE mới trước sẽ 403 mọi lượt ký. Migration `AddSignStepPerformedBy` áp trước khi bump tag. Migration tự backfill `PerformedBy* = SignedBy*` cho snapshot ký trước PROJ-2374 (người ký khi đó luôn là người bấm). Rolling BE: pod cũ còn ghi snapshot sau khi backfill đã chạy sẽ để `PerformedBy*` NULL — hoàn tất rollout rồi chạy lại câu UPDATE của migration (idempotent) nếu cần.

Gửi POST **không body, không Content-Type** đều được (axios `post(url)` / `post(url, undefined)`).

PDF: `GET /v1/exam-records/{recordId}/registration-form/preview` — hồ sơ `Signed` trả file đã ký
từ MinIO (thiếu file → lỗi, không bao giờ trả bản nháp thay thế); chưa ký trả bản nháp render từ HIS.

## 2. Khóa mục khám và tiến độ ký (Signing Progress State)

### Luồng chuyển trạng thái (State Sequence)
```text
Waiting --save first clinical form--> InProgress
step InProgress --section sign--> Signed
sign progress: 0/8 -> 1/8 -> ... -> 8/8
all clinical steps Signed --conclusion sign--> PDF Signed
```

- **Tiến độ ký (`SigningProgressDone / SigningProgressTotal`):**
  - Được tính độc lập từ các step lâm sàng non-conclusion có trạng thái `Signed`.
  - Không nhầm lẫn với `ExamRecord.ProgressDone / ProgressTotal` (vốn là tiến độ webhook cận lâm sàng).
  - Trả về trong phản hồi `GET/PUT his-form/sections/{itemGroupId}` và `POST sections/{itemGroupId}/sign`.
- `CurrentStepSignedByEmployeeName` — tên người đã ký bước (null khi chưa ký).
- `Signers[] {EmployeeID, EmployeeCode, EmployeeName}` — nhân viên giữ `SWRoleID` của bước, để FE vẽ dropdown "Người xác nhận"; chỉ nạp khi bước chưa `Signed`; `[]` khi mục không có bước ký, đã ký, hoặc HIS lỗi (FE báo không tải được danh sách và ký bằng tài khoản của mình).
- **UI Acceptance:**
  - `POST save first clinical section` (ví dụ M05): `RecordState = InProgress`, `CurrentStepStatus = InProgress`, `SigningProgressDone = 0`, `SigningProgressTotal = 8`.
  - `POST sections/{itemGroupId}/sign`: `CurrentStepStatus = Signed`, `SigningProgressDone = 1`, `SigningProgressTotal = 8`, `CurrentStepSignedAt != null`.
  - Huy hiệu (badge) danh mục quy trình ở màn hình khám đọc từ `SigningProgressDone / SigningProgressTotal`.

Mục khám bị khóa **khi và chỉ khi** đã ký số (`Status == "Signed"` hoặc `"Snapshot"`). Bước đang ở `InProgress` (mới lưu, chưa ký) vẫn cho phép sửa nội dung.
Server chặn thật: `PUT .../his-form/sections/{itemGroupId}` vào mục đã ký → `409` "Mục khám đã ký số,
hủy ký trước khi sửa". Hồ sơ đã `Signed` chặn mọi thao tác sửa (mục khám, phiếu đăng ký, hành chính)
với `409` "Hồ sơ đã ký kết luận, không sửa được". FE tắt nút chỉ là tiện ích.

## 3. DTO (đúng tên trường serialize)

### `ExamSectionSignResult` — endpoint 1
```json
{
  "ItemGroupId": 101,
  "SwStep": 1,
  "StepName": "Khám thể lực",
  "SignedByEmployeeID": 1274,
  "SignedByEmployeeName": "BS. Nguyễn Văn An",
  "SignedAt": "2026-09-18T02:30:00Z",
  "Status": "Signed",
  "SigningProgressDone": 1,
  "SigningProgressTotal": 8,
  "PerformedByEmployeeID": 1274,
  "PerformedByEmployeeName": "BS A"
}
```

### `ConclusionEligibilityResult` — endpoint 3
```json
{
  "ProfileID": "92256694-2930-4b61-9e72-57f746f5bafc",
  "RecordCode": "KSK-2026-000123",
  "SubmissionID": null,
  "Conditions": [
    { "Code": "A", "Label": "Khám lâm sàng", "Satisfied": false,
      "Detail": "1/2 mục khám đã ký số", "Source": "health-exam-server" },
    { "Code": "B", "Label": "Cận lâm sàng", "Satisfied": true,
      "Detail": "3/3 chỉ định đã trả kết quả hoặc đã huỷ", "Source": "health-exam-server" }
  ],
  "CanSignConclusion": false,
  "SignedByEmployeeID": null,
  "SignedAt": null,
  "Steps": [
    { "SwStep": 1, "StepName": "Khám thể lực", "ItemGroupID": 101, "SwRoleId": 45,
      "Status": "Signed", "SignedByEmployeeID": 1274, "SignedAt": "2026-09-18T02:30:00Z",
      "SignedByEmployeeName": "BS Nguyễn Văn A", "PerformedByEmployeeID": 1274, "PerformedByEmployeeName": "BS A" },
    { "SwStep": 2, "StepName": "Khám mắt", "ItemGroupID": 102, "SwRoleId": 45,
      "Status": "InProgress", "SignedByEmployeeID": null, "SignedAt": null, "SignedByEmployeeName": "", "PerformedByEmployeeID": null, "PerformedByEmployeeName": "" },
    { "SwStep": 3, "StepName": "Kết luận", "ItemGroupID": null, "SwRoleId": 60,
      "Status": "", "SignedByEmployeeID": null, "SignedAt": null, "SignedByEmployeeName": "", "PerformedByEmployeeID": null, "PerformedByEmployeeName": "" }
  ],
  "MissingSteps": [2]
}
```
- `ProfileID` = `recordId` (tên trường lịch sử, giữ nguyên).
- `Steps` là **mọi** bước trong bảng map của bộ biểu mẫu hiện tại; bước kết luận có `ItemGroupID = null`.
  `Status` rỗng = chưa thực hiện; `InProgress` = đang khám / đã lưu form; `Signed` = chuyên khoa đã ký.
- `MissingSteps` = `SwStep` của các bước lâm sàng chưa có trạng thái `Signed` (`InProgress` hoặc rỗng đều tính là thiếu).
- `CanSignConclusion` = mọi `Conditions.Satisfied` **và** `MissingSteps` rỗng **và** nhân viên hiện tại
  giữ `SwRoleId` của bước kết luận **và** hồ sơ chưa `Signed`. Endpoint này **chỉ đọc DB**: HIS không trả
  được vai trò thì vẫn `200`, chỉ `CanSignConclusion=false`.

### `ConclusionSignResult` — endpoint 4 (cả khi lỗi 422/403/409, nằm trong `Data`)
```json
{
  "RecordID": "92256694-2930-4b61-9e72-57f746f5bafc",
  "Status": "Signed",
  "SignedFilePath": "DEV/2026/09/92256694-2930-4b61-9e72-57f746f5bafc.pdf",
  "SignedByEmployeeID": 1009,
  "SignedAt": "2026-09-18T03:05:12Z",
  "Conditions": [ { "Code": "B", "Label": "Cận lâm sàng", "Satisfied": true, "Detail": "...", "Source": "health-exam-server" } ],
  "Steps": [ /* ConclusionSignStepResult, như trên, Status = "Signed" */ ],
  "MissingSteps": []
}
```

## 4. Trạng thái

| Trường | Giá trị |
|---|---|
| `ConclusionSignResult.Status` (= `HEX_ExamRecord.SignStatus`) | `New` · `Signed` · `Failed` (upload MinIO hỏng sau khi ký — gọi lại ký toàn bộ) |
| `Steps[].Status` | `""` (chưa ký) · `InProgress` (đang khám) · `Snapshot` (đang xử lý ký PDF) · `Signed` (chuyên khoa/kết luận đã ký) · `Failed` |

#### `HisSignedFilePath` trên mọi `ExamRecordItem` (18/09/2026)

`GET /v1/exam-records`, `GET /v1/exam-records/{id}` và `GET /v1/patients/{id}/exam-history`
trả thêm `HisSignedFilePath: string` — đường dẫn PDF đã ký mà HIS trả về sau khi ký xong;
`""` khi chưa ký. **FE không mở trực tiếp được** đường dẫn này (HIS `api/Sign/ViewFile` yêu
cầu Bearer của nhân viên và FE không nói chuyện thẳng với HIS). Cách dùng:

- `HisSignStatus === "Signed" && HisSignedFilePath !== ""` → hiện nút "Xem PDF".
- Bấm → `GET /v1/exam-records/{RecordID}/registration-form/preview` (đã trả đúng file ký khi
  hồ sơ ở trạng thái `Signed`), nhận `application/pdf` và mở blob.

---

## 5. Mã lỗi

| HTTP | `ErrorCode` | Khi nào | Endpoint |
|---|---|---|---|
| 403 | 4030 | Không phải nhân viên; không giữ `SwRoleId` của bước | 1, 4 |
| 404 | 4040 | Không tìm thấy hồ sơ; hủy ký mục chưa ký | 1, 2, 3, 4 |
| 409 | 4090 | Hồ sơ đã `Signed` (ký/hủy mục, sửa nội dung); lượt ký đồng thời ("đang được ký bởi lượt khác") | 1, 2, 4, các API sửa |
| 422 | 4221 | Mục chưa cấu hình bước ký; chưa có/hết hạn chứng thư số; chưa đủ điều kiện (xem `Conditions`, `MissingSteps`); hồ sơ chưa có biểu mẫu HIS | 1, 4 |
| 502 | 5022 | HIS không trả được vai trò ký (chỉ 1 và 4 — 3 xuống cấp); sign-server / render PDF lỗi; "Ký xong nhưng không lưu được file" | 1, 4 |

Gọi lại endpoint 4 khi hồ sơ đã `Signed` → `200` trả trạng thái đã lưu, không ký lại, không tạo file mới.
Ký hỏng giữa chừng (cert, sign-server) → không có file, `Status` vẫn `New`, snapshot giữ nguyên; gọi lại chạy sạch từ đầu.
