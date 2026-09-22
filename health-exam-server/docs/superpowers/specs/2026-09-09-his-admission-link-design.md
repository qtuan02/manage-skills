# HIS Admission Link Design

## Goal

Khi nhân viên tạo hồ sơ KSK thủ công, `health-exam-server` tạo hồ sơ cục bộ
trước, rồi tạo hoặc cập nhật lượt tiếp nhận ở HIS và lưu `AdmissionID` HIS vào
`HEX_ExamRecord`. Form KSK sau đó dùng chính ID này cho các thao tác quy trình,
ghi và ký trên HIS.

HIS hiện có một controller khai báo route không khớp tên method. Đây là route
đang chạy và là một phần của hợp đồng cần giữ nguyên:

```http
POST /api/M02F00000/GetAdmissionInfo
```

Nó thực thi method HIS `CUAdmission`; `Data` của response thành công là
`AdmissionID` kiểu số nguyên dương. Không sửa `his-server` trong thay đổi này.

## Architecture boundary

`IHisEmrApi` vẫn là cổng hạ tầng duy nhất biết HTTP HIS. Bổ sung một operation
cho wire contract:

```csharp
Task<JToken> CreateAdmissionAsync(
    HisAdmissionWireRequest request,
    CancellationToken ct = default);
```

`HisEmrHttpClient` triển khai operation bằng `POST` route hiện hữu bên trên,
truyền tiếp bearer token, `X-Trace-Id`, và `X-Division-Id` theo cùng cơ chế của
form HIS. Đây là thao tác ghi nên không tự retry ở tầng HTTP.

Nghiệp vụ nằm sau một interface mới thuộc `health-exam-server`:

```csharp
public interface IHisAdmissionService
{
    Task<ExamRecordItem> EnsureAdmissionAsync(
        Guid recordId,
        CancellationToken ct = default);
}
```

`HisAdmissionService` sở hữu việc nạp hồ sơ/đợt cùng tenant, dựng payload,
gọi transport, xác nhận `Data` là `long > 0`, và ghi `ExamRecord.AdmissionID`.
Controller và `ExamRecordService` không biết route HIS hoặc JSON response.

```text
POST /v1/exam-records
  -> ExamRecordService: persist local record
  -> IHisAdmissionService.EnsureAdmissionAsync
  -> IHisEmrApi.CreateAdmissionAsync
  -> HIS CUAdmission
  -> persist AdmissionID

POST /v1/exam-records/{recordId}/his-admission
  -> IHisAdmissionService.EnsureAdmissionAsync
```

## Public behavior

### Manual registration

`POST /v1/exam-records` remains the manual registration endpoint. It first
commits the local record. It then makes one best-effort admission-link call.
When an existing client supplies a valid `AdmissionID`, the current
backward-compatible behavior is preserved and the idempotent service returns
that existing link without a second HIS call. Ordinary manual registration
with no admission ID follows the new creation flow.

- HIS returns a valid positive ID: persist it and return the record with
  `AdmissionID` and `HisAdmissionLinkStatus = "Linked"`.
- HIS rejects the request, is unavailable, or times out: log a warning without
  tokens or payload PII; keep and return the created local record with
  `AdmissionID = null` and `HisAdmissionLinkStatus = "Pending"`.
- A request-cancellation from the caller remains cancellation, rather than
  being converted into a successful pending response.

This isolates local registration availability from a transient HIS outage.
The existing form write/sign gateway remains the enforcement point: it refuses
form operations while a valid `AdmissionID` is absent.

### Retry endpoint

Expose the authenticated endpoint:

```http
POST /v1/exam-records/{recordId}/his-admission
```

It returns the ordinary `ResultData<ExamRecordItem>` envelope. It is
idempotent:

- if the tenant-owned record already has a positive `AdmissionID`, return it
  immediately and make no HIS call;
- otherwise attempt the same link creation and return the saved record on
  success;
- invalid HIS response, missing required configuration, authorization failure,
  or HIS failure is returned through the existing health-exam exception
  envelope. The existing local record remains unchanged and can be retried.

The request continues to require the employee HIS bearer token, because the
transport forwards it to HIS. The retry endpoint has no request body and never
accepts an `AdmissionID` from the client. The existing create/update DTO's
valid positive `AdmissionID` field is deliberately retained for backward
compatibility; this change does not expand that pre-existing contract.

### Scope exclusions

`ExamImportService` and its `CreateAsync(..., importBatchId)` path do not call
HIS. Existing imports stay local and retain their current behavior.

The update endpoint does not create or replace HIS admissions. Direct
client-supplied `AdmissionID` is removed from the public write DTO so the HIS
link is owned by the admission service rather than bypassable by a client.

## HIS request mapping

The stable idempotency key is `AdmissionCode = "HEX-" + RecordID.ToString("N")`.
The HIS handler upserts its `M02_Admission` by this code, so a retry after an
uncertain network result does not create another admission.

| HIS field | Source |
| --- | --- |
| `AdmissionCode` | `HEX-{RecordID:N}` |
| `AdmissionDate` | `ExamSession.ExamDate` at midnight local calendar date |
| `DepartmentID` | `ExamSession.DepartmentID` when positive; otherwise `HIS_EMR_KSK_DEPARTMENT_ID` |
| `IsOutPatient` | `2` (khám bệnh / KSK) |
| `PatientCode` | persisted `ExamRecord.PatientCode` |
| `FullName` | persisted `ExamRecord.FullName` |
| `FirstName` | last non-empty whitespace-separated token of `FullName` |
| `LastName` | all preceding name tokens joined with one space; empty when one token |
| `I_Gender` | `ExamRecord.GenderID` |
| `BirthDate` | `ExamRecord.Dob` when present |
| `BirthYear` | `ExamRecord.BirthYear`, else `Dob.Year`, else `0` |
| `IDCard` | `ExamRecord.IdentityNumber` |
| `MobileNo` | `ExamRecord.PhoneNumber` |
| `PersonalEmail` | `ExamRecord.Email` |
| `CurrentAddress` | `ExamRecord.Address` |

The wire DTO contains only the fields used by this integration. JSON names
match HIS `AdmissionRequest` names exactly.

The phase-one default session currently has `DepartmentID = 0`. Add
`HIS_EMR_KSK_DEPARTMENT_ID`, parsed only as a positive integer. A retry with no
positive session department and no valid fallback fails deterministically;
automatic creation catches that failure and returns pending.

Manual creation must have a stable, nonempty `PatientCode` for HIS to create or
find its patient. When the client omits it, reuse the existing
`ComposeGeneratedPatientCode(RecordCode)` rule for manual creation as well as
imports. The generated `HEX-...` code is persisted before any HIS call and
therefore survives retries. If it would exceed the column maximum, creation
fails with validation rather than calling HIS with an empty patient code.

## Configuration and observability

Add to `.env.example` and README:

```dotenv
HIS_EMR_KSK_DEPARTMENT_ID=0
```

`0`, a missing value, a non-numeric value, or a negative value means no
fallback department is configured. Existing `HIS_EMR_ENABLED`,
`HIS_EMR_BASE_URL`, timeout, and credential-header configuration remain the
source of transport configuration.

Log the operation, record ID, admission ID only after success, division,
TraceID, downstream status, and elapsed time. Do not log bearer tokens,
identity card, phone, email, or the full admission payload.

## Error handling and consistency

- Local record persistence completes before the first HIS mutation.
- HIS mutation has no automatic retry; the explicit endpoint is the retry
  mechanism.
- A locally successful record with failed link stays valid but pending, so no
  registration data is lost.
- The stable `AdmissionCode` makes retries safe at HIS even after a response is
  lost between HIS commit and local `AdmissionID` save.
- `Data` must parse exactly as a positive integral value. `null`, a fraction,
  a string, zero, or a negative value is an integration failure and is never
  persisted.

## Testing

Tests are written first and cover:

- manual record creation without `PatientCode` generates and persists
  `HEX-{RecordCode}`; import behavior remains unchanged;
- `HisEmrHttpClient` sends the exact deployed POST route, expected headers,
  exact JSON field names, and makes exactly one request on connection failure;
- `HisAdmissionService` maps names and KSK fields, stores a valid HIS ID,
  skips HIS when already linked, and rejects invalid `Data` or missing
  department fallback;
- automatic manual registration returns a local pending record when HIS fails;
- the retry route's contract, tenant ownership, idempotency, and response link
  state;
- DI resolves `IHisAdmissionService`, and the full existing test suite remains
  green.

## Out of scope

- Any route, command, handler, database schema, or deployment change in
  `his-server`.
- Background retry scheduling, queues, or an outbox for admission creation.
- Bulk admission creation during Excel import.
- Synchronizing later edits/cancellations in a health-exam record back to HIS.
- Replacing the existing form/signing gateway admission validation.
