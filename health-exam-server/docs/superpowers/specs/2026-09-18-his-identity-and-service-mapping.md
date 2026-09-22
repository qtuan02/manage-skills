# HIS Identity & Service Mapping Specification for Health Exam Paraclinical Module

## 1. Identity Resolution Matrix

Để gửi chỉ định cận lâm sàng sang HIS (`POST /api/M02F00710/ClinicalRequest`), health-exam-server cần giải quyết 5 định danh chuẩn từ HIS:

| HIS Parameter | Ý nghĩa | Nguồn dữ liệu trong HealthExam | Cách lấy / Fallback |
|---|---|---|---|
| `PtID` / `PtCode` | ID / Mã người bệnh trên HIS | `ExamRecord.Patient.HisPatientID`, `ExamRecord.Patient.PatientCode` | Lấy từ `Patient` liên kết với `ExamRecord`. Nếu chưa có `HisPatientID > 0`, tạo hoặc đồng bộ từ HIS qua `IEnsureHisAdmission` / `HisEmrClient.CreatePatientAsync`. |
| `AdmID` / `AdmCode` | ID / Mã lượt tiếp nhận trên HIS | `ExamRecord.AdmissionID`, `HEX-{ExamRecord.RecordID:N}` | Lấy từ `ExamRecord.AdmissionID`. Nếu chưa có, kích hoạt `IEnsureHisAdmission` để tạo Admission trên HIS (gắn với Khoa KSK `ExamSession.DepartmentID` hoặc config fallback). |
| `TPID` | Treatment Process ID trên HIS | `ExamRecord.HisTreatmentProcessID` hoặc `ExamRecord.SignSteps[].HisTreatmentProcessID` / `M02_MedicalProcess.ID` | Lấy từ hồ sơ / đợt khám đã liên kết quy trình điều trị / hồ sơ bệnh án HIS `M02_MedicalProcess`. Nếu chưa có, tạo/lấy qua `CMedicalProcess` hoặc cấu hình mặc định. |
| `PCReqDoctorID` | Bác sĩ chỉ định trên HIS | `ParaclinicalOrder.OrderedByID` / `HealthExamContext.ActorId` | Bác sĩ đăng nhập tạo/chốt chỉ định. Phải là số nguyên dương hợp lệ tương ứng với `EmpID` trong HIS. |
| `ReqDeptID` | Khoa phòng chỉ định trên HIS | `ExamSession.DepartmentID` / `ParaclinicalOrder.RoomID` / fallback options | Khoa/phòng của đợt khám (`ExamSession.DepartmentID`). Nếu <= 0, fallback theo cấu hình khoa khám sức khỏe (`IHisCredentialOptions.KskDepartmentId`). |

## 2. Local Service → HIS MedSerID Mapping

Mỗi dòng chỉ định `ParaclinicalOrderItem` chứa:
- `ServiceID`: Mã định danh dịch vụ kỹ thuật. Khi tạo từ gói khám hoặc danh mục, `ServiceID` tương ứng chính là `HisMedSerId` (HIS Medical Service ID).
- `ServiceCode`: Mã dịch vụ (`LocalServiceCode`).
- `ServiceName`: Tên dịch vụ (`HisMedSerName`).
- Bảng ánh xạ:
  - Nếu `ServiceID > 0`: sử dụng trực tiếp làm `HisMedSerID`.
  - Nếu `ServiceID <= 0`: tra cứu qua bảng ánh xạ cấu hình `LocalServiceCode -> HisMedSerID`. Nếu không tìm thấy, từ chối submit.

## 3. Submit Precondition Validation Rule

Trước khi đưa lệnh `SubmitParaclinicalOrder` vào outbox hoặc gửi sang HIS:
1. **Kiểm tra trạng thái Order**: Phải là `Draft`. Nếu đã `Submitted`, `Ordered`, `InProgress`, `Completed` hoặc `Cancelled` -> Reject.
2. **Kiểm tra danh sách Items**: Phải có ít nhất 1 dòng dịch vụ và không chứa dịch vụ trùng lặp.
3. **Kiểm tra HIS Identity**:
   - `PtID > 0` và `PtCode` không rỗng.
   - `AdmID > 0`.
   - `TPID > 0`.
   - `PCReqDoctorID > 0`.
   - `ReqDeptID > 0`.
4. **Kiểm tra Service Mapping**:
   - Mọi dòng trong `Items` đều phải có `HisMedSerID > 0`.
5. **Xử lý khi thiếu thông tin**:
   - Trả về lỗi validation rõ ràng: `HisIdentityMissing` / `ServiceMappingMissing` kèm danh sách cụ thể các trường hoặc mã dịch vụ bị thiếu, không enqueue vào outbox.
