# Health Exam Paraclinical Module Design

## Goal

`health-exam-server` quản lý toàn bộ vòng đời CLS ở lớp nghiệp vụ local; HIS giữ định danh chuẩn, kiểm tra nghiệp vụ HIS và làm gateway kết nối PACS.

## Decisions

- Health-exam tạo và quản lý `ParaclinicalOrder` local.
- `PtID/PtCode`, `AdmID/AdmCode`, `TPID`, `PCReqDoctorID`, `ReqDeptID` và `MedSerID` là định danh từ HIS.
- Khi tạo hồ sơ mới, health-exam tìm hoặc tạo dữ liệu tương ứng trên HIS qua API được cấp; nếu HIS chưa có API tạo thì người dùng phải tạo trước trên HIS.
- Bản đầu đồng bộ kết quả bằng polling; webhook là mở rộng sau.
- Không ghi trực tiếp HIS database và không gọi trực tiếp PACS/gRPC.
- Mọi mutation tới HIS đi qua outbox, có retry và idempotency.

## Flow

```text
ExamRecord
  → local ParaclinicalOrder (Draft)
  → map HIS patient/encounter/treatment/service
  → POST /api/M02F00710/ClinicalRequest
  → GET /api/M02F40000/CareProcessConnectTP
  → HIS tạo ParaClinProcess và gọi PACS
  → polling kết quả HIS
  → lưu ParaclinicalResult local
```

## Local data

### ParaclinicalOrder

```text
OrderId, ExamRecordId, HisPtId, HisPtCode,
HisAdmissionId, HisAdmissionCode, HisTreatmentProcessId,
HisParaClinReqId, Status, Note, CreatedAt, SubmittedAt,
CancelledAt, LastSyncAt, LastSyncError, Version
```

### ParaclinicalOrderItem

```text
OrderItemId, OrderId, LocalServiceCode, HisMedSerId,
HisMedSerName, ServiceKind, Quantity, Priority,
ScheduledFrom, ScheduledTo, HisParaClinReqDtlId,
HisParaClinProcessId, Status, ResultRequired
```

### Result

```text
ParaclinicalResult:
  ResultId, OrderId, HisResultId, ResultDate, Status, ImportedAt

ParaclinicalResultItem:
  ResultItemId, ResultId, OrderItemId, HisDetailId,
  Value, Text, Unit, ReferenceRange, AbnormalFlag
```

## State machine

```text
Draft → Submitted → Ordered → InProgress → Completed
Draft/Submitted/Ordered/InProgress → Cancelled
```

`Completed` và `Cancelled` là trạng thái kết thúc. Lỗi network là retryable; lỗi validation HIS không retry tự động.

## Health-exam API

```http
POST /api/exam-records/{recordId}/paraclinical-orders
GET  /api/exam-records/{recordId}/paraclinical-orders
GET  /api/paraclinical-orders/{orderId}
POST /api/paraclinical-orders/{orderId}/submit
POST /api/paraclinical-orders/{orderId}/cancel
GET  /api/paraclinical-orders/{orderId}/results
GET  /api/paraclinical-results/{resultId}
```

Tạo order chỉ tạo `Draft`; submit enqueue command và không chờ PACS trong HTTP request.

## HIS API

```text
GET    /api/M06F00000/GetAdmissionInfo
GET    /api/M02F00710/GetListMedSerType
GET    /api/M02F00710/GetListMedicalServiceItem
POST   /api/M02F00710/ClinicalRequest
GET    /api/M02F00710/ClinicalRequest
GET    /api/M02F00710/ClinicalRequest/{admID}
DELETE /api/M02F00710/ClinicalRequest
GET    /api/M02F40000/CareProcessConnectTP
GET    /api/M02F40000/CareProcessConnectCancelTP
GET    /api/M07F90000/RParaClinicalByAdmID
GET    /api/M07F90000/RParaClinicalResultByAdmID
GET    /api/M07F90000/RParaClinicalByID
```

## Integration rules

```text
HIS:ClinicalRequest:{OrderId}
HIS:ClinicalConnect:{OrderId}
HIS:ClinicalCancel:{OrderId}
HIS:ClinicalResult:{HisResultId}
```

- Retry timeout, connection reset và HTTP 5xx.
- Không retry lỗi dữ liệu 4xx.
- Nếu timeout sau khi tạo request, reconcile trước khi tạo lại.
- Upsert kết quả theo `HisResultId`.
- Reconciliation phát hiện order local thiếu HIS ID, trạng thái lệch và kết quả HIS chưa import.

## Acceptance criteria

- Tạo được order local ở `Draft`.
- Submit tạo đúng một request HIS.
- Connect tạo process và không duplicate khi retry.
- Polling import được kết quả và detail.
- Hủy được order chưa hoàn tất.
- Sai `TPID`, `MedSerID`, doctor hoặc department trả lỗi rõ ràng.
- Không có direct database/PACS integration.
