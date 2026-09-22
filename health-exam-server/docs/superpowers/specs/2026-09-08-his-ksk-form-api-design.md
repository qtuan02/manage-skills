# HIS KSK Form API Design

## Goal

Expose the HIS health-examination form `KSK-TREN18TUOI` through
`health-exam-server` without using `form-server`. HIS remains the source of truth
for the form definition, medical-process state, signing workflow, and section
signatures.

Phase 1 is read-only for form definitions and entered form values. The only
mutating operations are the existing HIS submit-sign, sign, and cancel-sign
operations for a section.

## Scope

### Included

- Read the active `KSK-TREN18TUOI` template from HIS M03.
- Read template metadata and its ordered `M03_TemplateDetail` rows.
- Read the generated layout used to render fields, choices, links, and formulas.
- Read the HIS medical process and signing status associated with an admission.
- Read the signing workflow configured for the template's document type.
- Submit a section for signing through HIS.
- Sign a section through HIS.
- Cancel a section signature through HIS.
- Add a nullable HIS `AdmissionID` reference to `HEX_ExamRecord` so a health-exam
  record can address the correct HIS encounter.
- Forward the authenticated user's HIS credential on every proxied request so
  HIS records and authorizes the real signer.
- Cache immutable form-definition reads for five minutes. Do not cache medical
  process or signing state.

### Excluded

- `form-server` integration.
- Creating, updating, deleting, importing, or rebuilding HIS form definitions.
- Creating or updating `M03_EMRData` values through `CUEMR`.
- Automatically creating an HIS admission.
- Reimplementing M02 signing rules in `health-exam-server`.
- Persisting a second copy of section signature state in the health-exam
  database.
- Batch signing.

## Source-of-truth boundaries

| Concern | Source of truth |
|---|---|
| Template metadata | HIS `M03_Template` |
| Ordered template roots | HIS `M03_TemplateDetail` |
| Renderable layout | HIS `M03_TemplateGenerate` plus M03 item tables |
| Admission | HIS `M02_Admission` |
| Medical process | HIS `M02_MedicalProcess` |
| Per-section performer/sign state | HIS `M02_MedicalProcessDetail` |
| Digital signing workflow/file | HIS M02 signing workflow and sign-server |
| Link from KSK record to HIS encounter | `HEX_ExamRecord.AdmissionID` |

`health-exam-server` is an adapter and authorization boundary. It must not
invent or independently advance HIS signing state.

## HIS APIs used

### Form definition reads

- `GET /api/M03F00030/GetListTemplateByEMR`
- `GET /api/M03F00030/GetTemplateByID?templateID={templateId}`
- `GET /api/M03F00030/GetTreeTemplateByID?templateID={templateId}`
- `GET /api/M03F00030/GetTreeByFileDocTypeID?fileDocTypeID={fileDocTypeId}`

The adapter selects the template by the exact, case-insensitive code
`KSK-TREN18TUOI`. It never resolves the template by its Vietnamese display name.

### Medical-process and workflow reads

- `GET /api/M02F01500/RMedicalProcess?admissionID={admissionId}`
- `GET /api/M02F01500/RMedicalProcessByID?id={medicalProcessId}`
- `GET /api/M02F01500/RSWByDocTypeID?id={fileDocTypeId}`

### Allowed signing mutations

- `POST /api/M02F01500/SubmitEMR`
- `POST /api/M02F01500/SignEMR`
- `POST /api/M02F01500/CancleEMR`

The spelling `CancleEMR` is retained only in the downstream HIS route. The
public health-exam API uses `sign/cancel`.

### Explicitly forbidden in phase 1

- `POST /api/M02F01500/CUEMR`
- `POST /api/M03F10010/CUEMR`
- all M03 template mutation APIs

## Public health-exam API

All routes require the existing employee authentication policy. The API must
also receive a forwardable HIS credential for calls protected by HIS
`LicenseAuth`.

### Get the KSK form definition

```http
GET /v1/his-forms/KSK-TREN18TUOI
```

Behavior:

1. Read `GetListTemplateByEMR` from HIS.
2. Select the single active template whose code equals `KSK-TREN18TUOI`.
3. Return `404` if none exists and `409` if more than one active match exists.
4. In parallel after resolution, read `GetTemplateByID` and
   `GetTreeTemplateByID`.
5. Return one normalized definition document.
6. Cache the successful definition for five minutes. The cache key includes
   HIS base URL, division, and template code.

Response fields:

```json
{
  "templateId": "uuid",
  "templateCode": "KSK-TREN18TUOI",
  "templateName": "Phiếu khám sức khỏe và khám sức khỏe định kỳ dùng cho người từ đủ 18 tuổi trở lên",
  "fileDocTypeId": 510,
  "versionCode": "202609081530",
  "isDraft": false,
  "active": true,
  "details": [],
  "layout": []
}
```

`details` preserves the HIS `OrderNo`. `layout` preserves the flat HIS tree and
its `OrderString`, `LevelNo`, `ParentItemID`, `ItemRootID`, `ItemGroupID`, control
metadata, default values, and formula metadata. Phase 1 deliberately avoids a
lossy conversion to a new field schema.

### Get the form associated with an exam record

```http
GET /v1/exam-records/{recordId}/his-form
```

Behavior:

- Validate that the record exists and is visible in the current division.
- Return the same definition as `GET /v1/his-forms/KSK-TREN18TUOI`.
- Include the record's `AdmissionID` in the envelope.
- This endpoint does not create an admission or medical process.

### Read medical processes for the record

```http
GET /v1/exam-records/{recordId}/his-form/processes
```

Behavior:

- Require `ExamRecord.AdmissionID > 0`.
- Call HIS `RMedicalProcess` using that admission.
- Select/return processes whose template or file document corresponds to
  `KSK-TREN18TUOI`.
- Never cache the result.

### Read one medical process and section statuses

```http
GET /v1/exam-records/{recordId}/his-form/processes/{medicalProcessId}
```

Behavior:

- Require the record's `AdmissionID`.
- Verify `medicalProcessId` belongs to a process returned for that admission;
  do not allow arbitrary cross-record process access.
- Call HIS `RMedicalProcessByID` and return its workflow rows, including
  `SWStep`, `ItemGroupID`, signer, handled date, and sign status.

### Read configured signing workflow

```http
GET /v1/exam-records/{recordId}/his-form/processes/{medicalProcessId}/sign-workflow
```

Behavior:

- Apply the same ownership check as the process-detail endpoint.
- Resolve the process's `FileDocTypeID`.
- Call HIS `RSWByDocTypeID`.
- Never cache the result because HIS workflow configuration may affect who can
  sign now.

### Submit a section for signing

```http
POST /v1/exam-records/{recordId}/his-form/processes/{medicalProcessId}/sections/{sectionKey}/submit
```

Request:

```json
{
  "swStep": 0,
  "signatoryFlows": []
}
```

The adapter constructs the downstream HIS request:

```json
{
  "id": "medicalProcessId",
  "swStep": 0,
  "itemGroupID": "sectionKey",
  "signatoryFlows": []
}
```

It forwards the request to HIS `SubmitEMR` without interpreting or duplicating
the signing workflow.

### Sign a section

```http
POST /v1/exam-records/{recordId}/his-form/processes/{medicalProcessId}/sections/{sectionKey}/sign
```

Request:

```json
{
  "swStep": 0,
  "employeeId": 456,
  "handledRoleId": 123
}
```

The authenticated HIS identity must match the intended signer. The adapter
must not allow the caller to use an arbitrary employee identity. It constructs
the downstream HIS `SignEMR` request with `Id`, `SWStep`, `ItemGroupID`, `EmpID`,
and `HandledRoleID`.

### Cancel a section signature

```http
POST /v1/exam-records/{recordId}/his-form/processes/{medicalProcessId}/sections/{sectionKey}/sign/cancel
```

Request:

```json
{
  "swStep": 0,
  "reason": "Nhập sai kết quả"
}
```

The reason is required after trimming. The adapter forwards the request to HIS
`CancleEMR` with `Id`, `SWStep`, `ItemGroupID`, and `Reason`.

## Admission linkage

Add nullable `long? AdmissionID` to `HEX_ExamRecord` and the corresponding API
models. It is a cross-service reference and has no foreign key in the
health-exam database.

Phase 1 accepts `AdmissionID` through the existing exam-record create/update
contracts. It does not automatically call HIS `CUAdmission`; admission creation
belongs to the registration/HIS-integration workflow and requires a separate
design. Form-definition reads work without `AdmissionID`, but all
medical-process and signing routes return a validation error when it is absent.

`PatientID`, `SessionID`, and `RecordID` must never be substituted for
`AdmissionID`.

## Section identity

HIS M02 stores `M02_MedicalProcessDetail.ItemGroupID` from
`M03_ItemGroup.FunctionID`, which is a nullable string. This is not guaranteed
to equal the numeric M03 `ItemGroupID` displayed in the form layout.

Therefore phase 1 public signing routes use an opaque string `sectionKey` and
pass it unchanged to HIS. The API must not parse it as a number. The process
detail response is the authoritative source for signable `sectionKey` values.
The definition response retains the numeric `ItemGroupID` separately for UI
grouping.

Before forwarding a signing mutation, the adapter reads/uses the current
process details and rejects a `sectionKey` that does not belong to the selected
medical process.

## Authentication and signer identity

HIS controllers use `LicenseAuth`, and M02 handlers derive the current employee
from `IWebCoreSecurity.EmployeeID`. A single health-exam service credential
would incorrectly record every operation as the service account.

For phase 1:

- The inbound request must carry the user's HIS credential in the configured
  forwarding header.
- `health-exam-server` forwards that credential only to the configured HIS
  origin.
- The credential is never logged, cached, persisted, or returned.
- Missing credentials produce `401` before calling HIS.
- The requested `employeeId`, when present, must match the employee identity
  asserted by the authenticated health-exam context; otherwise return `403`.

## Error handling

- Preserve the health-exam `ResultData<T>` envelope.
- Map downstream HIS `401/403` to the equivalent health-exam status.
- Map HIS validation/business `400` responses to `422` when the downstream
  error represents a rejected operation; retain the HIS message for users.
- Map HIS `404` to `404`.
- Return `502` for malformed HIS responses and connection failures.
- Return `504` for HIS timeouts.
- Do not retry signing mutations automatically.
- A definition GET may retry once for transient connection failure before any
  response bytes are received.

## Concurrency and idempotency

Definition reads are idempotent and cacheable. Signing mutations are not
automatically retried. Concurrent sign calls are serialized by HIS state and
workflow checks; the adapter always revalidates process ownership and the
section key immediately before forwarding.

No success is synthesized. The adapter reports success only when HIS returns a
successful `ResultData` response.

## Observability

Log:

- operation name;
- record ID;
- admission ID;
- medical process ID;
- section key;
- HIS status and duration;
- correlation ID.

Never log authorization headers, certificate data, or full form values.

## Testing

### Client contract tests

- Correct HIS routes, query parameters, and JSON property names.
- Credential forwarding.
- Timeout and error-envelope mapping.
- No retry for submit/sign/cancel.

### Form-definition service tests

- Resolves exactly `KSK-TREN18TUOI`.
- Rejects no match and duplicate active matches.
- Combines metadata, details, and layout without losing order or identifiers.
- Reuses the five-minute cache.
- Does not cache failures.

### Record/process authorization tests

- Missing record returns `404`.
- Missing `AdmissionID` returns validation error.
- A medical process belonging to another admission is rejected.
- An unknown section key is rejected before a signing mutation.

### Signing endpoint tests

- Submit, sign, and cancel use the correct HIS endpoint and payload.
- Cancel requires a nonblank reason.
- Signer mismatch returns `403`.
- HIS failure never produces a local success.
- No form value or template mutation endpoint is exposed.

### Regression tests

- Existing exam-record create/read/update responses include nullable
  `AdmissionID` without changing behavior for old records.
- Existing health-exam test suite remains green.

## Rollout

Required configuration:

- HIS base URL.
- HIS request timeout.
- Name of the inbound credential header to forward.
- Five-minute definition-cache duration.

Deployment order:

1. Apply the nullable `AdmissionID` migration.
2. Deploy the health-exam API with HIS integration disabled by default.
3. Configure HIS URL and credential forwarding in a non-production environment.
4. Verify reads against `KSK-TREN18TUOI`.
5. Verify one section submit/sign/cancel using a test admission and real HIS
   employee credential.
6. Enable the feature for KSK clients.

## Acceptance criteria

- The health-exam API returns the current active HIS definition for
  `KSK-TREN18TUOI` without contacting `form-server`.
- The returned definition preserves all HIS identifiers and ordering required
  to render the 12 configured parts.
- A health-exam record with an `AdmissionID` can read only its own HIS medical
  process and per-section signing status.
- Submit, sign, and cancel are executed by HIS with the real user credential.
- Phase 1 exposes no endpoint that changes form definitions or entered form
  values.
- `health-exam-server` stores no duplicate section signature state.
