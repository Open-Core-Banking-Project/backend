---
decision: P1-E05-D12
title: Backdate, value-date windows and recalculation impact
epic: P1-E05 Core Ledger and Financial Posting
owners: [Finance, Compliance]
required_by: P1-E05-S11
status: Proposed
brief_date: 2026-11-09
related: [P1-E05-D04, P1-E05-D10, P1-E05-D13]
decisions: [P1-E05-D12]
---

# Decision brief: financial date policy

Transaction timestamp, business date, posting date and value date have different meanings. This decision prevents a
caller from using one date to disguise another or from silently changing a closed period or accrued result.

## 1. Decision requested

Finance and Compliance must approve date windows by event type, the authority required outside a normal window, and the
downstream recalculation/reconciliation work triggered by a historical value date.

## 2. Recommended position

- The server records transaction timestamp. The institution calendar supplies business date. The open accounting
  period supplies posting date. A caller may request value date only where its event mapping permits it.
- Ordinary posting uses the current business/posting date and a value date inside the approved event window.
- A posting date in a closed or locked period is refused; it is never moved silently to another period.
- An exceptional historical posting uses the current open posting date with the approved historical value date and a
  clear prior-period classification, unless Finance explicitly approves reopen under D13.
- Backdate authority is scoped by institution, legal entity, event, account/product, maximum age and amount. It requires
  reason, evidence, step-up and an exact approval reference.
- Future value dates are scheduled instructions owned by the source domain; Ledger does not hold a request open or post
  a journal with a future transaction timestamp.
- Recalculation is explicit: affected accruals, fees, interest, limits, statements, reports and reconciliation ranges are
  recorded as required follow-up work. Ledger never rewrites an earlier posted result.
- Cut-off and timezone use the institution business calendar version recorded with the decision.

## 3. Required implementation evidence

Boundary tests cover daylight-saving/offset handling, business-date rollover, maximum age, closed period, future date,
stale calendar version, exact approval binding and the durable recalculation handoff. Every refusal leaves no journal.

## 4. Deferred for approval

Owners must publish the actual windows, cut-offs, eligible event types, approval thresholds and recalculation matrix.
Product interest/accrual conventions remain outside this decision.

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending | |
| Finance | | Pending: approve windows and accounting treatment | |
| Compliance | | Pending: approve evidence and historical limits | |

