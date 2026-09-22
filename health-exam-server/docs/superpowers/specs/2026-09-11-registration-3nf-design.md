# Chuẩn hoá 3NF phân hệ đăng ký hồ sơ Health Exam

**Ngày:** 2026-09-11  
**Phạm vi:** phân hệ đăng ký hồ sơ của `health-exam-server`  
**Trạng thái:** design đã được chốt trong brainstorming

## 1. Mục tiêu

Chuẩn hoá phần dữ liệu đăng ký đang tập trung quá nhiều nhóm thông tin trong
`HEX_ExamRecord`, đồng thời giữ tương thích với API hiện tại và dữ liệu đang có.

Mục tiêu cụ thể:

- `health-exam-server` là source of truth của Patient.
- Một Patient được tái sử dụng qua nhiều đợt khám.
- HIS là hệ thống downstream: nhận yêu cầu tạo/đồng bộ Patient và tạo Admission.
- `AdmissionID` thuộc về một registration, không thuộc về Patient.
- BHYT, nghề nghiệp/đơn vị công tác và thân nhân có thể thay đổi giữa các lần đăng ký.
- Registration cũ vẫn giữ đúng bản ghi BHYT, nghề nghiệp và thân nhân đã dùng.
- API hiện tại, tên field và shape response tiếp tục hoạt động trong giai đoạn chuyển đổi.
- Không tự động merge Patient chỉ vì trùng CCCD hoặc BHYT khi chưa có `PatientID` HIS.

## 2. Ngoài phạm vi

- Không chuẩn hoá lại toàn bộ order/RIS, webhook inbox hoặc outbox.
- Không thay đổi form-server.
- Không đổi public route của API đăng ký.
- Không dùng distributed transaction giữa health-exam và HIS.
- Không triển khai màn hình quản trị master-data trong phạm vi này.

## 3. Nguyên tắc dữ liệu

### 3.1 Khoá Patient

`PatientRefID` là UUID nội bộ của health-exam và là khoá chính của Patient.
`HisPatientID` là mã tham chiếu downstream, không dùng làm primary key.

Trong phạm vi tenant hiện tại, Patient HIS được nhận diện duy nhất theo:

```text
(DivisionID, HisPatientID)
```

Chỉ áp dụng unique khi `HisPatientID` có giá trị dương. Patient chưa đồng bộ HIS
được phép tồn tại với `HisPatientID = null` và không được tự merge bằng heuristic.

### 3.2 Ba loại dữ liệu

- **Patient canonical:** dữ liệu người bệnh hiện tại, do health-exam sở hữu.
- **Registration facts:** dữ liệu chỉ thuộc một lần đăng ký, do `HEX_ExamRecord`
  hoặc bảng con của registration sở hữu.
- **Versioned patient facts:** BHYT, nghề nghiệp/đơn vị công tác và thân nhân;
  các bản ghi đã được registration tham chiếu không bị sửa tại chỗ. Thay đổi tạo
  bản ghi mới và registration mới trỏ tới bản ghi mới.

Không tạo bảng snapshot riêng. Việc giữ lịch sử được thực hiện bằng registration
giữ FK tới các bản ghi versioned cụ thể và bằng `HEX_AuditLog` cho lịch sử thay đổi.

### 3.3 Master-data

`HEX_MasterDataOption` vẫn là bảng danh mục dùng chung theo tenant. Các danh mục
DB-backed lưu FK tới `OptionID`; các danh mục tĩnh đang nằm trong code
(`Gender`, `BloodAbo`, `BloodRh`, `Relationship`) tiếp tục lưu code/giá trị đóng.
Application layer tiếp tục nhận/trả code để giữ API cũ. Tên option không được
nhận từ client.

Các field registration liên quan:

- `PatientTypeOptionID` — category `PATIENT_TYPE`;
- `PaymentSourceOptionID` — category `PAYMENT_SOURCE`;
- `ExamLocationOptionID` — category `EXAM_LOCATION`.

`ExamLocation` là field hiện tại tương ứng với “Nơi đăng ký/Địa điểm khám”.
`ExamPlace` của `HEX_ExamSession` là địa điểm của cả đợt, không thay thế field này.

## 4. Mô hình mục tiêu

### 4.1 `HEX_Patient`

Nguồn sự thật nội bộ của người bệnh.

Các field chính:

- `PatientRefID` — UUID PK;
- `DivisionID`;
- `HisPatientID` — nullable, unique theo tenant khi có;
- `PatientCode`;
- `FullName`, `Dob`, `BirthYear`, `GenderID`;
- `IdentityNumber`, `IdentityIssuedDate`, `IdentityIssuerOptionID`;
- `PhoneNumber`, `Email`, `Address`;
- `EthnicityOptionID`;
- `BloodAboCode`, `BloodRhCode` — danh mục tĩnh, không có FK DB;
- audit fields và trạng thái đồng bộ HIS.

Các trường này không còn là nguồn ghi chính trên `HEX_ExamRecord`. API cũ vẫn
trả chúng bằng join qua `PatientRefID`.

### 4.2 `HEX_PatientInsurance`

Thông tin BHYT/đối tượng thanh toán theo từng phiên bản của Patient:

- `InsuranceRefID` — UUID PK;
- `PatientRefID` — FK tới Patient;
- `InsuranceNumber`;
- `InsuranceObjectOptionID`;
- `ValidFrom`, `ValidTo`;
- `IsActive` và audit fields.

Khi một registration đã tham chiếu bản ghi này, không update bản ghi tại chỗ để
đổi số thẻ hoặc thời hạn; tạo bản ghi mới rồi dùng cho registration tiếp theo.

### 4.3 `HEX_PatientEmployment`

Thông tin nghề nghiệp/đơn vị công tác theo phiên bản:

- `EmploymentRefID` — UUID PK;
- `PatientRefID` — FK tới Patient;
- `OccupationOptionID`;
- `StaffCode`, `OrgDeptName`, `JobTitle`;
- `IsActive` và audit fields.

### 4.4 `HEX_PatientRelative`

Danh sách thân nhân của Patient:

- `RelativeRefID` — UUID PK;
- `PatientRefID` — FK tới Patient;
- `RelationshipOptionID`;
- `FullName`, `IdentityNumber`, `PhoneNumber`;
- `IsActive` và audit fields.

Trong phase hiện tại registration chọn tối đa một thân nhân; schema dùng FK
`RelativeRefID` ở registration để giữ tương thích với form hiện tại.

### 4.5 `HEX_ExamRecord`

Giữ vai trò registration header và tiếp tục dùng `RecordID` làm định danh API.

Giữ lại:

- `RecordID`, `DivisionID`, `SessionID`, `RecordCode`;
- `PatientRefID`;
- `EmploymentRefID`, `InsuranceRefID`, `RelativeRefID`;
- `VariantCode`, `PackageID`;
- `ExamReason`;
- `PatientTypeOptionID`, `PaymentSourceOptionID`, `PaymentSourceOther`,
  `ExamLocationOptionID`;
- `AdmissionID` và trạng thái liên kết HIS;
- trạng thái registration, các mốc thời gian, cancellation và audit nghiệp vụ;
- liên kết form-server/HIS, `Note` và các cache tích hợp hiện có.

Loại bỏ khỏi nguồn ghi chính của `HEX_ExamRecord`:

- toàn bộ thông tin Patient;
- `InsuranceObjectCode/Name`, `InsuranceNumber`, thời hạn BHYT;
- `OccupationCode/Name`, `StaffCode`, `OrgDeptName`, `JobTitle`;
- thông tin thân nhân;
- các cặp `...Name` của master-data.

Các tên `PatientTypeName`, `ExamLocationName` và field tương tự trong API được
dựng từ option hiện tại hoặc dữ liệu tương thích trong giai đoạn chuyển tiếp.

## 5. Luồng nghiệp vụ

### 5.1 Tạo Patient và Admission

Không gọi HIS trong cùng transaction DB với health-exam.

1. Tạo hoặc cập nhật Patient trong health-exam.
2. Tạo `HEX_ExamRecord` và các liên kết versioned facts trong transaction local.
3. Ghi trạng thái đồng bộ Patient/registration là `Pending`.
4. Gọi HIS tạo/đồng bộ Patient.
5. Lưu `HisPatientID` khi HIS trả thành công.
6. Gọi HIS tạo Admission cho registration.
7. Lưu `AdmissionID` trên registration.

Nếu bước HIS lỗi, Patient local và registration không bị mất; trạng thái là
`Pending`/`Failed` theo contract hiện có và worker hoặc thao tác retry xử lý lại.
Không gửi request nghiệp vụ cần AdmissionID trước khi registration có AdmissionID.

### 5.2 Cập nhật Patient

Thông tin canonical của Patient cập nhật tại health-exam và có thể được đồng bộ
sang HIS. Cập nhật BHYT, nghề nghiệp hoặc thân nhân tạo version mới; không sửa
bản ghi đang được registration cũ tham chiếu.

### 5.3 API tương thích

Các endpoint hiện tại vẫn giữ nguyên. Application service thực hiện mapping:

- API `PatientID` ← `Patient.HisPatientID`, giá trị 0/null nếu chưa sync;
- API `AdmissionID` ← `ExamRecord.AdmissionID`;
- API `PatientTypeCode/Name` ← `PatientTypeOption`;
- API `ExamLocationCode/Name` ← `ExamLocationOption`;
- các field người bệnh ← `Patient`;
- BHYT/nghề nghiệp/thân nhân ← bản ghi được FK từ `ExamRecord`.

Contract serialization hiện tại là PascalCase và không đổi trong scope này.

## 6. Migration và backfill

### 6.1 Migration additive

Migration đầu tiên chỉ tạo bảng mới, index, FK và các cột liên kết trên
`HEX_ExamRecord`. Không drop cột cũ trong migration này.

Các FK phải cùng tenant với registration; tenant isolation tiếp tục được kiểm
tra ở application layer và bằng index/constraint phù hợp.

### 6.2 Backfill Patient

- Gom các record có cùng `(DivisionID, PatientID > 0)` thành một Patient.
- `PatientID = 0` tạo Patient local riêng cho từng record.
- Chép thông tin canonical từ bản ghi đại diện xác định theo `ModifiedDate` mới nhất.
- Ghi mapping `ExamRecord.PatientRefID` cho mọi record.
- Xuất báo cáo các nhóm có xung đột tên/CCCD/BHYT để kiểm tra, không tự merge.

### 6.3 Backfill bảng con

- Tạo `PatientInsurance` từ các tổ hợp giá trị BHYT khác nhau của từng Patient.
- Tạo `PatientEmployment` từ các tổ hợp nghề nghiệp/đơn vị công tác khác nhau.
- Tạo `PatientRelative` từ các tổ hợp thân nhân khác nhau.
- Gắn các FK tương ứng trên registration theo đúng dữ liệu cũ của từng record.
- Các row được registration tham chiếu được xem là immutable; chỉnh sửa về sau
  tạo row mới.

### 6.4 Backfill master-data

Resolve theo `(DivisionID, Category, Code)` và chỉ nhận option active. Code không
tồn tại/inactive không được tự thay thế; ghi vào migration error report và giữ
đường fallback legacy cho tới khi xử lý xong.

### 6.5 Tính chất idempotent

Backfill chạy lại không tạo Patient hoặc child row trùng. Mỗi bước có key xác
định và lưu mapping/marker để retry sau lỗi giữa chừng.

## 7. Rollout và rollback

### Release A — schema và backfill

- Deploy migration additive.
- Backfill trong chế độ không ghi hoặc transaction theo batch.
- Chạy orphan/count/conflict checks.
- Chưa đổi nguồn đọc/ghi API.

### Release B — normalized write/read

- Tất cả đường create/update/import ghi mô hình mới.
- API đọc ưu tiên mô hình mới, fallback legacy cho record chưa backfill.
- Legacy columns được ghi shadow tạm thời để consumer cũ không hỏng.
- Chạy đối chiếu normalized projection với legacy columns.

### Release C — dọn legacy

Chỉ thực hiện sau một chu kỳ release có đối chiếu không còn lệch:

- ngừng ghi shadow columns;
- chuyển toàn bộ query nội bộ sang join/mapping mới;
- drop các cột người bệnh/master-data dư thừa bằng migration riêng;
- giữ API facade không đổi.

Rollback trước Release C là tắt normalized read/write và quay lại legacy columns.
Rollback sau Release C cần restore migration/database backup và release facade
tương ứng; không dùng `git reset` để rollback dữ liệu.

## 8. Kiểm thử chấp nhận

### Patient và reuse

- Hai registration cùng `HisPatientID` và tenant dùng cùng `PatientRefID`.
- Hai tenant khác nhau không dùng chung Patient.
- Patient chưa có HIS ID không bị merge tự động theo CCCD/BHYT.
- Retry đồng bộ HIS không tạo Patient trùng.

### Versioned facts

- Registration cũ giữ nguyên BHYT/nghề nghiệp/thân nhân cũ.
- Registration mới dùng version mới.
- Update không sửa row child đang được registration khác tham chiếu.

### Master-data và API

- `PatientTypes` và `ExamLocations` trả đúng option active của tenant.
- Create/update nhận code, lưu FK, trả lại code/name đúng API cũ.
- Code invalid/inactive trả lỗi field-specific.
- Tên option đổi không làm hỏng FK hoặc record đã lưu.

### Migration

- Backfill chạy lại không tạo trùng.
- Không có registration mồ côi sau backfill.
- Count trước/sau đối chiếu được.
- Conflict report đầy đủ và không có heuristic merge ngoài quy định.

### HIS

- HIS tạo Patient thành công thì lưu `HisPatientID`.
- HIS lỗi không làm mất Patient/registration local.
- Chưa có `AdmissionID` thì không gửi request downstream yêu cầu admission.
- Retry thành công không tạo admission trùng.

## 9. Tiêu chí hoàn thành design

Thiết kế được xem là sẵn sàng cho implementation khi:

- schema mục tiêu và ownership field được duyệt;
- migration/backfill có idempotency và conflict report;
- API compatibility mapping được test;
- cơ chế HIS retry không phụ thuộc distributed transaction;
- có điều kiện rõ ràng để xoá legacy columns.
