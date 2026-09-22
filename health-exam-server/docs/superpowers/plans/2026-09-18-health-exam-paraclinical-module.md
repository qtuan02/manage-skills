# Health Exam Paraclinical Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build the health-exam CLS module described in `docs/superpowers/specs/2026-09-18-health-exam-paraclinical-design.md`.

**Architecture:** Local order/result domain plus HIS adapter and outbox. HIS remains the identity source and PACS gateway. The first release polls HIS for results.

**Tech Stack:** Existing ASP.NET Core, EF Core/PostgreSQL, existing paraclinical domain, existing outbox/worker, typed HttpClient.

## Task 1: Freeze HIS identity and service mapping

**Files:** existing exam-record integration/mapping files; new contract fixtures and tests.

- [ ] Document how each record obtains `PtID`, `AdmID`, `TPID`, `PCReqDoctorID`, and `ReqDeptID`.
- [ ] Add local-service → HIS `MedSerID` mapping.
- [ ] Reject submit when any required HIS identity or service mapping is missing.
- [ ] Add tests for valid mapping and missing mapping.

## Task 2: Add persistence and state model

**Files:** existing paraclinical entities, EF configurations, migration, repository tests.

- [ ] Add HIS linkage fields to order and order-item entities.
- [ ] Add result header/detail entities and unique HIS IDs.
- [ ] Implement `Draft → Submitted → Ordered → InProgress → Completed`.
- [ ] Implement cancellation and terminal-state guards.
- [ ] Add migration and persistence tests.

## Task 3: Implement the HIS client

**Files:** `HealthExam.Application/Integrations/IHisParaclinicalClient.cs`, HIS DTOs, `HealthExam.Infrastructure/Integrations/HisEmr/HisParaclinicalClient.cs`, DI/configuration, HTTP tests.

- [ ] Implement admission lookup, service catalog, clinical request, connect, cancel, and result methods.
- [ ] Parse HIS `ResultData<T>` and convert non-zero `errorCode` to typed errors.
- [ ] Use existing authentication, timeout, cancellation, correlation and redaction conventions.
- [ ] Test 2xx, HIS business errors, 401, 404, timeout, 5xx and malformed responses.

## Task 4: Add local order APIs

**Files:** existing `ParaclinicalOrderController`, application handlers, validators, API tests.

- [ ] Add create/list/get endpoints from the design spec.
- [ ] Create local `Draft` only; do not call HIS synchronously.
- [ ] Validate exam record, service mapping, quantity, schedule and duplicate lines.
- [ ] Return stable local order/item IDs.

## Task 5: Submit and connect through outbox

**Files:** existing outbox model/worker, submit handler, connect handler, integration tests.

- [ ] Add `POST /api/paraclinical-orders/{orderId}/submit`.
- [ ] Enqueue `SubmitParaclinicalOrder` with `HIS:ClinicalRequest:{OrderId}`.
- [ ] Map local order to HIS `ClinicalRequest` and save `ParaClinReqID`.
- [ ] Enqueue `ConnectParaclinicalOrder` with `HIS:ClinicalConnect:{OrderId}`.
- [ ] Call `CareProcessConnectTP` only after request creation succeeds.
- [ ] Reconcile after ambiguous timeout before retrying creation.
- [ ] Test idempotency and no duplicate HIS request.

## Task 6: Implement polling and result import

**Files:** result handler, polling worker, result mapper, result tests.

- [ ] Poll `RParaClinicalResultByAdmID` for active HIS-linked orders.
- [ ] Fetch details using `RParaClinicalByID` for new result IDs.
- [ ] Upsert by HIS result/detail IDs.
- [ ] Map details to local items and update states.
- [ ] Test duplicate polling, partial results, changed results and unmatched details.

## Task 7: Implement cancellation

**Files:** cancel handler, cancel outbox handler, state tests.

- [ ] Add `POST /api/paraclinical-orders/{orderId}/cancel`.
- [ ] Enqueue cancellation with `HIS:ClinicalCancel:{OrderId}`.
- [ ] Use HIS delete/cancel API and `CareProcessConnectCancelTP` when connected.
- [ ] Reject cancellation after `Completed`.
- [ ] Test cancellation before submit, after submit, after connect and after result.

## Task 8: Reconciliation, security and operations

**Files:** reconciliation worker, audit/logging/metrics integration, operational tests.

- [ ] Detect local/HIS missing links and status mismatches.
- [ ] Record correlation ID, operation, attempt count, status and sanitized failure.
- [ ] Add permissions for view/create/submit/cancel/result/mapping.
- [ ] Redact tokens and unnecessary patient payloads.
- [ ] Add metrics for submit, connect, cancel, polling and reconciliation failures.

## Task 9: End-to-end verification

**Files:** HIS integration fixtures and E2E tests.

- [ ] Test one laboratory order.
- [ ] Test one imaging order.
- [ ] Test multi-item order.
- [ ] Verify HIS IDs are persisted.
- [ ] Verify result polling and display.
- [ ] Verify cancellation and retry behavior.
- [ ] Verify invalid `TPID`, doctor, department and service mapping.
- [ ] Run targeted tests, build and static checks.

## Delivery order

```text
Task 1 → Task 2 → Task 3 → Task 4 → Task 5 → Task 6
                                              ↓
                                      Task 7 → Task 8 → Task 9
```
