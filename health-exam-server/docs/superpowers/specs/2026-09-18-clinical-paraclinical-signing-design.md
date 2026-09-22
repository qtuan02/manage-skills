# Clinical and Paraclinical Signing Module Design

## Goal

`health-exam-server` quản lý hồ sơ khám, tài liệu khám lâm sàng và phiếu CLS; khi cần ký, hệ thống gọi `his-server` để submit/ký tài liệu. HIS vẫn là nguồn định danh, signing authority và gateway sang PACS.

## Scope

### In scope

- Một signing module dùng chung cho `ClinicalExam` và `ParaclinicalOrder`.
- Render PDF local cho từng loại tài liệu.
- Submit, ký, theo dõi trạng thái và lấy signed file qua HIS.
- Tạo/chuyển phiếu CLS qua HIS sau khi ký thành công.
- Đồng bộ kết quả CLS bằng polling ở release đầu.
- Outbox, retry, idempotency, audit và reconciliation.

### Out of scope

- Lưu private key hoặc tự ký PDF tại health-exam.
- Gọi trực tiếp PACS/gRPC.
- Ghi trực tiếp HIS database.
- Tự sinh `PtID`, `AdmID`, `AdmCode`, `TPID`, `MedSerID`.

## Source of truth and mapping

```text
HealthExam PatientId       ↔ HIS PtID / PtCode
HealthExam ExamRecordId    ↔ HIS AdmID / AdmCode / TPID
HealthExam DoctorId        ↔ HIS EmployeeID / AccountID
HealthExam DepartmentId    ↔ HIS DepartmentID
Local ServiceCode          ↔ HIS MedSerID
SigningDocumentId          ↔ HIS SWTID / FileDocID
ParaclinicalOrderId        ↔ HIS ParaClinReqID
OrderItemId                ↔ HIS ParaClinReqDtlID
Local result               ↔ HIS ParaClinicalResultID
```

`PtID` được dùng lại cho nhiều lần khám; `AdmID` là một đợt tiếp nhận; `TPID` là tờ điều trị thuộc đợt đó.

## Architecture

```text
ExamRecord / ParaclinicalOrder
  ↓
ClinicalExamPdfRenderer / ParaclinicalPdfRenderer
  ↓
SigningDocument
  ↓ outbox
HIS Signing Client
  ↓ SubmitFile / SignFiles / status
HIS signed artifact
  ↓
Clinical workflow complete

Paraclinical only:
Signed
  → HIS ClinicalRequest
  → HIS CareProcessConnectTP
  → PACS
  → polling HIS results
```

## Shared signing model

### SigningDocument

```text
SigningDocumentId
DocumentType          // ClinicalExam | ParaclinicalOrder
BusinessId
ExamRecordId?
ParaclinicalOrderId?
PdfFileId
FileHash
FileName
FileDocTypeId
Status
CreatedAt
SignedAt
```

### SigningTransaction

```text
SigningTransactionId
SigningDocumentId
HisSwtId
HisFileDocId
HisFileId
HisCurrentStep
HisSignStatus
CurrentSignerId
WorkflowSnapshot
LastSyncAt
LastError
```

### Signer mapping

```text
HealthExamUserId
HisAccountId
HisEmployeeId
HisDepartmentId
HisRoleId
HisSigningGroupId
```

## State machines

### Signing

```text
Unsigned → Submitted → WaitingForSigner → Signing → Signed
                                      └→ Rejected
Unsigned/Submitted/WaitingForSigner/Signing → Cancelled
```

### Paraclinical

```text
Draft → Signed → SubmittedToHis → Ordered → InProgress → Completed
Draft/Signed/SubmittedToHis/Ordered/InProgress → Cancelled
```

CLS cannot connect to PACS until signing is `Signed`.

## Health-exam APIs

```http
POST /api/signing/clinical-exam/{examRecordId}/preview
POST /api/signing/clinical-exam/{examRecordId}/submit
GET  /api/signing/clinical-exam/{examRecordId}/status
POST /api/signing/clinical-exam/{examRecordId}/sign
POST /api/signing/clinical-exam/{examRecordId}/cancel
GET  /api/signing/clinical-exam/{examRecordId}/file

POST /api/paraclinical-orders/{orderId}/sign/preview
POST /api/paraclinical-orders/{orderId}/sign/submit
GET  /api/paraclinical-orders/{orderId}/sign/status
POST /api/paraclinical-orders/{orderId}/sign
POST /api/paraclinical-orders/{orderId}/sign/cancel
GET  /api/paraclinical-orders/{orderId}/signed-file
```

## HIS APIs

### Signing

```http
POST /api/M02F30000/SubmitFile
GET  /api/M02F30000/GetPermissionGroup
GET  /api/M02F30000/GetFileSign
POST /api/M02F30000/SignFiles
GET  /api/M02F30000/SignProcess
GET  /api/M02F30000/GetFileSignByFileID
POST /api/M02F30000/SignCancelFile
```

### CLS

```http
GET    /api/M06F00000/GetAdmissionInfo
GET    /api/M02F00710/GetListMedicalServiceItem
POST   /api/M02F00710/ClinicalRequest
GET    /api/M02F40000/CareProcessConnectTP
GET    /api/M02F00710/ClinicalRequest
DELETE /api/M02F00710/ClinicalRequest
GET    /api/M02F40000/CareProcessConnectCancelTP
GET    /api/M07F90000/RParaClinicalResultByAdmID
GET    /api/M07F90000/RParaClinicalByID
```

## Business rules

- Clinical and paraclinical PDFs use separate renderers but one signing workflow.
- `SubmitFile` timeout must be reconciled before retrying to prevent duplicate signing transactions.
- `SignFiles` is idempotent per `SwtId + step`.
- A signed CLS document must be completed before `ClinicalRequest` is sent.
- `ClinicalRequest` success stores `ParaClinReqID`; connect stores process linkage.
- HIS validation remains authoritative for patient, encounter, treatment, role, department and service.
- Signed files and signing transitions require audit records.

## Acceptance criteria

- One clinical document and one CLS document can use the same signing service.
- HIS returns and health-exam stores `SWTID`, `FileDocID`, step and status.
- User can continue a multi-step HIS signing workflow.
- A signed CLS order creates exactly one HIS clinical request and connects exactly once.
- Polling imports CLS result and details without duplicates.
- Invalid HIS identity, signer permission or service mapping produces a visible business error.
- No private key, direct HIS DB access or direct PACS access exists in health-exam.
