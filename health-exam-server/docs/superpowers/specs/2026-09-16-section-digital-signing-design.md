# Section Digital Signing Design

## Goal and Scope

Enable submit, digital sign, cancel signature, and live status for each health-examination specialty/section by changing only `health-exam-server` and calling existing `his-server` APIs unchanged.

In scope:

- Use existing HIS routes `RMedicalProcessByID`, `RSWByDocTypeID`, `SubmitEMR`, `SignEMR`, and `CancleEMR`.
- Derive signing step, role, employee, and submit flow inside `health-exam-server` from authenticated context and live HIS data.
- Read status directly from HIS; never persist a local signing-state copy.
- Keep record → admission → KSK process → section ownership validation.

Out of scope:

- Any source, database, configuration, or deployment change in `his-server`.
- Whole-record signing, a second signing engine, certificate storage, or a health-exam signing table.
- Hardening HIS routes against callers other than `health-exam-server`.

## Existing HIS Contract

`health-exam-server` already calls:

- `GET api/M02F01500/RMedicalProcessByID?id={processId}`
- `GET api/M02F01500/RSWByDocTypeID?id={fileDocTypeId}`
- `POST api/M02F01500/SubmitEMR`
- `POST api/M02F01500/SignEMR`
- `POST api/M02F01500/CancleEMR`

HIS mutations require caller-provided `SWStep`, `EmpID`, `HandledRoleID`, and `SignatoryFlows`. Since HIS cannot change, `health-exam-server` calculates them from the authenticated employee and current HIS process/workflow response. The frontend never supplies them.

## Architecture

```text
Frontend
  -> health-exam-server
       validate record/admission/process/template/section
       fetch process detail from HIS
       fetch workflow by FileDocTypeID from HIS
       resolve current step, role, state and allowed action
       derive employee from authenticated request context
       call the existing HIS mutation
       re-fetch process detail
       normalize and return the observed section state
```

HIS remains the source of truth for process details, workflow, signed files, and signing state. Orchestration and normalization live in `health-exam-server`; no state is copied into its database.

## Signing Context Resolver

Add one pure application-layer resolver. Inputs:

- raw process JSON from `RMedicalProcessByID`;
- raw workflow JSON from `RSWByDocTypeID`;
- authenticated actor ID and kind.

The resolver accepts process-detail aliases already handled by the integration: `MedicalProcessDetails`, `Details`, and `M02_MedicalProcessDetail`. It reads workflow steps from the workflow `Details` array and produces one context per `ItemGroupID`:

- section key/name and stable state;
- current step/name and resolved role ID;
- authenticated employee ID;
- derived `SignatoryFlows` for submit;
- `CanSubmit`, `CanSign`, and `CanCancel`.

Rules:

- Only `ActorKind.Employee` with a positive numeric actor ID can sign.
- Workflow steps are ordered by `SWStep`.
- Current step comes from the matching section detail when present; otherwise use the first workflow step.
- Role comes from the workflow detail matching the resolved step.
- `New` permits submit, `InProcessing` permits sign, and `Finish` maps to `Signed` and permits cancel unless a later step is finished.
- Missing/inconsistent process or workflow data fails; never send step or role `0` as a guessed default.

Submit, sign, cancel, and read handlers reuse this resolver.

## Public API

```http
GET  /v1/exam-records/{recordId}/his-form/processes/{processId}/sections
POST /v1/exam-records/{recordId}/his-form/processes/{processId}/sections/{sectionKey}/submit
POST /v1/exam-records/{recordId}/his-form/processes/{processId}/sections/{sectionKey}/sign
POST /v1/exam-records/{recordId}/his-form/processes/{processId}/sections/{sectionKey}/sign/cancel
```

Submit/sign have no body. Cancel accepts only `{"Reason":"Ký nhầm kết quả"}`.

Normalized section response:

```json
{
  "SectionKey": "KSK_NOI",
  "SectionName": "Khám Nội",
  "Status": "InProcessing",
  "CurrentStep": 1,
  "CurrentStepName": "Bác sĩ chuyên khoa ký",
  "CanSubmit": false,
  "CanSign": true,
  "CanCancel": false,
  "SignedByEmployeeID": null,
  "SignedAt": null,
  "CancelReason": null
}
```

## Internal HIS Mutation Payloads

All values except cancellation reason and route identity are derived server-side.

```json
// SubmitEMR
{"Id":"{processId}","SWStep":1,"ItemGroupID":"KSK_NOI","SignatoryFlows":[{"SWStep":1,"SWRoleID":10,"SignatoryID":100}]}

// SignEMR
{"Id":"{processId}","SWStep":1,"ItemGroupID":"KSK_NOI","EmpID":100,"HandledRoleID":10}

// CancleEMR
{"Id":"{processId}","SWStep":1,"ItemGroupID":"KSK_NOI","Reason":"Ký nhầm kết quả"}
```

After HIS success, the handler fetches the process again and returns its observed post-mutation state. A refresh failure is returned instead of fabricating success.

## Validation and Errors

Before every operation:

1. Resolve the record by division and ID.
2. Require a positive `AdmissionID`.
3. Verify the process belongs to the admission and configured KSK template/document type.
4. Verify the section exists.
5. Require the section-signing feature flag.
6. Resolve live process/workflow data before building a mutation.

Mapping: malformed data/reason `400`; authentication `401`; non-employee/invalid employee `403`; missing scoped resource `404`; invalid transition `409`; invalid/unavailable HIS response `502`; timeout `504`.

## Rollout and Tests

Add `HIS_EMR_SECTION_SIGNING_ENABLED`, default `false`. Rollback is setting it to `false`; no database rollback exists.

Test the pure resolver, handler-derived payloads, refresh-after-mutation, exact existing HIS routes, reduced public requests, ownership validation, and endpoint contracts. Full PostgreSQL integration tests additionally require `HEALTHEXAM_TEST_DB`.

## Known Boundary

This prevents the health-exam frontend from choosing signer, role, or step. It does not harden HIS itself: another direct HIS client can still send its own legacy payload. That requires separately authorized HIS work.
