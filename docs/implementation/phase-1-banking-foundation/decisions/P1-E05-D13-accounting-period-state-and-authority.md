---
decision: P1-E05-D13
title: Accounting-period close, lock, reopen state and authority
epic: P1-E05 Core Ledger and Financial Posting
owners: [Finance]
required_by: P1-E05-S11
status: Approved
brief_date: 2026-11-09
approved_on: 2026-11-26
approved_by: Project owner
selected_option: B - four states with maker-checker reopen
related: [P1-E05-D12, P1-E05-D14]
decisions: [P1-E05-D13]
---

# Decision brief: accounting periods

Period state is a financial control, not a display flag. It must be checked inside posting transactions and cannot be
changed merely to force a rejected journal through.

## 1. Decision requested

| Question | Required answer |
| --- | --- |
| States | Which period states exist and which transitions are legal? |
| Posting rules | What may post in each state? |
| Close | Which evidence and authority does a close need? |
| Reopen | Who may reopen, for how long, and what follows? |
| Lock | May a locked period ever reopen? |
| Order | May a later period close while an earlier one is open? |

## 2. Approved position

Option B was approved on 2026-11-26.

```text
Open -> SoftClosed -> Closed -> Locked
          |             |
          +-> Open      +-> Reopened -> Closed
```

- `Open` accepts approved ordinary mappings. `SoftClosed` accepts only event codes on the period's published allow-list.
  The allow-list is empty by default, so a soft-closed period refuses every posting until Finance publishes one.
- `Closed` and `Locked` reject every posting. A posting into either state is refused and never moved to another period.
- A close requires balanced debit/credit totals, no blocking reconciliation exception, the completed required processing
  steps, outbox health evidence and an immutable close summary. One Finance approver performs the close.
- A reopen requires two distinct Finance approvers with maker separation, step-up authentication, a reason and an
  evidence reference. It is legal-entity scoped, names the allowed event categories, and expires after five business
  days; the period auto-recloses at expiry and a post-reclose reconciliation is recorded.
- `Locked` is never reopened by an ordinary application action. Only a dual-control break-glass with independent
  Security evidence and a Finance head approval may reopen it, and that action is separately audited.
- A later period is not closed while a required earlier period remains open or reopened, unless Finance explicitly
  approves that calendar rule for the legal entity.
- State transitions use optimistic concurrency and an idempotency key. A posting locks and rechecks the exact current
  period state inside its database transaction, so a concurrent close and post cannot both succeed.
- Business-date advancement and period close remain separate decisions joined by the `P1-E05-D14` EOD completion gates.

## 3. Options and impact

| Option | Position | Impact |
| --- | --- | --- |
| A — Open/Closed only | Two states; reopen is another close operation. | Simple, but no soft-close window and no distinction between a reversible close and a hard lock. |
| B — four states with maker-checker reopen **(approved)** | Open/SoftClosed/Closed/Locked with a time-bounded two-approver reopen and a break-glass-only locked exit. | Matches financial close practice and keeps every state change evidenced. Needs a published allow-list, reopen window and reconciliation procedure. |
| C — Open/Closed/Locked with no reopen | A closed period is final; corrections always post in the current period. | Strongest immutability, but a genuine prior-period error has no controlled path. |

## 4. Required implementation evidence

1. The state graph and every illegal transition refused.
2. Transition replay under the same idempotency key.
3. Concurrent close/post contention: exactly one wins and the loser is refused.
4. Soft-close allow-list enforcement, including the empty default.
5. Reopen maker-checker: self-approval denied and one approval insufficient.
6. Reopen expiry and automatic reclose with the post-reclose reconciliation.
7. Locked-state denial, and the dual-control break-glass exception with its own evidence.
8. Exact close and reclose balancing evidence.

## 5. Approved policy boundary

- Calendar granularity, soft-close allow-lists, close blockers, reopen windows and reason codes are published
  configuration owned by Finance; changing them is a configuration publication, not a code default.
- Locked-period emergency reopening is a Security-and-Finance dual-control action, never a general Finance permission.
- Until this policy is implemented and proven, no period may be closed, reopened or locked through the application.

## 6. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | User | Approved Option B | 2026-11-26 |
| Finance | Project-owner approval | Approved the four-state graph, one-approver close, two-approver reopen, five-business-day reopen window and empty soft-close default | 2026-11-26 |
