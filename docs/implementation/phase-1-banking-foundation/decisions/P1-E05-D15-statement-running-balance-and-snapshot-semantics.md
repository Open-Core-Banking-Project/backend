---
decision: P1-E05-D15
title: Statement running-balance and snapshot semantics
epic: P1-E05 Core Ledger and Financial Posting
owners: [Finance, Product]
required_by: P1-E05-S13
status: Proposed
brief_date: 2026-11-09
related: [P1-E04-D13, P1-E10-D12, P1-E10-D13]
decisions: [P1-E05-D15]
---

# Decision brief: authoritative statement data

Documents may render a statement, but Ledger must define the authoritative rows, ordering and running-balance snapshot.
Without these semantics, the same postings can produce different opening and closing balances.

## 1. Decision requested

Approve period boundaries, inclusion/finality rules, deterministic ordering, opening/closing balance derivation, treatment
of backdated/value-dated/reversed items and snapshot consistency.

## 2. Recommended position

- A query identifies institution, ledger account, explicit ISO currency, inclusive start business date and exclusive end
  business date. Institution timezone/calendar version is recorded.
- Posted movements are ordered by posting date, committed transaction timestamp, journal sequence and stable movement
  identifier. Display order never relies on database insertion order.
- Opening balance is the authoritative posted ledger balance immediately before the first included movement; each
  running balance applies the signed effect defined by D05; closing balance equals opening plus included movements.
- Reversals are separate rows linked to the original. The original is never removed or rewritten.
- Transaction timestamp, posting date and value date are all returned. The approved display/grouping date is explicit.
- Pending, rejected, failed and uncertain items are excluded from posted totals. If Product chooses to display them,
  they appear in a separate non-posted section and never affect running balance.
- The query uses one database snapshot or a persisted high-water mark so pagination/retry cannot mix different ledger
  states. The response carries that snapshot reference and a deterministic integrity digest.
- Backdated value dates do not reorder an already posted accounting movement unless the approved presentation policy
  explicitly selects value-date grouping; any such view still reconciles to posting-order totals.

## 3. Required implementation evidence

Tests cover empty periods, boundary timestamps, same-time postings, pagination, reversal, backdated value date, snapshot
stability under concurrent posting, opening/movement/closing reconciliation and tenant/account authorisation.

## 4. Deferred for approval

Finance/Product must decide the customer display/grouping date, pending-item presentation, narration/component detail,
maximum period and whether historical statements use on-demand snapshots or retained signed statement snapshots.

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending | |
| Finance | | Pending: approve balance and ordering semantics | |
| Product | | Pending: approve display and pending-item policy | |

