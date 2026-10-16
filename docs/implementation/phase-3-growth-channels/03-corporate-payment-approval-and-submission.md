---
phase: "Phase 3 - Growth Channels"
epic: "Corporate Payment Approval and Submission"
owning_service: "Operations and Workflow with Payments and VTU"
modules: [Mandate Evaluation, Corporate Approval, Validated Submission]
status: Draft
requirements: [CHN-005, PAY-005..PAY-015, IAM-004..IAM-007, WFL-001..WFL-008]
stories: [US-060]
dependencies: [Epic 01 Corporate Mandates, Epic 02 Bulk Payments, Phase 1 Payments and Workflow]
adrs: [Corporate signing evaluation, Approval snapshot binding]
last_updated: 2026-10-16
---

# Epic 03: Corporate Payment Approval and Submission

## 1. Objective

Let authorised corporate approvers review validation results and fees, satisfy current account/amount/mandate/user signing rules, and submit single, bulk or payroll payments idempotently.

## 2. Success measures

- Approval binds exact payment/batch version, validation result, fees, account, amount and currency.
- Single/joint/multi-level mandate, user/account/amount limits and conflicts are enforced.
- Content change invalidates prior approval.
- Completed approval triggers one payment submission and retains independent financial status.

## 3. Scope

Included: approval snapshot, signer eligibility, signing sequence/quorum, decision/rejection, expiry, invalidation and submission authorization. Excluded: file parsing (Epic 2), payment/provider execution internals and assuming workflow completion means financial success.

## 4. Actors, roles and permissions

Initiator uses `corporate.payment.initiate`; approver `corporate.payment.approve`; corporate admin cannot implicitly approve; bank operator `corporate.payment.override` only under explicit policy. All scoped by institution, corporate, account, mandate, amount, currency and payment type with step-up.

## 5. Domain model

Concepts: `CorporatePaymentApprovalRequest`, immutable `ApprovalSnapshot`, `SignerRequirement`, `ApprovalDecision`, `SubmissionAuthorization`. State `Pending -> PartiallyApproved -> Approved -> SubmissionPending -> Submitted`, with `Rejected`, `Expired`, `Invalidated`, `SubmissionUncertain`.

Invariants: maker cannot approve; current signer/grant/status revalidated; one signer counts once per rule; snapshot digest changes invalidate decisions; fees/validation visible before decision; one authorization submits once; approval never posts balance.

## 6. Use cases

Create approval from validated payment; calculate signers/quorum; approve/reject with step-up; invalidate on change/revocation/expiry; issue single-use submission authorization; converge on payment outcome.

## 7. API contracts

Routes: `POST /corporate-payment-approvals`, `GET /.../{id}`, `POST /.../{id}/decisions`, `POST /.../{id}/submit`, and work-queue query. Responses show safe validation/fee summary, outstanding signer requirement and payment status. Stable errors cover stale mandate, signer conflict, limit, changed content and uncertainty.

## 8. Event contracts

Produce `CorporatePaymentApprovalRequested`, `CorporatePaymentDecisionRecorded`, `CorporatePaymentApproved/Invalidated`, `CorporatePaymentSubmissionAuthorized`. Consume mandate/user changes and payment outcomes. Outbox/inbox and ordering by approval/payment are mandatory.

## 9. Database design

Approval state resides in `operations_workflow_db`; Payments owns payment/batch. Store snapshot digest/content references, mandate version, signer requirements, decisions, authorization and payment reference. Unique approver/requirement decision and one authorization per approved snapshot; append-only decisions; tenant scope/concurrency required.

## 10. Consistency, concurrency and idempotency

Decisions use optimistic concurrency. Workflow transaction commits decision/outbox; Payments validates signed authorization, mandate freshness and idempotency. Retries return prior decision/submission. No distributed transaction; submission pending/uncertain is visible.

## 11. Security and privacy

Step-up and transaction-signing context as approved; prevent self-approval, collusion outside mandate, replay, approval swapping, stale grants and cross-corporate access. Show minimum beneficiary/payroll data necessary.

## 12. Audit and observability

Audit snapshot, validation/fee refs, mandate/user versions, signer requirements, decisions, invalidation and submission/payment refs. Metrics: approval age, rejections, stale/invalidation, quorum completion and submission uncertainty.

## 13. Failure, retry and recovery

IAM/mandate unavailable denies or leaves pending per policy. Payment timeout becomes submission uncertain and is queried. Replay cannot duplicate decisions/submission. Restore reconciles approved authorizations to payment outcomes.

## 14. Configuration

Approve signing rule semantics, order/quorum, step-up, limits, expiry, delegation, holidays/cutoffs, override and notification policy.

## 15. Implementation slices

1. Snapshot/mandate contract. 2. Eligibility/quorum engine. 3. Decision API/concurrency. 4. Invalidation. 5. Single-use submission. 6. Audit/reconciliation/runbooks.

## 16. Test plan

| ID | Scenario | Expected |
| --- | --- | --- |
| CPA-01 | Single/joint/multi-level approval | Exact current rule enforced |
| CPA-02 | Initiator self-approves | Denied |
| CPA-03 | Amount/account/user limit exceeded | Denied/escalated |
| CPA-04 | Payload/fee/mandate changes | Approval invalidated |
| CPA-05 | Concurrent/replayed decisions | Count once |
| CPA-06 | Submission retry/timeout | One instruction, explicit uncertainty |
| CPA-07 | Cross-corporate approval | Denied/audited |

## 17. Acceptance criteria

- [ ] Amount, account, mandate and user limits are enforced.
- [ ] Approvers see validation results and fees before decision.
- [ ] Current signing rules and maker-checker govern all payment types.
- [ ] Approved payments submit idempotently with explicit outcome.

## 18. Definition of Done

Repository DoD plus mandate engine tests, snapshot/invalidation contracts, migration/concurrency evidence, payment reconciliation, dashboards and uncertainty runbook.

## 19. Open decisions

Signing semantics/order, transaction signing, delegation, expiry, cutoffs, fee-change behavior and bank override require approval.

## 20. Prohibited assumptions

Do not infer authority from role/title, reuse approval after content change, count one signer twice, expose payroll data broadly or equate approval with posting.

## 21. Agent implementation instructions

Read Epics 1/2 and Phase 1 payment/workflow. Keep workflow authorization separate from payment outcome. Run mandate, concurrency, invalidation, security, idempotency and E2E tests. Stop for unresolved signing semantics.

