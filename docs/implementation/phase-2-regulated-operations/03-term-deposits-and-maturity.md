---
phase: "Phase 2 - Regulated Operations"
epic: "Term Deposits and Maturity"
owning_service: "Core Banking and Ledger"
modules: [Term Deposit Products, Deposit Contracts, Accrual, Maturity]
status: Draft
requirements: [ACC-001, ACC-002, ACC-007, ACC-008, ACC-010, LED-001..LED-013]
stories: [US-024]
dependencies: [Phase 1 Products and Accounts, Phase 1 Core Ledger and Posting, Phase 1 Workflow]
adrs: [Deposit calculation policy, Maturity processing, Tax and accounting mappings]
last_updated: 2026-10-16
---

# Epic 03: Term Deposits and Maturity

## 1. Objective

Create and service fixed/term deposits with approved tenure, interest, withholding tax, funding, maturity, rollover and premature-liquidation rules, with every monetary outcome posted through the protected ledger.

## 2. Success measures

- Product simulation and contract schedules reproduce the approved product/rule version.
- Funding occurs once and activates only after authoritative posting.
- Accrual, capitalisation, tax, penalty, payout and rollover journals balance and reconcile.
- Maturity jobs restart safely without duplicate contracts or postings.

## 3. Scope

Included: approved term-deposit product versions, quotation, contract creation, funding, accrual, maturity instruction, payout/rollover, premature liquidation, certificates and reconciliation. Excluded: traded securities, treasury dealing, bespoke tax/legal rules not approved in a country pack.

## 4. Actors, roles and permissions

| Actor | Permission | Scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Product manager | `deposit.term_product.manage` | Institution/product | Required to publish | Maker-checker |
| Service/treasury officer | `deposit.term_contract.create` | Customer/account/product | Funding step-up | Policy based |
| Customer channel | `deposit.term_contract.request` | Owned accounts | Strong auth | Product policy |
| Approver | `deposit.term_contract.approve` | Amount/product/branch | Required | Cannot self-approve |
| Operations officer | `deposit.term_maturity.manage` | Assigned exceptions | Required | Controlled override |

## 5. Domain model

| Concept | Responsibility | Key rules |
| --- | --- | --- |
| `TermDepositProductVersion` | Tenure, rate, accrual, tax, penalty, eligibility | Immutable after publication |
| `TermDepositQuote` | Time-bound simulation | References exact rule version |
| `TermDepositContract` | Principal, dates, instruction and lifecycle | Explicit currency; one funding outcome |
| `AccrualSchedule` | Expected interest periods | Approved day-count/rounding |
| `MaturityInstruction` | Payout/principal rollover/principal+interest rollover | Versioned and cutoff controlled |

State: `Draft -> PendingApproval -> FundingPending -> Active -> Matured -> PaidOut/RolledOver`; `LiquidationPending -> Liquidated`; failures remain explicit.

Invariants: amount/currency never inferred; product version is frozen per contract; ledger alone changes balances; journals balance; posted accruals/funding/payouts are immutable; rollover creates a linked new contract; liquidation never rewrites accrued history.

## 6. Use cases

- Simulate a term deposit without committing state.
- Create/approve and fund a contract idempotently.
- Run periodic accrual/capitalisation under business-date controls.
- Change future maturity instruction before approved cutoff.
- Mature into payout or rollover exactly once.
- Quote and execute approved premature liquidation with explicit penalty/tax components.

## 7. API contracts

| Method | Route | Permission | Idempotency |
| --- | --- | --- | --- |
| POST | `/api/v1/term-deposits/simulations` | `deposit.term_contract.request` | Query/input digest |
| POST | `/api/v1/term-deposits` | `deposit.term_contract.create` | Required key |
| GET | `/api/v1/term-deposits/{id}` | `deposit.term_contract.view` | Query |
| PUT | `/api/v1/term-deposits/{id}/maturity-instruction` | `deposit.term_contract.update` | Version token |
| POST | `/api/v1/term-deposits/{id}/liquidation-quotes` | `deposit.term_contract.liquidate` | Quote request key |
| POST | `/api/v1/term-deposits/{id}/liquidate` | `deposit.term_contract.liquidate` | Required key |

Stable errors cover stale quote/product, eligibility, funds, cutoff, period lock, approval, already matured and currency mismatch.

## 8. Event contracts

Produced: `TermDepositCreated`, `TermDepositFundingRequested`, `TermDepositActivated`, `InterestAccrued`, `TermDepositMatured`, `TermDepositRolledOver`, `TermDepositPaidOut`, `TermDepositLiquidated`. Consumed: authoritative ledger posting outcomes, product publication and account restriction events. Order by contract ID; deduplicate posting outcome ID.

## 9. Database design

Database `core_ledger_db`; schemas `product`, `account`, `processing`, `integration`.

Tables: `product.term_deposit_rules`, `account.term_deposit_contracts`, `term_deposit_instruction_versions`, `term_deposit_quotes`, `processing.term_deposit_schedules`, `interest_accruals`, `maturity_runs`, `maturity_run_items`. Unique contract/business reference and contract/operation idempotency; explicit decimal precision/currency; dates separated; contract/version and posting references immutable.

Migrations are additive. Existing eligible product vocabulary is promoted through approved publication/backfill; never infer live contracts from Phase 1 product rows.

## 10. Consistency, concurrency and idempotency

Contract transitions use optimistic concurrency. Local state/outbox commit precedes ledger response convergence. Funding/accrual/maturity/liquidation keys include contract and operation period/type. Scheduler claims bounded items and resumes from checkpoints. Reconciliation resolves cross-service uncertainty; no distributed transaction.

## 11. Security and privacy

Validate account ownership/authority, product eligibility, amount/limit, step-up and maker-checker server-side. Never trust client interest, tax, penalty or payout totals. Mask account identifiers; test IDOR, cross-tenant access, quote tampering, replay and privileged override.

## 12. Audit and observability

Audit product/rule version, quote, contract, instruction changes, approvals, posting refs, overrides and liquidation reason. Metrics: funding pending age, accrual/maturity backlog, failures, payout/rollover counts and reconciliation exceptions. Trace through ledger and notifications; no sensitive account data in labels/logs.

## 13. Failure, retry and recovery

Ledger timeout yields `FundingUncertain`/operation-pending and status enquiry, never blind resubmission. Failed maturity items stop dependent actions and resume from checkpoints. Rollback uses reversals/adjustments, not deletion. Restore verifies contracts, schedules, ledger references and job checkpoints.

## 14. Configuration

Version, approve and effective-date tenure, rate/bands, day-count, accrual/capitalisation, rounding, tax, penalty, minimum/maximum, eligibility, cutoff and accounting mappings. Exact values are business decisions.

## 15. Implementation slices

1. Approved product/rule and simulation contracts.
2. Contract persistence, permissions and approval.
3. Funding and authoritative ledger convergence.
4. Accrual schedules and checkpointed processing.
5. Maturity, payout and rollover.
6. Premature liquidation, reconciliation, observability and runbooks.

## 16. Test plan

| ID | Scenario | Expected |
| --- | --- | --- |
| TDM-01 | Simulation against rule version | Reproducible schedule/totals |
| TDM-02 | Funding retry | One balanced funding posting |
| TDM-03 | Concurrent maturity workers | One terminal outcome |
| TDM-04 | Principal rollover | Linked new contract; balanced postings |
| TDM-05 | Principal+interest rollover | Correct explicit components |
| TDM-06 | Premature liquidation | Approved penalty/tax and reversal-safe history |
| TDM-07 | Ledger timeout | Explicit uncertainty and enquiry |
| TDM-08 | Cross-tenant/account request | Denied and audited |
| TDM-09 | Job restart | No duplicate accrual/maturity posting |

## 17. Acceptance criteria

- [ ] Approved product versions drive interest, tax and penalty calculation.
- [ ] Payout, principal rollover and principal-plus-interest rollover work idempotently.
- [ ] All funding/accrual/maturity/liquidation outcomes use balanced ledger postings.
- [ ] Schedules, contracts, postings and statements reconcile.

## 18. Definition of Done

Repository DoD plus approved calculation/accounting specifications, migration evidence, golden calculation tests, concurrency/replay tests, reconciliation, dashboards and runbooks.

## 19. Open decisions

Product ownership, day-count, compounding, rounding, tax, value dates, cutoff, penalty, partial liquidation, rate change, EOD order and accounting mappings require approval.

## 20. Prohibited assumptions

Do not invent rates/taxes, use floating point, calculate trusted totals in clients, mutate posted accruals, auto-retry uncertain funding or treat rollover as an in-place rewrite.

## 21. Agent implementation instructions

Read Phase 1 product/ledger/workflow epics and accounting specifications. Keep all authoritative money changes in Core Banking and Ledger. Run calculation, migration, database, concurrency, posting, reversal, contract and end-to-end tests. Stop if any calculation or accounting rule is unresolved.

