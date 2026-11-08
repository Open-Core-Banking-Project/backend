---
decision: P1-E05-D03
title: Phase 1 accounting event catalogue and mapping content
epic: P1-E05 Core Ledger and Financial Posting
owners:
  - Finance
  - Domain owners
required_by: P1-E05-S1
status: Proposed
brief_date: 2026-11-08
related: [P1-E05-D02, P1-E05-D04, P1-E05-D09, ADR-0016]
decisions: [P1-E05-D03]
---

# Decision brief: Phase 1 accounting event catalogue

A caller submits a canonical accounting event, never journal lines. What an event *is*, which components it may carry
and which ledger roles it resolves are unapproved, and the epic forbids both an implicit mapping and a suspense
fallback. This brief proposes the event set the ledger accepts in Phase 1 and the shape of the mapping content that
turns an event into a balanced journal.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D03-A | Which canonical events exist in Phase 1? | The accepted event codes and their versions |
| D03-B | What does a mapping version declare? | The configuration content and its simulation |
| D03-C | How does a caller supply components? | The request contract and what is refused |
| D03-D | How is a mapping approved and made effective? | Lifecycle, maker-checker and dating |
| D03-E | What happens with no effective mapping, or an ambiguous one? | Fail-closed behaviour and codes |
| D03-F | How are new events added after Phase 1? | Configuration publication, not code release |

## 2. Why the answer is needed now

- Slice 3 builds the mapping-version lifecycle and its simulation; slice 6 posts the first journal against it.
- Every later epic (payments, VTU, channels, statements) posts through these events, so the vocabulary has to exist
  before they are written.
- The epic's non-negotiable invariant 10 requires a normal posting to resolve an effective approved mapping for the
  transaction dates and event, which presupposes a catalogue.

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Mapping tables and lifecycle | **Not implemented** | No `ledger.mapping` area or table |
| Event codes | **Not implemented**; no posting contracts exist | `CoreBanking.CoreLedger.Contracts` |
| Configuration lifecycle to reuse | Implemented, four areas already published through it | `P1-E01`, `P1-E04` |
| Approval profiles with pinned hashes | Implemented | `AccountOpeningApprovalV1`, `SavingsEligibilityApprovalV1` |
| Callers that will post | Account module today; payments and VTU in `P1-E07` | Epic documents |

## 4. Recommended position

### D03-A The Phase 1 event set

Each code carries an explicit version, so a later change is a new version rather than an edit.

| Event code | Meaning | Balancing effect | Components |
| --- | --- | --- | --- |
| `customer_deposit.v1` | Funds credited to a customer account | Debit `cash_settlement`, credit `customer_deposit` | Principal |
| `customer_withdrawal.v1` | Funds debited from a customer account | Debit `customer_deposit`, credit `cash_settlement` | Principal |
| `internal_transfer.v1` | Move between two accounts in the institution | Debit source `customer_deposit`, credit destination `customer_deposit` | Principal |
| `fee_charge.v1` | Fee charged to a customer | Debit `customer_deposit`, credit `fee_income` | Fee, tax where applicable |
| `fee_waiver.v1` | Approved reversal of a charged fee | Debit `fee_income`, credit `customer_deposit` | Fee, authority reference |
| `commission_charge.v1` | Commission charged and owed onward | Debit `customer_deposit`, credit `commission_payable` | Commission |
| `penalty_charge.v1` | Penalty charged to a customer | Debit `customer_deposit`, credit `penalty_income` | Penalty |
| `interest_credit.v1` | Interest credited to a customer | Debit `interest_expense`, credit `customer_deposit` | Interest |
| `reversal.v1` | Approved reversal of an eligible posted transaction | Exact opposite of the original component set | Mirrors the original |
| `adjustment.v1` | Controlled correction with explicit evidence | Declared per adjustment, always balanced | Declared per case |
| `hold_capture.v1` | A hold becomes a posting | The capture amount posts; the hold reduces by the same amount | Principal |

Anything not in this table is refused. The catalogue is closed in Phase 1 and open only through D03-F.

### D03-B Mapping content

| Field | Rule |
| --- | --- |
| Event code and version | Exactly one mapping version per event version per institution and legal entity |
| Supported scope | Institution and legal entity; a mapping never spans entities |
| Input components | The declared component types and their required/optional status, with validation |
| Role resolvers | Named roles from `P1-E05-D02` only, never a code |
| Debit/credit template | Which role is debited and which credited for each component |
| Currency | Declared per mapping; cross-currency handling stays prohibited |
| Date policy | Which of business, posting and value date the mapping consumes |
| Rounding reference | The approved rounding version from `P1-E05-D04` |
| Reversal eligibility | Whether the event may be reversed, and the reversible basis |
| Evidence requirements | Upstream approval, risk or control references that must be present |

### D03-C Caller input

| Item | Recommendation |
| --- | --- |
| Caller supplies | Event code and version, principal and component amounts with currency, source and destination references, dates, correlation and idempotency key |
| Caller never supplies | GL codes, ledger account ids for internal roles, final totals, approval truth or a claimed balance |
| Component validation | A missing required component refuses; an undeclared component refuses; a zero or negative component refuses |
| Derived values | Fees, taxes and commissions are calculated by the owning module and supplied as explicit components; the ledger never computes a price |

### D03-D Approval and effective dating

| Item | Recommendation |
| --- | --- |
| Area | `ledger.mapping`, schema `ledger.mapping.v1`, through the shared configuration lifecycle |
| Simulation | Synthetic journals per declared component set, proving balance per scope and currency, plus the expected rejections |
| Approval | Workflow approval pinned to the exact content hash, maker excluded, published effective-dated with no overlap |
| Rollback | A new version, never an edit; the superseded version keeps its evidence |

### D03-E Failure behaviour

| Condition | Answer |
| --- | --- |
| No effective mapping at the business date | `accounting_mapping_missing` and no posting |
| More than one effective mapping | `accounting_mapping_ambiguous` and no posting |
| Mapping inactive or superseded | `accounting_mapping_not_effective` and no posting |
| Declared role unresolvable | `ledger_role_unknown` or `ledger_role_ambiguous`, per `P1-E05-D02` |
| Component set unmatched | `accounting_component_invalid` and no posting |
| Any of the above | Never fall back to suspense, cash or a previously effective version |

### D03-F Adding events later

A new event or event version is a mapping publication with its own approval. It does not change the domain code, the
posting contract shape or the database schema. A new *component type* is a code change and requires a decision
extension, because it changes the accounting vocabulary itself.

## 5. Constraints

- Do not accept arbitrary journal lines for a normal business event.
- Do not compute fees, taxes, interest or commissions inside the ledger.
- Do not reuse a superseded mapping to keep an integration working.
- Do not add cross-currency events before FX accounting is approved.
- Do not treat this catalogue as the Phase 2 interest or FX catalogue.
- This brief does not approve the chart (`D02`), precision (`D04`), the sign convention (`D05`), the available-funds
  formula (`D06`) or the component accounting detail (`D09`).

## 6. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Provider settlement and interchange events | Payments/VTU owners | `P1-E07`, posted through the same mechanism |
| Interest accrual and capitalisation events | Finance | Phase 2 scope, `P1-E05-D15` for statement semantics |
| Dispute and chargeback events | Operations | `P1-E06` case workflow |
| Which adjustment reasons exist | Finance | Code list is policy data |

## 7. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending | |
| Finance | | Pending: confirm the event set and the component sets | |
| Domain owners | | Pending: confirm the callers' component obligations | |

## 8. Implementation notes (for slices 3, 6 and 10)

1. Model the mapping as configuration content with a strict allow-listed schema, parsed back through the same shape
   that produced its hash.
2. Resolve roles through the effective chart version and mapping version for the business date before the transaction,
   then revalidate inside it.
3. Build journals from the mapping template, never from caller lines, and assert balance per scope before insert.
4. Emit stable refusal codes and record the refused attempt as audit evidence without creating a journal.
5. Prove with tests: a missing mapping refuses, an ambiguous mapping refuses, an undeclared component refuses, a
   superseded mapping refuses, a waived fee posts as a contra component, and a replayed posting returns the original
   journal id.
