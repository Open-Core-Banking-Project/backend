---
decision: P1-E05-D10
title: Reversal, partial-reversal and component policy
epic: P1-E05 Core Ledger and Financial Posting
owners: [Finance, Operations]
required_by: P1-E05-S9
status: Proposed
brief_date: 2026-11-09
related: [P1-E05-D03, P1-E05-D09, P1-E07-D14]
decisions: [P1-E05-D10]
---

# Decision brief: reversal policy

The original posting is immutable. This decision defines which new opposite postings may correct it and prevents a
provider timeout, duplicate request or concurrent operator action from producing an excess reversal.

## 1. Decision requested

| Question | Required answer |
| --- | --- |
| Eligibility | Which posted event types and final provider states may be reversed? |
| Scope | Is reversal full-only or component/amount partial for each event type? |
| Authority | Which reason, evidence, permission and approval are required? |
| Dates | Which posting/value dates and periods apply to the reversal? |
| Concurrency | How is the reversible remainder reserved and consumed? |

## 2. Recommended position

- Only a `Posted` transaction is reversible. An uncertain provider outcome is never sufficient evidence.
- The original journal and lines remain unchanged; a new transaction and opposite journal link to the original.
- Full reversal is the default. Partial reversal is allowed only when the approved event mapping names the reversible
  components and amount rules.
- The remaining reversible amount is checked and consumed under the original transaction lock in the posting
  transaction. Concurrent requests cannot exceed it.
- Principal, fee, tax and commission treatment is explicit per mapping. No component is silently retained or netted.
- Every request carries a stable reason code, evidence reference and idempotency key. Restricted reversals require an
  exact Workflow decision; the maker cannot approve their own proposal.
- A reversal posts on the current open posting date unless an approved date/period exception explicitly says otherwise;
  its value-date relationship to the original is recorded, not inferred.
- Reversal refusal is a durable, queryable outcome and never edits the original transaction state to `Failed`.

## 3. Required implementation evidence

1. Full reversal produces balanced opposite lines and an immutable original/reversal link.
2. Two concurrent reversals produce at most the approved reversible total.
3. Same idempotency key returns the same reversal; another hash conflicts.
4. Provider `Uncertain` evidence is refused.
5. Component-only reversal is refused unless the exact mapping permits it.
6. Maker self-approval, stale approval and closed-period attempts are denied and audited.

## 4. Deferred for approval

Finance and Operations must publish the event-by-event eligibility matrix, partial limits, component treatment, reason
catalogue and approval thresholds. Provider-specific finality remains owned by Payments/Integration decisions.

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending | |
| Finance | | Pending: approve eligibility and component rules | |
| Operations | | Pending: approve reasons and evidence | |

