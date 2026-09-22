# HIS Conclusion Signing and Signed PDF Preview Design

**Date:** 2026-09-16
**Scope:** `health-exam-server` only
**Status:** Approved in brainstorming

## Goal

Replace the legacy form-server conclusion-signing path with an HIS-only flow.
After a conclusion is digitally signed, the existing PDF preview endpoint must
return the signed PDF produced and stored by HIS. During the examination,
preview should show any signed HIS files when available and may otherwise fall
back to the current unsigned draft rendering.

## Scope

In scope:

- Change only `health-exam-server`.
- Use existing, unchanged `his-server` APIs.
- Sign the conclusion section through the HIS medical-process workflow.
- Derive process, section, step, role, signatory flow, and signer server-side.
- Return live HIS signing state; do not persist a local signing-state copy.
- Serve the signed HIS PDF after conclusion signing.
- Preserve the current preview route for frontend compatibility.

Out of scope:

- Any source, database, configuration, or deployment change in `his-server`.
- Calls to `form-server` for conclusion eligibility, signing, or preview.
- A second signing engine, certificate storage, PDF signature stamping, or PDF
  caching inside `health-exam-server`.
- Frontend implementation.

## Source of Truth

HIS is the sole source of truth for medical processes, signing workflow,
section signing state, signed files, signer identity, and signing time.
`health-exam-server` owns record/tenant validation, local conclusion
preconditions, orchestration, response normalization, and the stable public
API.

`health-exam-server` must never draw or paste a signature image into a PDF. A
preview is considered signed only when its bytes come from the HIS signed-file
endpoint.

## Existing HIS Contracts

The design reuses these deployed routes unchanged:

- `GET api/M02F01500/RMedicalProcess?admissionID={admissionId}`
- `GET api/M02F01500/RMedicalProcessByID?id={processId}`
- `GET api/M02F01500/RSWByDocTypeID?id={fileDocTypeId}`
- `POST api/M02F01500/SubmitEMR`
- `POST api/M02F01500/SignEMR`
- `GET api/M02F01500/VEMRs?admissionID={admissionId}`
- `GET api/M03F10010/VEMR?EMRDataID={emrDataId}&IsJson=false`

HIS identifies the conclusion/medical-record summary section with
`ItemGroupID = "M02F01512"` (`MedicalRecordGroup.MedicalRecordSummary`). The
integration uses this stable contract; it must not discover the conclusion by
matching a Vietnamese display name.

## Architecture

```text
Frontend
  -> health-exam-server
       validate tenant, record, admission, and local conclusion conditions
       resolve VariantCode -> MedicalTypeCode from the active DB mapping
       read admission processes from HIS
       select the matching KSK process
       read process detail and sign workflow from HIS
       resolve section M02F01512 and authenticated employee context
       submit when Draft/New
       sign when InProcessing
       re-read HIS state
       return success only when the conclusion section is Signed/Finish
  -> his-server APIs (unchanged)
```

The orchestration reuses the section-signing resolver and handlers already
present in `health-exam-server`. It adds a conclusion-specific coordinator
rather than duplicating HIS workflow parsing in the paraclinical module.

## Conclusion Sign API

Keep the existing public route:

```http
POST /v1/exam-records/{recordId}/conclusion/sign
```

The request has no caller-controlled signing data. A missing body or `{}` is
valid. Remove the legacy `ConclusionSectionID`, `Actor`, and `SignatoryFlows`
fields from this route's contract. The signer comes from the authenticated
request context; process, section, step, role, and flow come from live HIS
data.

Processing rules:

1. Load the record by current `DivisionID` and `recordId`.
2. Re-evaluate the local conclusion preconditions at command time. The
   implementation must not call form-server; any remaining clinical
   completion requirement is read from health-exam data and live HIS state.
3. Require a positive `AdmissionID`.
4. Resolve the active form mapping for the record variant and require a
   non-empty `MedicalTypeCode`.
5. Read HIS processes for the admission and select the unique process whose
   `MedicalTypeCode` matches the mapping. Zero or multiple matches are an
   invalid/inconsistent state; never choose the first silently.
6. Resolve live section contexts for the process and select exact ordinal
   section key `M02F01512`.
7. If the section is Draft/New, invoke the existing derived `SubmitEMR` path
   and read live state again.
8. If the section is InProcessing, invoke the existing derived `SignEMR` path
   and read live state again.
9. If the section is already Signed/Finish, return success without another
   mutation.
10. Return success only when the observed final state is Signed/Finish.

The normalized response contains at least `ProcessID`, `SectionKey`, `Status`,
`SignedByEmployeeID`, and `SignedAt`. It may retain the existing conclusion
condition and step arrays if compatibility requires them, but their values
must describe the HIS-only execution and must not mention form-server.

## Idempotency and Partial Progress

There is no distributed transaction around HIS calls.

- Never retry `SubmitEMR` or `SignEMR` automatically.
- After every successful mutation, re-read HIS state.
- If submit succeeded and sign failed, return the observed InProcessing state.
  A later call resumes with sign and does not submit again.
- If HIS already reports Signed, return success without mutation.
- If a mutation response is ambiguous because a follow-up read failed, return
  a refresh-required integration error. A repeated request reads HIS before
  deciding whether another mutation is needed.

These rules make user retries safe without inventing a local signing state.

## Signed PDF Preview

Keep the existing route:

```http
GET /v1/exam-records/{recordId}/registration-form/preview
```

Preview selection is state-aware:

1. Validate record ownership by current `DivisionID`.
2. Read the conclusion section state from live HIS data.
3. When conclusion is Signed/Finish, call `M02F01500/VEMRs` with the record's
   `AdmissionID` and return those bytes. If HIS does not return a signed PDF,
   return a clear conflict/integration failure. Do not fall back to unsigned
   `VEMR`, because that would display an unsigned document after the user has
   completed signing.
4. Before conclusion is signed, try `VEMRs` first. If HIS already has signed
   specialty files, return the available signed aggregate PDF.
5. If no signed file exists during the examination, fall back to the current
   `M03F10010/VEMR` draft rendering by `HisEmrDataID`.

Successful responses remain raw PDF bytes with `Content-Type:
application/pdf` and inline disposition. The service does not persist or cache
PDF bytes.

## Errors and Security

- Missing record: `404 NotFound`.
- Missing admission, mapping, or matching KSK process: `409 InvalidState`.
- Multiple matching KSK processes: `409 InvalidState`.
- Missing conclusion section, malformed workflow, missing step, or missing
  role: integration failure (`502`); never send guessed zero/default values.
- Authenticated actor is not a valid employee or lacks signing permission:
  `403 Forbidden`.
- Invalid signing transition: `409 Conflict`.
- HIS timeout: `504`; other unavailable/malformed HIS responses: `502`.
- A signed conclusion whose signed PDF is not yet available must never return
  an unsigned fallback.

Credentials, PDF bytes, patient values, and signature contents must not be
logged. Continue forwarding the configured HIS credential header,
`X-Trace-Id`, and `X-Division-Id` through the existing HIS client.

## Testing

Application tests must prove:

- unique process selection by admission and mapped medical type;
- exact conclusion selection by `M02F01512`;
- Draft -> submit -> sign -> Signed orchestration;
- resume from InProcessing without a duplicate submit;
- idempotent success when already Signed;
- actor, step, role, and signatory flow are never accepted from the client;
- no conclusion call reaches form-server;
- malformed and ambiguous HIS data fail closed.

PDF tests must prove:

- Signed conclusion uses `VEMRs` and never calls/falls back to draft `VEMR`;
- before conclusion signing, a successful `VEMRs` response is preferred;
- before conclusion signing, missing signed files fall back to `VEMR`;
- tenant isolation, credential/header forwarding, timeouts, empty bytes, and
  non-PDF responses are handled correctly;
- the endpoint returns `%PDF-` bytes as `application/pdf` with inline
  disposition.

Endpoint/OpenAPI tests must prove the conclusion request no longer exposes
legacy signing-decision fields and the preview URL remains unchanged.

Staging acceptance requires this exact sequence:

1. Open a KSK record with a valid HIS admission and process.
2. Call conclusion sign as an authorized employee.
3. Observe the returned conclusion section as Signed.
4. Open the existing preview endpoint.
5. Visually confirm that the HIS-generated PDF displays the digital signature.

## Rollout and Rollback

Reuse the existing HIS/section-signing feature configuration. Enable the
HIS-only conclusion coordinator and signed-preview selection together so the
system cannot report a completed conclusion while still intentionally serving
the draft path.

Rollback disables the HIS conclusion-signing feature and restores the route
to an unavailable response; it must not reactivate form-server signing. No
health-exam signing-state migration or data rollback is required.

## Explicitly Deferred

- A dedicated per-section PDF preview endpoint.
- Local archival or hashing of signed PDF bytes.
- Background polling for eventual signed-file availability.
- Changes that harden or redesign HIS signing APIs.
