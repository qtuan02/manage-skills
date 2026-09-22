# Step 2 HIS Form Value Persistence Design

## Goal

Persist entered values for the two configured sections of the `DTK_03` health
exam group to HIS, using the `KSK-TREN18TUOI` template:

| Section kind | HIS `ItemGroupID` |
| --- | --- |
| `HISTORY` | `64` |
| `EXTRA_INFO` | `63` |

The health-exam API, not the browser, owns the HIS integration.  It creates an
HIS admission before a first form write and keeps the resulting `AdmissionID`
and `EMRDataID` on the local exam record.

## Scope

Included:

- create an HIS admission for a manually created exam record when needed;
- create and update user-entered M03 form values for the two configured
  sections;
- validate section and field ownership from the tenant-scoped group/form
  mapping and live HIS definition;
- preserve values in the other section when one section is edited;
- expose retryable, explicit failures for the admission/form integration.

Excluded:

- medical-process creation, submission, or digital signing;
- other exam groups or template sections;
- client calls to HIS and a mapping-management UI;
- automatic/background retry or a distributed transaction across the two
  services.

## HIS persistence model

HIS stores a completed/draft M03 form as one header plus value rows:

| HIS table | Role |
| --- | --- |
| `M03_EMRData` | form instance: template, admission, patient, author, draft state |
| `M03_EMRDataDetail` | field value: `ItemID`, `Value`, `Text`, type/display metadata |

`POST /api/M03F10010/CUEMR` creates a form when `EMRDataID` is empty.  On
update it deletes all existing detail rows for that `EMRDataID` and inserts the
submitted list again.  Therefore `HISTORY` and `EXTRA_INFO` are not two
independently persisted HIS forms: they are two sections of one
`KSK-TREN18TUOI` instance and each HIS write must contain the full retained
snapshot.

The M02 `CUEMR` route is intentionally not used in this phase because it
requires an existing `MedicalProcessID` and additionally changes medical-record
process/signing state.

## Architecture and flow

The existing admission-link design remains the authority for creating an HIS
admission and for its idempotency key (`HEX-{RecordID:N}`).  Form-value write
is added as a separate application use case behind `IExamFormService` and the
HIS adapter.  The HTTP transport is the sole owner of the HIS route and wire
format.

```text
browser
  -> health-exam-server: create/update section values
  -> validate tenant-owned record, mapping and field IDs
  -> ensure local record has a valid HIS AdmissionID
  -> load current M03 values (when an EMRDataID exists)
  -> merge permitted changed section values by ItemID
  -> HIS M03F10010/CUEMR with complete Details snapshot
  -> persist HisEmrDataID and form-sync state on exam record
  -> response
```

For a new record, creation remains locally durable first.  The admission is
created/linked, then the form is written.  A successful HIS form write stores
its returned `EMRDataID` locally.  The browser never provides `AdmissionID`,
`EMRDataID`, `TemplateID`, or `ItemGroupID` as authority values.

## API contract

Add record-scoped save endpoints or an equivalent operation under the existing
exam-record controller.  The public request identifies the semantic section,
not HIS implementation keys:

```http
PUT /v1/exam-records/{recordId}/registration-form/sections/{sectionKind}
```

```json
{
  "fields": [
    { "itemId": "uuid", "value": "...", "text": "..." }
  ],
  "isDraft": true
}
```

Initially `sectionKind` accepts only `HISTORY` and `EXTRA_INFO`.  The response
returns the normalized registration form, including current values and form
sync status, so the client can refresh both sections after a successful save.

The service resolves the record's selected `VariantCode`, mapping/template and
section `ItemGroupID` server-side.  It accepts a field only when its `ItemID`
is a writable leaf belonging to that section in the active HIS template.
Read-only fields, unknown IDs, and fields from the other section are rejected.

## Merge and update semantics

1. Resolve `KSK-TREN18TUOI` and both configured sections from the live HIS
   definition.
2. For an existing `HisEmrDataID`, read the persisted HIS form details.
3. Replace only values for field IDs submitted for the requested section;
   keep valid values from the other section and untouched fields in the same
   section.
4. Send the merged, full `Details` collection to `M03F10010/CUEMR`.
5. Store the returned/new `EMRDataID` after HIS reports success.

The server must not invent a detail for a layout/group container that has no
writable `ItemID`.  Type/control metadata sent to HIS comes from the resolved
template definition, rather than from the browser.

## Consistency, security, and errors

`HEX_ExamRecord` records at least `AdmissionID`, `HisEmrDataID`, template ID
and a form-sync state/error summary.  It remains the local reference for later
updates; HIS remains the source of truth for entered values.

- If admission creation fails, no form write is attempted.  The local record
  stays pending and the caller can retry the admission path.
- If admission succeeds but M03 save fails, preserve `AdmissionID`, mark form
  sync failed and return a non-success result.  A later retry must reuse the
  admission rather than create another one.
- A form mutation is sent to HIS once per request; no transparent HTTP retry is
  allowed because an interrupted call may already have written at HIS.
- Every record lookup and mapping resolution is constrained by the authenticated
  division.  HIS credentials are forwarded only by the HIS HTTP adapter and
  never persisted or logged.

## Verification

Tests are written first and prove:

- a new form write first obtains/reuses a positive HIS admission ID and stores
  the returned M03 `EMRDataID`;
- `HISTORY` (`64`) and `EXTRA_INFO` (`63`) resolve from the DB mapping for
  `DTK_03`/`KSK-TREN18TUOI`;
- updating one section sends a merged full snapshot and retains values in the
  other;
- unknown, cross-section, read-only, and inactive-template fields are rejected
  before any HIS mutation;
- failed admission never calls M03; failed M03 leaves a retryable local state;
- routes, tenant isolation, forwarded auth/trace headers, exact M03 payload,
  and no-retry behavior are covered by HTTP-adapter tests.
