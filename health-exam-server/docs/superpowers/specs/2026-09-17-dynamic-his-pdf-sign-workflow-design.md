# Dynamic HIS PDF Signing Workflow Design

**Date:** 2026-09-17
**Scope:** `health-exam-server` only
**Status:** Approved in brainstorming

## Goal

Replace the forced one-step PDF signing payload (`SWStep = 1`, `SWRoleID = 0`, current employee as `SignatoryID`) with the workflow configured in HIS for the resolved `FileDocTypeID`. Each authenticated employee signs only the currently active workflow step that HIS says they are allowed to sign.

## Constraints

- Change only `health-exam-server`; do not modify HIS source, database, configuration, or deployment.
- Continue using the existing HIS PDF and signing APIs.
- The frontend never sends employee, role, step, `SWTID`, or `SWTDetailID` as signing decisions.
- HIS remains the source of truth for workflow, permissions, current step, signature placement, and final PDF.
- Do not use a git worktree.

## Key Decision

Health-exam does not assign every workflow step to a named employee. Before submission it reads `GET api/M02F01500/RSWByDocTypeID?id={FileDocTypeID}` and validates that an active workflow with non-empty details exists. It then calls `SubmitFile` without `SignatoryID` and without caller-built `SignatoryFlows`, allowing HIS to create transaction details from its configured workflow.

For each signing action, health-exam calls `GET api/M02F30000/GetFileSign` with the admission, current employee, file-document type, department, and pending status. HIS applies its role/group/department rules and returns only steps the authenticated employee can currently handle. Health-exam signs the matching transaction detail through `POST api/M02F30000/SignFiles`.

This matters because HIS currently replaces supplied flows with `{ SignatoryID, SWStep = 1 }` whenever top-level `SignatoryID` is present. Omitting that field is required for a genuine multi-step workflow.

## Flow

### Initial submission

1. Re-evaluate local conclusion eligibility.
2. Resolve `FileDocTypeID` from the record's actual HIS template.
3. Fetch and validate the live workflow by `FileDocTypeID`.
4. Render the current HIS form PDF.
5. Call `SubmitFile` with the PDF, admission, department, submission employee, table, key, and voucher date only.
6. Persist the returned `SWTID`, `FileDocID`, `FilePath`, and status immediately, including `New`/`InProcessing` responses.

### Employee signing

1. Query pending files from `GetFileSign` for the authenticated employee.
2. Select the row matching the record's persisted `FileDocID` or stable `KeyID`; never select another document from the same admission.
3. Require the returned row to reference the transaction's current step and contain positive `SWTID`, `SWTDetailID`, `SWStep`, and `SWRoleID`/handled role.
4. Call `SignFiles` using authenticated employee identity and the HIS-returned transaction identifiers and role.
5. Re-read HIS state. Persist the observed status and signed file path.
6. Return `InProcessing` while later steps remain, and `Signed` only after HIS marks the transaction finished.

The same public `POST /v1/exam-records/{recordId}/conclusion/sign` route performs the next valid action: submit when no transaction exists, sign the current employee's pending step when one is available, or return the existing final state when already signed.

## Eligibility and Status

`GET /v1/exam-records/{recordId}/conclusion-eligibility` continues to answer whether the medical data is ready for conclusion signing. It must additionally expose whether the current employee has a pending HIS signing step once a transaction exists. Readiness and permission are separate facts: a record can be ready while the current employee is not the signer for the active step.

The sign response must expose the real `SWTID`, current step, status, signer, and signing time. It must not return `ProcessID = Guid.Empty` or `SectionKey = "PDF"` as substitutes for HIS transaction identity.

## Persistence and Idempotency

Add the minimum local transaction pointers needed to resume safely:

- `HisSignTransactionID` (`SWTID`)
- `HisSignStatus`
- `HisSignKeyID`
- `HisSignedByEmployeeID`
- `HisSignedAt`

Reuse the existing `HisSignedFileDocID` and `HisSignedFilePath` fields. Use one stable key per record and PDF revision. Repeated requests must first reconcile the persisted transaction with HIS and must not submit another file while a non-cancelled transaction exists.

Concurrent calls must be serialized for the record using the repository's PostgreSQL locking/transaction pattern or an atomic compare-and-set. EF tracking alone is not a lock.

## Errors

- Missing/malformed HIS workflow: integration failure; never fall back to step or role zero.
- No pending step for the current employee: `409 Conflict` with the active step/required role when available, not `403` guessed locally.
- HIS explicitly denies the employee: `403 Forbidden`.
- Ambiguous pending rows for the same file: integration failure; do not pick the first.
- Submit succeeded but refresh failed: retain transaction identifiers and return refresh-required/in-processing state.
- Signed record whose signed file is unavailable: return an error; never silently serve the unsigned draft.

## Verification

Tests must prove workflow lookup occurs before submission, top-level `SignatoryID` and all fabricated `SignatoryFlows` are absent, only HIS-authorized pending details are signed, users cannot sign another employee's step, retries do not submit duplicates, multi-step state advances one step at a time, and preview returns the final signed PDF.

Staging acceptance uses at least two employees with different HIS roles: employee A signs step 1, employee B cannot sign step 1 but can sign step 2 after A completes it, and the final preview contains both visible signatures.

## Deferred

- Editing HIS workflow master data.
- A health-exam UI for assigning named employees to workflow steps.
- Cancelling or replacing a completed signature transaction.
- Local storage of signed PDF bytes.
