# M02_MedicalProcess trong HIS

## 1. `M02_MedicalProcess` là gì?

`M02_MedicalProcess` là một tài liệu hoặc hạng mục hồ sơ bệnh án cụ thể của một lượt tiếp nhận trên HIS.

Nó không phải:

- Patient.
- Admission.
- Cấu hình quy trình ký `WFM_SignWorkflow`.

Có thể hiểu đơn giản:

```text
Patient
  └── Admission
        ├── MedicalProcess: Phiếu khám sức khỏe
        ├── MedicalProcess: Phiếu xét nghiệm
        └── MedicalProcess: Phiếu tổng kết
```

Mỗi `M02_MedicalProcess` xác định:

| Field | Ý nghĩa |
|---|---|
| `ID` | ID quy trình/tài liệu bệnh án; chính là `Id` truyền vào `SubmitEMR`, `SignEMR` |
| `AdmissionID` | Lượt tiếp nhận sở hữu tài liệu |
| `MedicalTypeCode` | Loại hồ sơ bệnh án |
| `MedicalTypeName` | Tên loại hồ sơ bệnh án |
| `FileDocTypeID` | Loại tài liệu cụ thể |
| `FileDocTypeGroup` | Nhóm tài liệu |
| `TemplateID` | Biểu mẫu EMR dùng để nhập dữ liệu |
| `EMRDataID` | Dữ liệu biểu mẫu đã lưu |
| `SWTID` | Giao dịch ký đang chạy |
| `FileDocID` | File PDF/tài liệu được ký |
| `SignStatus` | Trạng thái ký |
| `FilePath` | Đường dẫn file sau khi sinh/ký |
| `VoucherDate` | Ngày chứng từ |

Entity nằm tại `HIS.Core/EntityFramework/Entity/M02/M02_MedicalProcess.cs` trong his-server.

## 2. Quan hệ với Patient và Admission

```text
IAM_Patient
    1
    │ PatientID
    ▼
M02_Admission
    1
    │ AdmissionID
    ▼
M02_MedicalProcess
    1
    │ EMRDataID
    ▼
M03_EMRData
    │
    └── M03_EMRDataDetail

M02_MedicalProcess
    │ SWTID
    ▼
M02_SignWorkflowTransaction
    │
    └── M02_SignWorkflowTransactionDetail
```

### Patient

`Patient` là hồ sơ nhân khẩu lâu dài của người bệnh:

```text
PatientID, PatientCode, họ tên, ngày sinh...
```

Một Patient có thể có nhiều lần khám.

### Admission

`Admission` là một lượt tiếp nhận hoặc một lần đến khám:

```text
AdmissionID, PatientID, AdmissionCode, ngày tiếp nhận, khoa...
```

Một Patient có thể có nhiều Admission:

```text
Patient 123
├── Admission 1001 — khám ngày 01/09
└── Admission 1057 — khám ngày 16/09
```

### MedicalProcess

Một Admission có thể sinh nhiều `M02_MedicalProcess`, mỗi bản ghi đại diện cho một loại tài liệu cần lập trong lượt khám đó.

`M02_MedicalProcess` không giữ `PatientID` trực tiếp. Muốn xác định người bệnh:

```text
MedicalProcess.AdmissionID
→ Admission.PatientID
→ Patient
```

## 3. `M02_MedicalProcess` được dùng ở đâu?

### 3.1. Liệt kê tài liệu của một lượt khám

```http
GET api/M02F01500/RMedicalProcess?admissionID={AdmissionID}
```

HIS đọc các `M02_MedicalProcess` thuộc Admission.

### 3.2. Lấy chi tiết phần khám và trạng thái ký

```http
GET api/M02F01500/RMedicalProcessByID?id={MedicalProcessID}
```

API trả:

- Các phần khám `ItemGroupID`.
- Bước ký hiện tại.
- Vai trò được ký.
- Người đã ký.
- Trạng thái từng bước.

### 3.3. Liên kết dữ liệu biểu mẫu

Khi lưu form EMR, HIS tạo `M03_EMRData` rồi gắn:

```text
M02_MedicalProcess.EMRDataID = M03_EMRData.EMRDataID
```

### 3.4. Trình ký và ký

```http
POST api/M02F01500/SubmitEMR
POST api/M02F01500/SignEMR
POST api/M02F01500/CancleEMR
```

Các API này nhận `Id = M02_MedicalProcess.ID`.

`WFM_SignWorkflow.ID` không được truyền làm `Id` cho `SignEMR`.

## 4. Cách tạo `M02_MedicalProcess`

Gọi API:

```http
POST api/M02F01500/CMedicalProcess
Authorization: Bearer {his-token}
Content-Type: application/json
```

Body:

```json
{
  "MedicalTypeCode": "MA_LOAI_HO_SO_KSK",
  "MedicalTypeCodeOld": null,
  "AdmissionID": 123456
}
```

HIS thực hiện:

1. Tìm `IAM_FileMedicalType` theo `MedicalTypeCode`.
2. Lấy các `IAM_FileDocType` thuộc loại hồ sơ đó.
3. Tìm template tương ứng trong `M03_Template`.
4. Tạo một `M02_MedicalProcess` cho mỗi `FileDocType`.
5. Tạo các `M02_MedicalProcessDetail` tương ứng với nhóm khám.
6. Gắn tất cả vào `AdmissionID`.

Mã tạo nằm tại `HIS.Server/Features/Command/M02/M02F01500Commands.cs` trong his-server.

### Thay đổi loại hồ sơ bệnh án

Nếu gọi lại với `MedicalTypeCode` khác:

- HIS kiểm tra các process hiện tại.
- Nếu đã trình ký thì không cho thay đổi.
- Nếu chưa trình ký, HIS xóa process, detail và dữ liệu EMR cũ rồi tạo lại theo loại mới.

Nếu gọi lại với cùng `MedicalTypeCode`, handler hiện có thể trả `Data: []` dù dữ liệu cũ vẫn còn trong database. Sau lệnh tạo nên gọi lại `RMedicalProcess?admissionID=...` để lấy danh sách chuẩn.

## 5. Quan hệ với cấu hình quy trình ký

Ba khái niệm khác nhau:

| Thành phần | Vai trò |
|---|---|
| `WFM_SignWorkflow` | Cấu hình mẫu: gồm những bước nào, vai trò nào được ký |
| `M02_MedicalProcess` | Tài liệu thực tế của một Admission |
| `M02_SignWorkflowTransaction` | Phiên thực thi quy trình ký cho tài liệu đó |

Luồng:

```text
WFM_SignWorkflow
       │ cấu hình cho FileDocType
       ▼
M02_MedicalProcess
       │ SubmitEMR
       ▼
M02_SignWorkflowTransaction
       │ SignEMR từng SWStep
       ▼
Hoàn tất ký
```

`GetSignWorkflowByID` chỉ đọc cấu hình quy trình ký. Nó không tạo `M02_MedicalProcess` và không tạo phiên ký.

### API cấu hình quy trình ký

Danh sách quy trình ký:

```http
GET api/WFMF00030/GetListSignWorkflow
```

Chi tiết một quy trình và các bước ký:

```http
GET api/WFMF00030/GetSignWorkflowByID?id={workflowId}
```

## 6. Luồng health-exam-server cần thực hiện

```text
1. Tạo hoặc tìm Patient trên HIS
2. Tạo hoặc tìm Admission trên HIS
3. Lưu PatientID và AdmissionID vào health_exam_record
4. Xác định MedicalTypeCode dành cho KSK
5. Gọi CMedicalProcess
6. Gọi RMedicalProcess theo AdmissionID
7. Chọn MedicalProcess có TemplateID/FileDocTypeID của phiếu KSK
8. Lưu dữ liệu EMR
9. Gọi SubmitEMR để trình ký
10. Gọi SignEMR cho từng SWStep
```

Khoảng trống hiện tại của health-exam-server là bước 4–5: hệ thống đã có `PatientID` và `AdmissionID`, nhưng chưa có mapping `VariantCode → MedicalTypeCode` và chưa gọi `CMedicalProcess`.

## 7. Tóm tắt ID cần phân biệt

| ID | Thuộc bảng | Công dụng |
|---|---|---|
| `PatientID` | `IAM_Patient` | Định danh người bệnh |
| `AdmissionID` | `M02_Admission` | Định danh một lượt tiếp nhận |
| `M02_MedicalProcess.ID` | `M02_MedicalProcess` | Định danh tài liệu thực tế; truyền vào `SubmitEMR`, `SignEMR` |
| `TemplateID` | `M03_Template` | Định danh mẫu biểu nhập liệu |
| `EMRDataID` | `M03_EMRData` | Định danh dữ liệu đã nhập |
| `WFM_SignWorkflow.ID` | `WFM_SignWorkflow` | Định danh cấu hình quy trình ký |
| `SWTID` | `M02_SignWorkflowTransaction` | Định danh phiên thực thi quy trình ký |
