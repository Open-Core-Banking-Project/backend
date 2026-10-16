---
phase: "Phase 3 - Growth Channels"
epic: "Merchant Acquiring and Settlement"
owning_service: "Requires merchant-boundary ADR; coordinated across Customer, Payments, Risk, Integration and Reconciliation"
modules: [Merchant Onboarding, Terminals, Pricing, Reserves, Acquiring Transactions, Settlement]
status: Draft
requirements: [CHN-002, CHN-003, PSP-005..PSP-006, REC-001..REC-010, RSK-001..RSK-008]
stories: [US-063]
dependencies: [Phase 2 Corporate Onboarding Risk Reconciliation, Epic 04 Virtual Accounts, Epic 06 Cards if enabled]
adrs: [Merchant/acquiring ownership, Processor/terminal model, Pricing reserves and settlement]
last_updated: 2026-10-16
---

# Epic 07: Merchant Acquiring and Settlement

## 1. Objective

Onboard governed merchants, classify risk, inventory/assign terminals, configure approved fees/reserves/settlement accounts and reconcile settlement positions to acquiring transaction records and ledger outcomes.

## 2. Success measures

- Merchant KYC, beneficial ownership, risk category and approval precede activation.
- Terminals have unique inventory, assignment, status and processor references.
- Fees, reserves and settlement schedules are versioned, simulated and approved.
- Gross/net, fees, reserves, refunds/chargebacks where enabled and payouts reconcile.

## 3. Scope

Included: merchant/outlet references, onboarding/KYC/risk, accounts, terminals, pricing, reserves, settlement schedules/cycles, transaction ingestion and reconciliation. Excluded: invented acquiring rules, direct ledger writes, unapproved chargeback/dispute scope and processor selection.

## 4. Actors, roles and permissions

Merchant officer `merchant.onboard.manage`; risk reviewer `merchant.risk.review`; terminal officer `merchant.terminal.manage`; pricing/settlement manager `merchant.pricing.manage`; finance certifier `merchant.settlement.certify`. Sensitive changes use step-up/maker-checker and institution/merchant/outlet/terminal scope.

## 5. Domain model

Concepts: `MerchantProfile`, `Outlet`, `MerchantRiskCategory`, `Terminal`, `PricingVersion`, `ReserveRule/PositionReference`, `SettlementSchedule/Cycle/Position`, `AcquiringTransactionReference`. States merchant `Draft -> Kyc/Risk -> Approval -> Active -> Suspended/Closed`; terminal `Inventory -> Assigned -> Active -> Suspended/Retired`; settlement `Open -> Calculated -> Review -> Approved -> Paid/Reconciled`.

Invariants: merchant party/KYC remains sourced from Customer; terminal one active assignment; pricing/reserve version frozen per transaction; settlement account bank-verified; fees/reserves explicit; settlement certification blocked by material breaks; Ledger alone changes balances.

## 6. Use cases

Onboard/approve merchant; add outlet; inventory/assign terminal; publish pricing/reserve/schedule; ingest canonical transaction; calculate/review/pay settlement; reconcile processor, transaction, reserve and ledger; suspend risk/terminal safely.

## 7. API contracts

Routes: `/merchants`, `/merchants/{id}/decisions`, `/merchant-outlets`, `/terminals`, `/terminals/{id}/assign`, `/merchant-pricing`, `/merchant-settlements`, `/merchant-settlements/{id}/certifications`. Provider callbacks/files enter through Integration Hub; bounded queries mask settlement accounts.

## 8. Event contracts

Produce `MerchantActivated/Suspended`, `TerminalAssigned/StatusChanged`, `MerchantPricingPublished`, `MerchantSettlementCalculated/Certified/Paid`. Consume KYC/risk, processor transaction and ledger/reconciliation outcomes. Deduplicate external transactions/files and partition by merchant/settlement cycle.

## 9. Database design

No tables until ownership ADR. Customer owns merchant party/KYC; approved acquiring owner stores merchant operational refs, outlets, terminals, pricing/reserve/schedules and transaction refs; Audit/Reconciliation owns matches/breaks/certification. Enforce tenant IDs, unique terminal/processor refs, versioned effective rules, explicit amounts/currency and immutable transaction evidence.

## 10. Consistency, concurrency and idempotency

Local state/outbox per owner. Processor ingestion deduplicates external ID/digest. Settlement calculation is deterministic for source set/rule version. Ledger posting uses cycle/component idempotency. Breaks remain open until authoritative resolution; no distributed transaction.

## 11. Security and privacy

KYC/settlement/account data need-to-know; terminal/processor credentials in secret management; signed source input; strong approvals. Test fake merchant/terminal, settlement-account substitution, pricing tamper, replay, cross-tenant access and collusive certification.

## 12. Audit and observability

Audit onboarding/risk, terminal custody, pricing/reserve/schedule versions, settlement calculations, waivers/certification and postings. Metrics: onboarding age, terminal status, volume/value, fee/reserve/settlement, breaks and processor failures without sensitive labels.

## 13. Failure, retry and recovery

Provider uncertainty preserves explicit state. Duplicate clearing/file is ignored/evidenced. Settlement posting timeout queried/reconciled. Processor outage follows approved degraded policy. Restore verifies inventory, versions, source totals, positions and ledger refs.

## 14. Configuration

Approve merchant categories/KYC/risk, terminal types/custody, fees/taxes/reserves, settlement accounts/calendars/cutoffs, refunds/chargebacks, processor mappings, materiality and retention.

## 15. Implementation slices

1. Ownership/processor/accounting ADRs. 2. Merchant/outlet KYC/risk. 3. Terminal inventory/assignment. 4. Pricing/reserves/schedules. 5. Transactions/settlement posting. 6. Reconciliation/certification/operations.

## 16. Test plan

| ID | Scenario | Expected |
| --- | --- | --- |
| MAS-01 | Merchant KYC/risk/approval | Activation only after evidence |
| MAS-02 | Terminal assignment conflict | Rejected |
| MAS-03 | Pricing/reserve calculation | Approved version/totals |
| MAS-04 | Duplicate transaction/file | One settlement input |
| MAS-05 | Material break | Certification blocked |
| MAS-06 | Settlement posting retry | One balanced outcome |
| MAS-07 | Cross-tenant/account substitution | Denied/audited |

## 17. Acceptance criteria

- [ ] Merchant KYC, risk category and settlement schedule are recorded.
- [ ] Terminals are inventoried, assigned and controlled.
- [ ] Fees, reserves and settlement accounts are versioned/approved.
- [ ] Settlements/reserves reconcile to transactions and ledger.

## 18. Definition of Done

Repository DoD plus ownership/processor/accounting approval, migrations, KYC/risk/terminal contracts, calculation golden tests, reconciliation/certification, load/security evidence and runbooks.

## 19. Open decisions

Owner/database, acquirer/processor, merchant categories/KYC, terminal model, pricing/tax/reserve, settlement/cutoff/account, refunds/chargebacks, PCI scope and materiality remain open.

## 20. Prohibited assumptions

Do not create a tenth service, trust merchant-supplied settlement data, reuse terminal assignment, silently net unexplained differences or settle around ledger/reconciliation.

## 21. Agent implementation instructions

Stop before persistence until ownership ADR. Read corporate onboarding/risk/reconciliation/ledger contracts. Run migration, provider, calculation, duplicate, posting, reconciliation, security/load and restore tests.

