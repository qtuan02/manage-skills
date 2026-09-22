# Clinical and Paraclinical Signing Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add one shared HIS-backed signing workflow for clinical and paraclinical documents, then connect signed CLS orders to PACS through HIS.

**Spec:** `docs/superpowers/specs/2026-09-18-clinical-paraclinical-signing-design.md`

## Task 1: Freeze HIS identity, signer and document contracts

- [ ] Document the source of `PtID`, `AdmID`, `TPID`, doctor, department, role and signer mapping.
- [ ] Confirm `FileDocTypeId` for clinical and paraclinical documents.
- [ ] Confirm HIS `SubmitFile`, `SignFiles`, status and signed-file response shapes.
- [ ] Add contract fixtures for successful and rejected signing responses.

## Task 2: Add shared signing persistence

- [ ] Add `SigningDocument` for `ClinicalExam` and `ParaclinicalOrder`.
- [ ] Add `SigningTransaction` with `SwtId`, `FileDocId`, file ID, step and status.
- [ ] Add signer mapping and workflow snapshot fields.
- [ ] Add unique keys for `(DocumentType, BusinessId)` and `(HisSwtId, Step)`.
- [ ] Add migration and persistence tests.

## Task 3: Implement PDF renderers

- [ ] Extract shared PDF metadata and artifact handling.
- [ ] Implement `ClinicalExamPdfRenderer` using existing clinical document data.
- [ ] Implement `ParaclinicalPdfRenderer` using HIS patient, encounter, diagnosis, doctor and service data.
- [ ] Add stable file hash and `FileDocTypeId` per document type.
- [ ] Test required fields, stable output metadata and empty/invalid data.

## Task 4: Implement shared HIS signing client

- [ ] Create `IHisSigningClient`.
- [ ] Implement `SubmitFile`, permission group lookup, file status, `SignFiles`, cancel and signed-file retrieval.
- [ ] Convert HIS `ResultData<T>` errors into typed application errors.
- [ ] Add timeout, cancellation, correlation ID and payload redaction.
- [ ] Test 2xx, HIS business error, 401, 404, timeout and 5xx.

## Task 5: Implement shared signing application service

- [ ] Add preview, submit, status, sign, cancel and file operations.
- [ ] Route clinical and paraclinical documents through the same service.
- [ ] Enforce signing state transitions and signer permission checks.
- [ ] Enqueue all HIS mutations through outbox.
- [ ] Add idempotency keys `SIGN:Submit:{DocumentId}`, `SIGN:Step:{SwtId}:{Step}`, and `SIGN:Cancel:{SwtId}`.
- [ ] Reconcile ambiguous `SubmitFile` timeout before retry.
- [ ] Test both document types and multi-step signing.

## Task 6: Integrate clinical exam signing

- [ ] Add clinical preview/submit/status/sign/cancel/file endpoints.
- [ ] Link `ExamRecord` to `SigningDocument`.
- [ ] Persist signed PDF and final signing status.
- [ ] Test rejected, cancelled, partial and completed workflows.

## Task 7: Integrate paraclinical order and HIS identity

- [ ] Add `ParaclinicalOrder` HIS identity fields.
- [ ] Add local-service → `MedSerID` mapping.
- [ ] Resolve or create/link patient, admission and treatment process according to the confirmed HIS contract.
- [ ] Reject submit when `PtID`, `AdmID`, `TPID`, signer, department or service mapping is missing.
- [ ] Test valid and invalid mappings.

## Task 8: Submit signed CLS to HIS and connect PACS

- [ ] Add paraclinical sign endpoints using the shared signing service.
- [ ] After signing succeeds, enqueue `POST /api/M02F00710/ClinicalRequest`.
- [ ] Store `ParaClinReqID` and detail IDs.
- [ ] Enqueue `CareProcessConnectTP` only after request creation succeeds.
- [ ] Store process linkage and prevent duplicate connect.
- [ ] Test signing failure, HIS request failure, connect retry and duplicate prevention.

## Task 9: Poll and import CLS results

- [ ] Poll `RParaClinicalResultByAdmID` for active orders.
- [ ] Fetch details using `RParaClinicalByID`.
- [ ] Upsert by HIS result/detail IDs.
- [ ] Map details to local order items and update `Completed`.
- [ ] Test duplicate polling, partial results, changed results and unmatched details.

## Task 10: Cancel, reconcile and audit

- [ ] Add cancellation for both signing documents and CLS orders.
- [ ] Use HIS signing cancel and `CareProcessConnectCancelTP` where applicable.
- [ ] Reconcile local/HIS signing and CLS states.
- [ ] Record actor, source, timestamp, correlation ID and sanitized errors.
- [ ] Add metrics for submit, sign, connect, cancel, polling and reconciliation.

## Task 11: End-to-end verification

- [ ] Complete a clinical document through HIS signing.
- [ ] Complete a CLS document through HIS signing.
- [ ] Verify signed CLS creates one HIS request and one PACS connect.
- [ ] Verify result polling and display.
- [ ] Verify retries do not duplicate signing, request or connect.
- [ ] Verify invalid identity, service and signer permissions.
- [ ] Run targeted tests, build and static checks.

## Delivery order

```text
1 identity contract
  → 2 signing persistence
  → 3 PDF renderers
  → 4 HIS signing client
  → 5 shared signing service
  → 6 clinical signing
  → 7 CLS identity/mapping
  → 8 CLS submit/connect
  → 9 result polling
  → 10 cancel/reconcile/audit
  → 11 E2E
```
