---
phase: "Phase 3 - Growth Channels"
epic: "Bulk and Payroll Payment Processing"
owning_service: "Payments and VTU"
modules: [File Intake, Validation, Repair, Payment Batches, Item Processing]
status: Draft
requirements: [PAY-005..PAY-015, CHN-005, API-003..API-004]
stories: [US-030]
dependencies: [Epic 01 Corporate Users Mandates and Entitlements, Phase 1 Payments, Phase 2 Risk and Reconciliation]
adrs: [Bulk file formats and partial processing, Batch/item idempotency]
last_updated: 2026-10-16
---

# Epic 02: Bulk and Payroll Payment Processing

## 1. Objective

Accept authenticated bulk/payroll files, validate every row before submission, support controlled repair, and track independent payment-item outcomes with an accurate aggregate batch state.

## 2. Success measures

- File source/format/size/count/digest and every row validate with stable reasons.
- Corporate maker-checker binds to the exact validated version.
- Each valid item has unique instruction/idempotency and explicit lifecycle.
- Batch totals/status derive from items; mixed outcomes remain visible and reconcilable.

## 3. Scope

Included: upload/reference, parsing, malware scan where applicable, validation, row errors, repair/version, totals, approval-ready batch, item creation, submission, status, export and reconciliation. Excluded: mandate decisions (Epic 3), invented bank file formats and unapproved all-or-nothing/partial policy.

## 4. Actors, roles and permissions

| Actor | Permission | Scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Corporate maker | `payment.bulk.create` | Corporate/accounts/limits | Submit | Maker |
| Repair operator | `payment.bulk.repair` | Own/assigned batch | Sensitive change | Reapproval |
| Corporate approver | `payment.bulk.approve` | Mandate/amount/account | Required | Epic 3 |
| Operations investigator | `payment.bulk.investigate` | Assigned exceptions | Required | Controlled action |

## 5. Domain model

`BulkFile`, `BulkBatch`, immutable `BatchVersion`, `ValidationRun`, `PaymentItem`, `RowError`, `Repair`, `BatchTotals`. State: `Uploaded -> Validating -> Invalid/RepairRequired/ReadyForApproval -> Approved -> Processing -> PartiallyCompleted/Completed/Failed`; each item has ordinary payment states including `Uncertain`.

Invariants: digest/version frozen for approval; invalid row never posts; repair creates new version and invalidates approval; each item is independently idempotent; batch status/totals are derived; file/client fees/totals untrusted; ledger alone changes balances.

## 6. Use cases

Upload and validate; retrieve row reasons; repair/resubmit; preview totals/fees; send exact version for approval; fan out valid instructions; view batch/items; retry only proven-safe technical work; investigate failures.

## 7. API contracts

Routes: `POST /bulk-payment-files`, `GET /bulk-payment-batches/{id}/validation`, `POST /bulk-payment-batches/{id}/repairs`, `POST /bulk-payment-batches/{id}/submit`, `GET /bulk-payment-batches/{id}`, `/items`. Stream/bound uploads and paginate row results. Financial submission requires correlation/idempotency.

## 8. Event contracts

Produce `BulkFileReceived`, `BulkBatchValidated`, `BulkBatchReadyForApproval`, `BulkPaymentItemCreated`, `BulkBatchStatusChanged`. Consume corporate approval and payment/ledger/provider outcomes. Order items individually; batch projection is replay-safe.

## 9. Database design

`payments_vtu_db`, schemas `payment`, `integration`; object storage for raw files. Tables: bulk files/digests, batches/versions, validation runs, items, row errors, repairs, totals and evidence refs. Unique institution/corporate/file digest per policy, batch/item references and idempotency keys; explicit amounts/currency; immutable submitted versions.

## 10. Consistency, concurrency and idempotency

Batch/version/item creation and outbox are local transactions. Workers claim bounded items. Item key derives from institution, batch version and stable row identity. Recompute aggregate projection idempotently. No distributed transaction; partial outcomes are legitimate and reconciled.

## 11. Security and privacy

Authenticate file owner, validate content rather than extension, scan/quarantine, protect payroll/beneficiary data, neutralize spreadsheet injection and enforce size/count/rate limits. Test cross-corporate access, parser bombs, tampering, duplicate upload and approval substitution.

## 12. Audit and observability

Audit file digest, parser/schema, validation, repairs, totals/fees, version transitions, approval refs and item outcomes. Metrics: validation latency/errors, queue age, item success/failure/uncertain and batch completion. Never log file rows/payroll data.

## 13. Failure, retry and recovery

Parse failure quarantines file. Worker restart resumes unprocessed items. Provider timeout remains uncertain. Partial failure never rolls back successful financial items; approved recovery/reconciliation applies. Restore verifies file digest, version, item totals/status and posting refs.

## 14. Configuration

Approve formats/schema, size/row/amount limits, duplicate window, repair rules, partial-processing policy, fee calculation, schedule, retention and accounting/provider routes.

## 15. Implementation slices

1. Formats/contracts/security. 2. File storage/parsing. 3. Validation/errors/repair versions. 4. Approval-ready totals/fees. 5. Item fan-out/status. 6. Reconciliation/load/runbooks.

## 16. Test plan

| ID | Scenario | Expected |
| --- | --- | --- |
| BPP-01 | Mixed valid/invalid rows | Stable row errors; no invalid posting |
| BPP-02 | Repair approved version | Approval invalidated/new version |
| BPP-03 | Duplicate file/item/replay | One item outcome |
| BPP-04 | Mixed item outcomes | Accurate aggregate status |
| BPP-05 | Parser/malware abuse | Quarantined/rejected |
| BPP-06 | Provider timeout | Item uncertain, no blind retry |
| BPP-07 | Cross-corporate access | Denied/audited |

## 17. Acceptance criteria

- [ ] Files validate before posting and invalid rows return reasons.
- [ ] Corporate maker-checker applies to exact batch version.
- [ ] Each item has independent status and idempotency.
- [ ] Batch aggregate accurately represents all items.

## 18. Definition of Done

Repository DoD plus approved formats/partial policy, parser security, migrations, scale/idempotency/reconciliation tests, dashboard and repair/investigation runbooks.

## 19. Open decisions

Formats, limits, duplicate policy, repair model, partial/all-or-nothing semantics, fees, schedule, retention and provider/accounting routes remain open.

## 20. Prohibited assumptions

Do not trust file extension/totals, mutate approved version, treat batch status as item truth, retry uncertain items blindly, or load entire unbounded file in memory.

## 21. Agent implementation instructions

Read payment/ledger/workflow/mandate/reconciliation epics. Run parser, migration, file-security, load, concurrency, idempotency, posting and reconciliation tests. Stop for missing format/partial-processing/accounting policy.

