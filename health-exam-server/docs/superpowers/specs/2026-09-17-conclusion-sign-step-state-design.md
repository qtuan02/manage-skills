# Conclusion PDF Signing Step State Design

**Date:** 2026-09-17  
**Scope:** `health-exam-server` only  
**Status:** Approved for implementation

## 1. Mục tiêu

Chỉ triển khai ký **kết luận PDF** qua HIS. Health-exam lưu state machine của
toàn bộ workflow ký HIS và thời điểm từng bước được ký để frontend theo dõi.
Không triển khai hoặc sử dụng luồng ký từng section chuyên khoa.

## 2. Phạm vi và ràng buộc

Trong phạm vi:

- Dùng `POST /v1/exam-records/{recordId}/conclusion/sign` cho mọi lần ký kết luận.
- Lấy workflow động từ HIS theo `FileDocTypeID` của biểu mẫu thực tế.
- Lưu bước, role, trạng thái, nhân viên ký và thời gian ký trong health-exam.
- Đồng bộ lại trạng thái từ HIS trước khi trả kết quả.
- Chỉ thay đổi `health-exam-server`; không sửa HIS.

Ngoài phạm vi:

- `.../his-form/processes/{processId}/sections/.../submit` và `/sign`.
- Gán nhân viên thủ công cho workflow.
- Chỉnh workflow master data HIS.
- Lưu byte PDF trong health-exam.

## 3. Nguồn sự thật

- HIS là nguồn sự thật cho `FileDocTypeID`, workflow, transaction, bước hiện tại,
  quyền ký và file PDF đã ký.
- Health-exam lưu bản theo dõi cục bộ để hiển thị lịch sử và resume an toàn; không
  tự quyết định thay HIS ai được ký.
- JWT cung cấp nhân viên hiện tại (`EmployeeID`); role/bước lấy từ HIS.

## 4. Mô hình dữ liệu

Giữ các cột tổng hợp hiện có trên `HEX_ExamRecord`: `HisSignTransactionID`,
`HisSignStatus`, `HisSignKeyID`, `HisSignedFileDocID`, `HisSignedFilePath`,
`HisSignedByEmployeeID`, `HisSignedAt`.

Thêm bảng `HEX_ExamRecordSignStep`:

| Cột | Kiểu | Ý nghĩa |
| --- | --- | --- |
| `ID` | `uuid` | Khóa bản ghi |
| `DivisionID` | `varchar` | Tenant |
| `RecordID` | `uuid` | Hồ sơ khám |
| `SWTID` | `bigint` | Transaction HIS |
| `FileDocTypeID` | `int` | Loại tài liệu |
| `SWStep` | `int` | Số bước HIS |
| `StepName` | `varchar` | Tên bước snapshot |
| `SWRoleID` | `bigint` | Role HIS |
| `Status` | `varchar(30)` | `Pending`, `InProcessing`, `Signed`, `Cancelled`, `Error` |
| `SignedByEmployeeID` | `bigint nullable` | Nhân viên ký |
| `SignedAt` | `timestamptz nullable` | Thời gian HIS xác nhận |
| `LastObservedAt` | `timestamptz` | Lần đồng bộ cuối |
| `CreatedDate` / `ModifiedDate` | `timestamptz` | Audit |

Ràng buộc: unique `(DivisionID, RecordID, SWTID, SWStep)`, index
`(DivisionID, RecordID, Status)`, FK nội bộ tới `HEX_ExamRecord`, không FK sang HIS.
Mỗi transaction/PDF revision có một snapshot workflow, không ghi đè lịch sử cũ.

## 5. State machine

```text
Pending -> InProcessing -> Signed
                     \-> Cancelled / Error
```

- Chỉ cập nhật `SignedByEmployeeID` và `SignedAt` khi HIS xác nhận ký.
- `HisSignStatus = Signed` chỉ khi HIS xác nhận toàn bộ workflow hoàn tất.
- Nếu còn bước sau, transaction là `InProcessing`; bước sau là `Pending`.
- Đồng bộ idempotent theo unique key, không tạo dòng trùng.

## 6. API và luồng xử lý

### 6.1 Eligibility

```http
GET /v1/exam-records/{recordId}/conclusion-eligibility
```

API dùng cho UI, trả điều kiện y khoa và state machine local/HIS nếu đã có
transaction. Endpoint ký vẫn tính lại điều kiện.

### 6.2 Ký kết luận

```http
POST /v1/exam-records/{recordId}/conclusion/sign
```

Body không cần step, role, employee, SWTID hoặc SWTDetailID.

Lần đầu: tính điều kiện; lấy `FileDocTypeID`; lấy workflow và lưu snapshot; render
PDF; submit HIS; lưu transaction/file; cập nhật bước HIS xác nhận.

Các lần sau: khóa hồ sơ; đọc bước đang chờ từ HIS cho nhân viên JWT; từ chối nếu
không có bước hợp lệ; ký đúng transaction detail HIS trả về; đọc lại HIS; cập nhật
step và cột tổng hợp; trả `InProcessing` hoặc `Signed` cùng các step.

## 7. Đồng bộ và idempotency

- Không submit PDF mới khi còn transaction chưa kết thúc.
- Dùng `HisSignKeyID` ổn định theo `RecordID` và PDF revision.
- Retry sau `Signed` trả state đã lưu, không submit lại.
- Nếu submit thành công nhưng refresh lỗi, vẫn giữ SWTID/FileDocID và trả trạng thái
  cần đồng bộ.
- Dùng row lock/transaction hoặc atomic compare-and-set; EF tracking đơn thuần không
  đủ chống race condition.

## 8. Lỗi

- Workflow rỗng/không hợp lệ: lỗi tích hợp, không fallback step/role mặc định.
- Không có bước chờ cho nhân viên: HTTP `409` kèm step đang chờ nếu biết.
- HIS từ chối quyền: HTTP `403`.
- Nhiều detail khớp: fail closed, không chọn phần tử đầu.
- Đã Signed nhưng không lấy được file signed: lỗi, không trả PDF unsigned.

## 9. Acceptance criteria

- Luồng chính không gọi API ký section.
- Workflow 11 bước được lưu đủ 11 dòng sau submit đầu tiên.
- Bước 1 lưu đúng `SignedAt` và `SignedByEmployeeID`.
- Người không thuộc bước hiện tại không ký được; người ở bước sau ký được sau khi
  bước trước hoàn tất.
- Retry không tạo SWTID/step trùng.
- Eligibility hiển thị đúng step/status.
- Preview chỉ trả PDF signed sau khi HIS xác nhận hoàn tất.
- Migration chạy được trên DB hiện tại và test liên quan pass.
