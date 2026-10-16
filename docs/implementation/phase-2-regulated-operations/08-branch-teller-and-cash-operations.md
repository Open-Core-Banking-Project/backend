---
phase: "Phase 2 - Regulated Operations"
epic: "Branch, Teller and Cash Operations"
owning_service: "Core Banking and Ledger with Operations and Workflow"
modules: [Tills, Vaults, Cash Movements, Teller Transactions, Cash Balancing]
status: Draft
requirements: [BRN-001, BRN-002, BRN-003, LED-001..LED-013, WFL-001..WFL-008]
stories: [US-055, US-056]
dependencies: [Phase 1 Institution Accounts Ledger Workflow Documents, Epic 06 Risk, Epic 07 Reconciliation]
adrs: [Branch cash accounting, Till/vault custody and dual control, Receipt and denomination policy]
last_updated: 2026-10-16
---

# Epic 08: Branch, Teller and Cash Operations

## 1. Objective

Control physical cash through authorised till/vault custody, balanced movements, teller deposits/withdrawals, limits, dual control, receipts and end-of-day reconciliation with visible overages/shortages.

## 2. Success measures

- Every till/vault has one valid session/custody state and explicit currency.
- Cash movements and customer transactions create balanced ledger postings exactly once.
- Customer/account/cash/teller/branch limits and approval policies are enforced server-side.
- Close identifies expected/declared/ledger differences and creates investigation records.

## 3. Scope

Included: till/vault setup, open/close, custody, cash transfer, deposit, withdrawal, denomination declaration if approved, dual control, receipt, balancing and investigations. Excluded: cheque clearing, counterfeit/legal procedures not approved, and direct adjustment of differences.

## 4. Actors, roles and permissions

| Actor | Permission | Scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Teller | `branch.cash.transact` | Assigned branch/till/currency | Withdrawal/high value | Limits policy |
| Branch supervisor | `branch.till.manage` | Branch/tills | Required | Dual control where configured |
| Vault custodian | `branch.vault.manage` | Assigned vault/currency | Required | Dual control |
| Cash investigator | `branch.cash_exception.resolve` | Assigned difference | Required | Adjustment approval |

Teller cannot operate an unassigned/closed till or approve their own restricted transfer/adjustment.

## 5. Domain model

Concepts: `Till`, `Vault`, `CashSession`, `CustodyAssignment`, `CashMovement`, `TellerTransaction`, `CashDeclaration`, `CashDifference`, `CashInvestigation`, `ReceiptReference`.

States: till session `OpeningPending -> Open -> ClosingPending -> Balanced/ClosedWithDifference -> Closed`; movement `Requested -> ApprovalPending -> PostingPending -> Completed/Failed/Uncertain`; customer transaction preserves explicit posting state.

Invariants: one active session per till/currency; custody required; no negative physical position unless approved model permits; movement source/destination and journal balance; ledger is financial authority while declared physical cash is separately reconciled; differences cannot be erased; transaction receipts reflect authoritative status.

## 6. Use cases

- Open till from approved vault transfer and assign custodian.
- Transfer cash till-to-till or vault-to-till under dual control.
- Accept cash deposit after validating customer/account and limits.
- Process cash withdrawal after funds, restriction, risk and cash checks.
- Close/count till, identify difference, open investigation and certify branch cash.

## 7. API contracts

Routes: `POST /branches/{branchId}/tills/{id}/sessions`, `POST /cash-movements`, `POST /teller-transactions/deposits`, `POST /teller-transactions/withdrawals`, `POST /till-sessions/{id}/close`, `GET /branches/{id}/cash-position`, `POST /cash-differences/{id}/actions`. Financial commands require correlation/idempotency and return pending/uncertain explicitly.

## 8. Event contracts

Produced: `TillOpened`, `CashMovementRequested/Completed`, `CashDepositAccepted`, `CashWithdrawalCompleted`, `TillCloseDifferenceDetected`, `TillClosed`, `CashExceptionResolved`. Consume account/restriction/risk/workflow/ledger outcomes. Order by till/session/transaction as needed; inbox/outbox mandatory.

## 9. Database design

Database `core_ledger_db`; new approved `branch_cash` schema plus ledger references. Tables: tills, vaults, custody assignments, sessions, movements, teller transactions, declarations, differences, investigation refs and receipt refs. Unique active-session/till/currency constraint, explicit decimals/currency, optimistic transitions, unique idempotency/business/posting refs, append-only movements/differences. Workflow case details remain in `operations_workflow_db` by reference.

## 10. Consistency, concurrency and idempotency

Use row/concurrency controls for till sessions/positions and limits. Never hold DB transaction over ledger/risk calls. Local request/outbox commits, then authoritative posting converges. Operation keys scope institution/branch/till/type/client key. Concurrent withdrawals cannot spend the same till/customer availability.

## 11. Security and privacy

Enforce workstation/device, teller, branch, till, customer/account and amount context. Use step-up/dual control and session timeouts. Mask customer/account values. Test forced browsing, till reassignment, limit bypass, receipt forgery, replay, cross-branch/tenant action and supervisor self-approval.

## 12. Audit and observability

Audit custody, open/close declarations, movements, approvals, deposits/withdrawals, overrides, differences, investigations and receipts. Metrics: till/vault positions, transaction failures/uncertainty, limit rejection, session age, difference count/value and closing delay. Alerts cover unclosed sessions, large/aged differences and posting/reconciliation failure.

## 13. Failure, retry and recovery

Ledger timeout creates uncertain state and blocks duplicate cash handover until enquiry/runbook resolution. Printer/notification failure does not reverse a posted transaction; receipt can be reissued with same reference. Close failure resumes from checkpoint. Restore reconciles cash sessions/movements against ledger.

## 14. Configuration

Approve branch/till/vault/currency setup, limits, dual-control thresholds, hours/business date, denomination rules, receipt template, difference materiality, SLA and accounting mappings.

## 15. Implementation slices

1. Cash accounting/custody ADRs and permissions.
2. Till/vault/session persistence and opening.
3. Controlled movements and ledger integration.
4. Deposit/withdrawal with risk/limit checks and receipts.
5. Close, difference, workflow and reconciliation.
6. Concurrency, recovery, dashboards and operational rehearsal.

## 16. Test plan

| ID | Scenario | Expected |
| --- | --- | --- |
| BTC-01 | Open/close balanced till | Custody and balanced evidence |
| BTC-02 | High-value vault movement | Independent dual control |
| BTC-03 | Deposit retry | One posting/receipt reference |
| BTC-04 | Concurrent withdrawals | Limits/funds preserved |
| BTC-05 | Ledger timeout after handover decision | Explicit uncertainty; no blind retry |
| BTC-06 | Over/short declaration | Investigation remains visible |
| BTC-07 | Cross-branch/till access | Denied/audited |
| BTC-08 | Receipt delivery failure | Reissue, no financial reversal |

## 17. Acceptance criteria

- [ ] Tills/vaults, custody, opening, limits, transfers and close are controlled.
- [ ] Cash deposits/withdrawals validate customer/account/cash/teller scope.
- [ ] All cash outcomes use balanced, idempotent ledger postings, and cash and ledger positions update consistently.
- [ ] Receipts and overage/shortage investigations are traceable.

## 18. Definition of Done

Repository DoD plus approved cash accounting/policy, migration/concurrency tests, permission/device controls, reconciliation, receipt contracts, dashboards and branch runbooks.

## 19. Open decisions

Cash chart, currencies/denominations, custody, physical handover moment, limits, dual control, hours, difference/materiality, receipt, insurance/security and accounting mappings remain open.

## 20. Prohibited assumptions

Do not equate physical declaration with ledger balance, allow shared teller identity, hide differences, print success before authoritative outcome, or retry uncertain withdrawal/deposit blindly.

## 21. Agent implementation instructions

Read ledger/account/workflow/risk/reconciliation epics and approved cash policy. Keep branch cash and postings inside Core Banking boundaries; workflow stores references. Run database/concurrency/idempotency/security/posting/reconciliation/EOD/recovery tests. Stop on unresolved cash handover/accounting policy.

