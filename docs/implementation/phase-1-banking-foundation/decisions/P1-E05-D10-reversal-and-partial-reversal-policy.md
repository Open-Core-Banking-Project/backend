---
decision: P1-E05-D10
title: Reversal, partial-reversal and component policy
epic: P1-E05 Core Ledger and Financial Posting
owners: [Finance, Operations]
required_by: P1-E05-S9
status: Proposed
brief_date: 2026-11-26
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

## 3. Options and impact

| Option | Position | Impact |
| --- | --- | --- |
| A — full-only reversals | Every eligible posting is reversed in full, including every component selected by its approved mapping. | Smallest and safest Phase 1 implementation, but operations cannot correct a settled partial amount without a separate approved adjustment flow. |
| B — mapping-gated partial reversals **(recommended)** | Full reversal remains the default. An approved event mapping may opt specific principal/fee/tax/commission components into partial reversal and define their limits. | Supports legitimate partial corrections while keeping accounting treatment explicit. Requires Finance to publish an event-by-event eligibility/component matrix and Operations to publish reason/evidence/approval rules before implementation. |
| C — caller-selected partial reversals | The caller chooses any amount or component up to the remaining original amount. | Most flexible, but it permits callers to invent accounting treatment and can strand fees, taxes or commissions. This conflicts with the protected posting boundary and is rejected. |

Approving Option B authorises only the reversal mechanics described here. It does not approve provider finality,
backdating, closed-period posting, arbitrary journal lines or a production accounting mapping. Without the Finance and
Operations matrices named below, P1-E05-S9 remains blocked and no reversal API or posting path may be exposed.

## 4. Required implementation evidence

1. Full reversal produces balanced opposite lines and an immutable original/reversal link.
2. Two concurrent reversals produce at most the approved reversible total.
3. Same idempotency key returns the same reversal; another hash conflicts.
4. Provider `Uncertain` evidence is refused.
5. Component-only reversal is refused unless the exact mapping permits it.
6. Maker self-approval, stale approval and closed-period attempts are denied and audited.

## 5. Approval inputs required

Finance must select an option and, for Option B, publish the event-by-event eligibility matrix, partial limits and
principal/fee/tax/commission treatment. Operations must approve the reason catalogue, evidence requirements and
approval thresholds. Provider-specific finality remains owned by Payments/Integration decisions and cannot be inferred
from this approval.

## 6. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending | |
| Finance | | Pending: approve eligibility and component rules | |
| Operations | | Pending: approve reasons and evidence | |

