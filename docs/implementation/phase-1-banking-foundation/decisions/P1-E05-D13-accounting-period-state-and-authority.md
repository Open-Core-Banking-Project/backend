---
decision: P1-E05-D13
title: Accounting-period close, lock, reopen state and authority
epic: P1-E05 Core Ledger and Financial Posting
owners: [Finance]
required_by: P1-E05-S11
status: Proposed
brief_date: 2026-11-09
related: [P1-E05-D12, P1-E05-D14]
decisions: [P1-E05-D13]
---

# Decision brief: accounting periods

Period state is a financial control, not a display flag. It must be checked inside posting transactions and cannot be
changed merely to force a rejected journal through.

## 1. Decision requested

Approve the state machine, close evidence, permitted posting categories in each state, reopen authority and the
relationship between period close and business-date advancement.

## 2. Recommended position

```text
Open -> SoftClosed -> Closed -> Locked
          |             |
          +-> Open      +-> Reopened -> Closed
```

- `Open` accepts approved ordinary mappings. `SoftClosed` accepts only an explicit allow-list. `Closed` and `Locked`
  reject postings; `Locked` cannot be reopened through an ordinary application action.
- Close requires balanced debit/credit totals, no blocking reconciliation exception, completed required processing
  steps, outbox health evidence and an immutable close summary.
- State transitions use optimistic concurrency and an idempotency key. Posting locks and rechecks the exact current
  period state inside its database transaction.
- Reopen is time-bounded, legal-entity scoped, reasoned, step-up protected and maker-checker approved. It names the
  allowed event categories and produces a post-reclose reconciliation.
- A later period is not closed while a required earlier period remains open or reopened, unless Finance explicitly
  approves that calendar rule.
- Business-date advancement and period close are separate decisions joined by the approved EOD completion gates.

## 3. Required implementation evidence

Tests prove the state graph, transition replay, concurrent close/post contention, soft-close allow-list, self-approval
denial, reopen expiry, locked-state denial and exact close/reclose balancing evidence.

## 4. Deferred for approval

Finance must publish calendar granularity, soft-close event allow-lists, close blockers, approval quorum, reopen window
and locked-period emergency authority.

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending | |
| Finance | | Pending: approve state graph and close/reopen authority | |

