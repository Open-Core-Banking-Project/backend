---
decision: P1-E04-D13
title: Closure prerequisites and permitted post-close events
epic: P1-E04 Products and Accounts
owners:
  - Finance/Operations
  - Compliance
required_by: P1-E04-S12
status: Ready for review
brief_date: 2026-11-06
related: [ADR-0015, P1-E04-D06, P1-E04-D10, P1-E04-D11, P1-E04-D12, P1-E04-D15]
decisions: [P1-E04-D13]
---

# Decision brief: account closure

`ACC-UC-06` requires authoritative settled-balance evidence, no prohibitive controls and an approved closure policy
before an account becomes `Closed`, and forbids the Account module from forcing a balance to zero. What counts as
settled, which post-close operations remain legal and what happens to residual money are unapproved.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D13-A | What proves the balance condition for closure? | The authoritative source, its freshness and its shape |
| D13-B | Which control and obligation states prohibit closure? | The precondition list and its failure codes |
| D13-C | What is the ordering of proposal, check, approval and transition? | Concurrency and race safety |
| D13-D | Which operations may post after closure? | The ledger's post-close allow-list |
| D13-E | Is reopening permitted? | Terminality of `Closed` and number reuse |
| D13-F | How is residual money or interest handled? | The sweep dependency and its owner |

## 2. Why the answer is needed now

- Closure is the only transition that makes an account unusable for normal business; a wrong closure strands a
  customer's money and a missed precondition creates a legal exposure.
- The account module holds no money, so "zero balance" has to come from the ledger with a version and an as-of time,
  or it is a display value being used as a financial authority — explicitly prohibited by the epic.
- Post-close posting rules decide whether a late settlement, a chargeback or a correction can still be honoured. If
  nothing may post, the bank cannot correct its own error.

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Closure domain and proposal | **Not implemented.** `AccountStatusProposalRequestV1` exists as a transport record for status proposals | `AccountLifecycleV1.cs` |
| Ledger balances | **Not implemented**; the Ledger module is `P1-E05`. `AccountBalanceSummaryV1` already separates ledger balance, available balance, holds and liens and carries `BalanceSourceV1` | `AccountLifecycleV1.cs` |
| Control model | Contract-only; precedence proposed in `P1-E04-D10` | `AccountControlsV1.cs` |
| Statement requests | Contract-only; periods and retention proposed in `P1-E04-D15` | `StatementRequestsV1.cs` |
| Ledger post-close policy | **Not implemented**; `AccountPostingContextV1` carries `AccountStatus` and `LedgerInitialisationStatus` for the ledger to decide with | `AccountLifecycleV1.cs` |

## 4. Recommended position

### D13-A Balance evidence

| Item | Recommendation |
| --- | --- |
| Source | A ledger-provided closure-condition response carrying ledger balance, available balance, holds, liens, pending settlements and `BalanceSourceV1` (ledger reference, snapshot version, as-of) |
| Settled definition | Ledger balance is zero **and** there are no pending settlements, unapplied credits, holds or outstanding lien amounts |
| Freshness | The response must be within the approved freshness ceiling; a stale response refuses with `balance_source_stale` |
| Prohibited | Using a cached summary, an analytics figure, a display balance or a customer-supplied statement as the closure condition; and the Account module writing any balance or journal to achieve zero |
| Evidence | The proposal records the ledger reference, snapshot version and as-of used for the decision |

### D13-B Prohibiting states

| Condition | Effect |
| --- | --- |
| Any active or suspended legal block or regulatory freeze | Refuse with `closure_legal_control_active` |
| Any active `statutory_first` or `tax` lien | Refuse with `closure_lien_outstanding` |
| Outstanding institution or customer lien | Refuse unless the closure payload includes a release decision; the lien is released first, then closure re-checks |
| Active holds or pending settlements | Refuse with `closure_funds_not_settled` |
| Open dispute or chargeback | Refuse with `closure_dispute_open` |
| Positive or negative ledger balance | Refuse with `closure_balance_not_settled`; residual money is a sweep, not a closure side effect |
| Required notice or final statement pending | Refuse with `closure_notice_pending` |
| Account already `Closed` | Idempotent return of the existing closure, not an error |

### D13-C Ordering

| Step | Recommendation |
| --- | --- |
| 1 | `POST closure proposal` with reason and optional supporting reference creates a `PendingClosure` proposal without changing the account status |
| 2 | Conditions are checked against an authoritative ledger snapshot and recorded with the proposal |
| 3 | The proposal is submitted for approval bound to the exact proposal version and the condition snapshot hash |
| 4 | On approval the account moves to `PendingClosure` and ordinary debits are denied |
| 5 | A final condition re-check runs immediately before the transition, per the ADR-0015 precedent for account opening |
| 6 | `Closed` is committed with status history, audit and the outbox fact in one transaction |
| Concurrency | The account's optimistic version plus a per-account closure lease; a stale condition snapshot forces a re-check, never a silent proceed |

### D13-D Post-close posting

| Operation class | Recommendation |
| --- | --- |
| Ordinary debit or credit | Refused with `account_closed` |
| Correction of a bank error | Permitted, requires a distinct classified operation code, an authority reference and approval |
| Late settlement of a transaction posted before closure | Permitted, classified, and it may leave a non-zero balance that is then swept |
| Chargeback or dispute settlement | Permitted, classified, with evidence |
| Fee or interest accrual after the closure date | Refused; accrual must stop at the closure date |
| Lien or control creation | Refused except a legal block, which must always be recordable |

The allow-list is registered in code as a fixed set of classified operation codes; the ledger refuses anything else
against a `Closed` account.

### D13-E Reopening and numbers

| Item | Recommendation |
| --- | --- |
| Reopening | Not permitted in Phase 1. A returned customer opens a new account with a new number |
| Alternative | An exceptional policy decision may later define reopening; it is not implied here |
| Number | A closed account's number is never reused, consistent with `P1-E04-D06` |
| Customer record | Closure does not delete the customer, its history, statements or audit trail |

### D13-F Residual money

| Item | Recommendation |
| --- | --- |
| Residual credit balance at closure | Must be settled before closure; the settlement (transfer or payout) is a ledger operation with its own approval and evidence, referenced by the closure proposal |
| Residual debit balance | Refuse closure; the debt is handled by a collections or write-off policy outside this epic |
| Dormant and close | A dormant account must be reactivated or closed through the same preconditions; dormancy never bypasses closure checks |
| Interest to the closure date | Accrued and capitalised before closure by the ledger's own schedule; the Account module never computes it |

## 5. Constraints

- Do not close on a cached, derived or customer-supplied balance, and do not force a balance to zero.
- Do not delete or archive away the account, its history, its statements or its audit rows on closure.
- Do not allow an ordinary posting after closure, and do not permit an unclassified exception.
- Do not reopen a closed account, and do not reuse its number.
- Do not treat dormancy as a closure shortcut.
- This decision approves closure conditions only; it does not approve the ledger sweep mechanics (Ledger epic) or
  statement retention (`P1-E04-D15`).

## 6. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| The ledger sweep and payout contract | Finance/Core Ledger | Owned by the Ledger epic, not present in Phase 1 |
| Notice periods and final-statement obligations | Compliance | Jurisdiction and product specific |
| Write-off and collections policy for residual debits | Finance | Outside this epic |
| Whether reopening may ever be permitted | Product/Legal | Recommended: not in Phase 1 |

## 7. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending: approve, amend or reject the positions in §4 | |
| Finance/Operations | | Pending: confirm the settled-balance definition and residual-money rule | |
| Compliance | | Pending: confirm the post-close allow-list and notice obligations | |

## 8. Implementation notes (for slice 12)

1. Implement the closure proposal aggregate with a recorded condition snapshot (ledger reference, version, as-of) and
   an approval bound to the exact proposal version.
2. Implement the condition port as a ledger call returning the settled-state facts; no local arithmetic.
3. Implement the final re-check and the single transactional `Closed` transition with history, audit and outbox fact.
4. Register the classified post-close operation codes in domain code and refuse everything else for a `Closed` account.
5. Emit `closure_balance_not_settled`, `closure_funds_not_settled`, `closure_legal_control_active`,
   `closure_lien_outstanding`, `closure_dispute_open`, `closure_notice_pending` and `balance_source_stale`.
6. Add reconciliation findings `closed_account_with_residual_balance` and `closure_without_condition_snapshot`.
7. Prove with tests: closure refuses with a lien outstanding; a stale snapshot forces a re-check; a replayed closure
   returns the existing result; an ordinary posting is refused after closure while a classified correction posts; the
   number is never reissued.
