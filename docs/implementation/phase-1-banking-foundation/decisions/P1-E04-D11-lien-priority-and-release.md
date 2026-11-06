---
decision: P1-E04-D11
title: Lien priority, partial use and release authority
epic: P1-E04 Products and Accounts
owners:
  - Legal/Finance
  - Core operations
  - Architecture
required_by: P1-E04-S11
status: Ready for review
brief_date: 2026-11-06
related: [ADR-0015, P1-E04-D10, P1-E04-D13]
decisions: [P1-E04-D11]
---

# Decision brief: lien lifecycle

`CreateAccountLienRequestV1` already carries `Amount`, `PriorityCode` and `AuthorityReference`, and the ledger will
compute available balance from liens. Priority ordering, partial satisfaction and who may release a legal claim are
unapproved, so the availability answer is currently undefined.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D11-A | What does `PriorityCode` mean and how is it ordered? | Deterministic ordering across competing claims |
| D11-B | May a lien be partially used or partially released? | Outstanding amount model and available-balance arithmetic |
| D11-C | What is the relationship between lien amount, balance and availability? | Clamping, negative-availability behaviour and approval |
| D11-D | Who may release a lien, and in what increments? | Permissions, approval and evidence |
| D11-E | How is the lien currency constrained? | Validation rules |
| D11-F | What happens when the ledger lags the control change? | Fail-closed policy and consistency evidence |

## 2. Why the answer is needed now

- Account invariant 9 requires an explicit amount and currency, and the guard in `ACC-UC-04` requires the amount to be
  positive and the currency to match the account, but nothing fixes ordering or partial release.
- A lien is the only control that changes the customer's spendable money without denying a posting, so the arithmetic
  has to be agreed before availability is computed anywhere.
- Legal claims are released on instruction, often in instalments; modelling each lien as all-or-nothing would force a
  new lien per instalment and destroy the audit trail of one claim.

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Lien contracts | Transport-only: create, response, `AccountLienChangedV1`, decision exposure via `EvaluateAccountControls` | `AccountControlsV1.cs` |
| Lien domain and storage | **Not implemented** | Core Ledger Products module |
| Availability arithmetic | **Not implemented.** No balance rows exist; contracts keep `Holds` and `Liens` as separate amounts | `AccountBalanceSummaryV1` |
| Ledger enforcement | **Not implemented**; the Ledger module is `P1-E05` | — |
| Lien permission | Not defined; `account.lien.manage` proposed in `P1-E04-D10` | — |

## 4. Recommended position

### D11-A Priority

| Item | Recommendation |
| --- | --- |
| Vocabulary | Allow-listed codes: `statutory_first`, `tax`, `institution`, `customer`. Codes are registered in code, not free text |
| Rank | Each lien carries an integer `priorityRank` within its code; lower rank is applied first |
| Ordering | `(codeRank, priorityRank, effectiveFrom, lienId)` — deterministic and stable across restarts |
| Uniqueness | Two active liens may share a rank; the tie breaks on `effectiveFrom` then `lienId`, never on creation order |
| Purpose of order | Determines which lien is satisfied first when funds become available, and the order reported in summaries; it does **not** change available balance, which sums all active liens |

### D11-B Partial use and partial release

| Item | Recommendation |
| --- | --- |
| Original amount | Immutable. `Amount` on the lien is the claimed amount at creation and is never edited |
| Outstanding | Tracked separately as `outstandingAmount`, reduced only by recorded satisfaction or partial release entries |
| Partial release | Supported, requires its own entry with amount, authority reference, reason and approval; the lien stays active while `outstandingAmount > 0` |
| Satisfaction | A ledger-side settlement against the lien records the consumed amount; the Account module never infers satisfaction from a balance |
| Full release | Sets `outstandingAmount = 0` and closes the lien with a final entry; history is retained |
| Prohibited | Editing `Amount`, deleting a lien, computing outstanding from balances, and treating partial release as a new lien |

### D11-C Amount, balance and availability

| Item | Recommendation |
| --- | --- |
| Creation validation | Amount > 0, currency = account currency, effective period valid, authority reference present for statutory and tax codes |
| Lien above availability | Permitted but flagged: the lien is created with `exceedsAvailable = true` and requires an explicit acknowledgement from the approver; a statutory lien may exceed the balance, a customer-requested lien may not exceed the ledger balance |
| Availability floor | `available = max(0, ledgerBalance − holds − Σ outstanding liens)`; availability is never reported negative, and the shortfall is reported as `uncoveredLienAmount` |
| No ledger arithmetic here | The Account module never computes available balance for a response; it reports what the ledger returned plus its own lien total for reconciliation |
| Rounding | Minor-unit arithmetic only, with the precision approved in `P1-E04-D03`; no rounding inside the availability formula |

### D11-D Release authority

| Item | Recommendation |
| --- | --- |
| Permissions | `account.lien.create`, `account.lien.release`; release is a separate permission from creation |
| Maker-checker | Release of `statutory_first`, `tax` or `institution` liens requires a second approver and an authority reference; the maker of the lien cannot release it |
| Customer liens | A single authorised officer may create and release a customer-requested lien under policy; the default requires the same officer only when the amount is below the configured threshold |
| Legal hold | A lien marked `statutory_first` with an external order cannot be released without a new authority reference; an internal expiry never releases it |
| Audit | Every create, partial release, satisfaction and full release writes one audit row with actor, permission, amount, authority reference, correlation id and prior/after outstanding amounts |

### D11-E Currency

| Item | Recommendation |
| --- | --- |
| Rule | A lien's currency must equal the account currency in Phase 1 |
| Multi-currency | Not supported; a cross-currency claim is refused with `lien_currency_mismatch` rather than converted |
| Rationale | Conversion would invent an exchange rate and a valuation date, neither of which is approved |

### D11-F Ledger lag and consistency

| Item | Recommendation |
| --- | --- |
| Version handshake | Creation and release submit `KnownControlVersion`; a mismatch returns `409` with the current version, no partial write |
| Availability read | A summary must present `BalanceSourceV1` (ledger reference, snapshot version, as-of); a summary without a source is refused |
| Stale source | For operations that depend on availability, a source older than the approved freshness ceiling denies with `balance_source_stale` |
| Pending event | An unacknowledged `AccountLienChangedV1` is visible as a pending outbox row and appears in the reconciliation report with age |
| Fail-closed | If the ledger cannot confirm lien state for a high-risk instructing operation, refuse rather than assume applied |

## 5. Constraints

- Do not convert currencies, infer a rate or accept a cross-currency lien in Phase 1.
- Do not compute availability in the Account module, and do not report a negative available balance.
- Do not edit a lien amount, delete a lien, or model instalments as separate liens.
- Do not release a statutory lien on expiry alone.
- Do not infer lien satisfaction from a balance movement; satisfaction is an explicit recorded fact.
- This decision approves lien semantics and authority only; it does not approve account control precedence
  (`P1-E04-D10`) or closure conditions (`P1-E04-D13`).

## 6. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Minor-unit precision and rounding used in availability | Finance/Product | `P1-E04-D03` remains open |
| The threshold below which a single officer may release a customer lien | Operations/Risk | Institution policy; configured, not hard-coded |
| Interest or fees accruing on a claimed amount | Legal/Finance | Out of scope for Phase 1 lien enforcement |
| Cross-currency claims | Legal/Finance | Requires an approved valuation policy |

## 7. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending: approve, amend or reject the positions in §4 | |
| Legal/Finance | | Pending: confirm priority vocabulary, partial release and statutory release authority | |
| Architecture | | Pending: confirm the availability formula and version handshake | |

## 8. Implementation notes (for slice 11)

1. Implement the lien aggregate with immutable `amount`, mutable `outstandingAmount`, append-only entries and an
   explicit state machine (`PendingApproval → Active → Released | Satisfied | Expired`).
2. Register the priority vocabulary and rank ordering in domain code; refuse unknown codes at contract validation.
3. Implement partial release and satisfaction entries with approvals, and expose the entries in the response history.
4. Add reconciliation findings `lien_outstanding_mismatch` and `lien_event_unacknowledged` with age.
5. Emit `lien_currency_mismatch`, `lien_amount_exceeds_balance`, `lien_release_authority_required` and
   `balance_source_stale` as stable codes.
6. Prove with tests: partial releases reduce outstanding without changing the original amount; a statutory lien cannot
   be released without an authority reference; a cross-currency lien is refused; availability never goes negative; a
   stale `KnownControlVersion` returns one conflict and writes nothing.
