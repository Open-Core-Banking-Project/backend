---
phase: "Phase 2 - Regulated Operations"
epic: "Loan Servicing, Collateral and Recovery"
owning_service: "Requires lending-boundary ADR; coordinated with Core Banking and Ledger"
modules: [Repayment Schedules, Loan Servicing, Collateral, Delinquency, Collections, Recovery]
status: Draft
requirements: [LND-005, LND-006, LND-007, LND-008, LED-001..LED-013, WFL-001..WFL-008]
stories: [US-040, US-041, US-042]
dependencies: [Epic 04 Lending Products and Origination, Phase 1 Ledger, Epic 07 Reconciliation]
adrs: [Lending ownership, Schedule and allocation policy, Collateral and recovery policy]
last_updated: 2026-10-16
---

# Epic 05: Loan Servicing, Collateral and Recovery

## 1. Objective

Service approved loans through reproducible schedules, idempotent collections, payoff, collateral/guarantor controls, delinquency, restructuring and recovery while preserving separated principal, interest, fee and penalty accounting.

## 2. Success measures

- Schedule versions reconcile to contract terms and ledger postings.
- Auto-debit, partial repayment and early settlement post once and allocate by approved policy.
- Collateral retains valuation, revaluation, insurance, lien, substitution/release and loan-to-value history.
- Delinquency actions, promises, restructuring, write-off and recovery preserve debt/history and approvals.

## 3. Scope

Included: schedules, due items, repayment allocation, auto-debit, payoff quotes, settlement, collateral, guarantor commitments, arrears buckets, promises, activities, restructuring/refinancing, write-off and recovery. Excluded: invented collection law, asset-sale execution or external agency contracts.

## 4. Actors, roles and permissions

| Actor | Permission | Scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Servicing officer | `lending.loan.service` | Assigned loan/branch | Sensitive actions | Policy based |
| Credit officer | `lending.collateral.manage` | Loan/collateral | Required | Release/substitution approval |
| Collections officer | `lending.collection.manage` | Assigned portfolio | Required for settlement | Policy based |
| Recovery approver | `lending.recovery.approve` | Amount/stage | Required | Maker-checker |
| Scheduler identity | `lending.repayment.execute` | Due instructions | Workload identity | Pre-approved policy |

## 5. Domain model

Concepts: `LoanContract`, immutable `ScheduleVersion`, `Installment`, `RepaymentInstruction`, `RepaymentAllocation`, `PayoffQuote`, `CollateralAsset`, `Valuation`, `InsuranceCover`, `CollateralLien`, `GuarantorCommitment`, `DelinquencyCase`, `PromiseToPay`, `RestructureProposal`, `WriteOffDecision`, `RecoveryReceipt`.

State examples: loan `Active -> InArrears -> Restructured/Recovery -> Settled/WrittenOff`; collateral `Proposed -> Perfected -> Active -> ReleasePending -> Released`; repayment `Due -> CollectionPending -> Paid/PartPaid/Failed/Uncertain`.

Invariants: schedule changes create versions; allocation components remain explicit; ledger is balance authority; write-off does not delete debt/recovery history; collateral release requires eligible exposure and approval; valuation history is append-only; one repayment instruction has one accounting outcome.

## 6. Use cases

- Generate/version schedule and due items.
- Execute auto-debit or record authorised manual repayment.
- Produce expiring payoff quote and settle early.
- Register/value/insure/perfect/substitute/release collateral and guarantor commitments.
- Move arrears through approved buckets and record activities/promises.
- Approve restructuring/refinancing/write-off and record later recoveries.

## 7. API contracts

Routes include `GET /loans/{id}/schedule`, `POST /loans/{id}/repayments`, `POST /loans/{id}/payoff-quotes`, `POST /loans/{id}/settle`, `/loans/{id}/collateral`, `/collateral/{id}/valuations`, `/loans/{id}/promises`, `/loans/{id}/restructures`, and `/loans/{id}/write-offs`. Every command declares a specific lending permission, concurrency token and idempotency key where financial.

Errors distinguish stale schedule/quote, allocation conflict, period lock, insufficient funds, lien/insurance defect, LTV violation, approval required and uncertain posting.

## 8. Event contracts

Produced: `LoanScheduleGenerated`, `RepaymentDue`, `LoanRepaymentRequested`, `LoanRepaymentApplied`, `LoanSettled`, `CollateralValued`, `CollateralReleaseRequested`, `LoanBecameDelinquent`, `PromiseToPayRecorded`, `LoanRestructured`, `LoanWrittenOff`, `RecoveryReceived`. Consume ledger/account/risk/workflow outcomes. Order by loan/collateral; outbox/inbox mandatory.

## 9. Database design

Persistence waits for the lending-boundary ADR. Candidate owned tables cover loan contracts, schedule versions/installments, due items, instructions/allocations, payoff quotes, collateral/assets/valuations/insurance/liens, guarantor commitments, delinquency buckets/cases, activities/promises, restructures, write-offs and recoveries. Monetary precision/currency, effective dates, immutable history, idempotency uniqueness and tenant scope are mandatory. Ledger entries remain references.

## 10. Consistency, concurrency and idempotency

Lock/version loan state during schedule or allocation decisions; do not hold transactions across ledger/network calls. Auto-debit key includes loan/installment/due occurrence. Repayment convergence uses authoritative ledger outcome. Concurrent collections allocate once. Cross-service inconsistency becomes a visible reconciliation break.

## 11. Security and privacy

Need-to-know access for credit, guarantor, collateral and recovery information. Protect field-visit/location/legal notes and documents. Enforce branch/portfolio assignment and conflict rules. Test cross-tenant access, unauthorised release/write-off, quote manipulation, duplicate debit and evidence leakage.

## 12. Audit and observability

Audit schedule versions, allocations, quotes, valuations, liens, releases, activities, promises, restructures, write-offs, recoveries and approvals. Metrics: due/arrears amounts, bucket ageing, failed/uncertain debit, expired insurance/valuation, LTV exceptions, promise outcomes and reconciliation breaks; restrict sensitive labels.

## 13. Failure, retry and recovery

Auto-debit timeout remains uncertain until enquiry/reconciliation. Scheduler restarts from durable checkpoints. Collateral provider failure blocks affected action, not history. Write-off/restructure failure cannot partially post. Restore verifies schedules, ledger refs, collateral state and job checkpoints.

## 14. Configuration

Version/effective-date allocation order, grace, buckets, penalty, auto-debit, payoff, restructuring, write-off, valuation/LTV, insurance, promise SLA and accounting rules.

## 15. Implementation slices

1. Ownership ADR and schedule model.
2. Due items, repayment allocation and ledger integration.
3. Payoff/early settlement.
4. Collateral, guarantors, valuation and lien controls.
5. Delinquency, activities, promises and escalation.
6. Restructure/write-off/recovery, reconciliation and operations.

## 16. Test plan

| ID | Scenario | Expected |
| --- | --- | --- |
| LSR-01 | Schedule from approved terms | Reproducible version |
| LSR-02 | Duplicate auto-debit | One posting/allocation |
| LSR-03 | Partial repayment | Approved component allocation |
| LSR-04 | Early payoff | Expiring quote and balanced settlement |
| LSR-05 | Concurrent collateral release | One approved transition |
| LSR-06 | LTV/insurance violation | Release blocked |
| LSR-07 | Write-off | Debt/recovery history preserved |
| LSR-08 | Recovery after write-off | Explicit balanced recovery posting |
| LSR-09 | Cross-tenant case | Denied/audited |

## 17. Acceptance criteria

- [ ] Principal, interest, fees and penalties remain separated.
- [ ] Scheduled/partial/early repayments are idempotent and ledger-backed.
- [ ] Collateral and guarantor history, LTV and approvals are enforced.
- [ ] Delinquency, promises, restructuring, write-off and recovery are traceable.

## 18. Definition of Done

Repository DoD plus approved servicing/accounting rules, schedule golden tests, migration/concurrency evidence, reconciliation, dashboards, collections/collateral runbooks and UAT.

## 19. Open decisions

Ownership, allocation/day-count, delinquency buckets, penalty, grace, payoff, collateral valuation/perfection, LTV, insurance, restructure, write-off, legal recovery and accounting mappings remain open.

## 20. Prohibited assumptions

Do not invent collection/legal policy, overwrite schedules/valuations, release collateral automatically, delete debt on write-off, trust client payoff totals or retry uncertain debits blindly.

## 21. Agent implementation instructions

Read Epic 04 and approved lending/accounting policies. Keep postings in Core Banking and Ledger. Run schedule/calculation, database, concurrency, idempotency, security, reconciliation and recovery tests. Stop for unresolved ownership or financial policy.

