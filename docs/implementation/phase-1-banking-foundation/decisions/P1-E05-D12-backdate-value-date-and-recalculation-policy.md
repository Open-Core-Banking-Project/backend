---
decision: P1-E05-D12
title: Backdate, value-date windows and recalculation impact
epic: P1-E05 Core Ledger and Financial Posting
owners: [Finance, Compliance]
required_by: P1-E05-S11
status: Approved
brief_date: 2026-11-09
approved_on: 2026-11-26
approved_by: Project owner
selected_option: B - 90/365-day windows with classified historical posting
related: [P1-E05-D04, P1-E05-D10, P1-E05-D13]
decisions: [P1-E05-D12]
---

# Decision brief: financial date policy

Transaction timestamp, business date, posting date and value date have different meanings. This decision prevents a
caller from using one date to disguise another or from silently changing a closed period or accrued result.

## 1. Decision requested

| Question | Required answer |
| --- | --- |
| Ordinary window | How far may a caller set a value date relative to the business date? |
| Extended history | What authority does an older value date need? |
| Posting date | Which period does a historical value date post into? |
| Future dates | May Ledger hold or post a future-dated request? |
| Recalculation | What follow-up work does a historical value date trigger? |
| Evidence | Which calendar, cut-off and approval evidence is recorded? |

## 2. Approved position

Option B was approved on 2026-11-26.

- The server records the transaction timestamp. The institution business calendar supplies the business date, and the
  open accounting period supplies the posting date.
- Ordinary posting uses the current business/posting date with a value date from 90 calendar days before the business
  date up to and including the business date.
- A value date between 91 and 365 calendar days before the business date is an exceptional historical posting. It
  requires the `ledger.backdate.post` authority, step-up authentication, a bounded reason code, an evidence reference
  and an exact approval reference scoped to institution, legal entity, event, account/product, maximum age and amount.
- A value date older than 365 calendar days is refused. It needs a `P1-E05-D13` period reopen plus explicit Finance
  approval; Ledger never backdates by default.
- An exceptional historical posting keeps its historical value date but posts on the current open posting date and
  carries an explicit prior-period classification. The earlier period is not silently reopened or rewritten.
- A posting date that falls in a closed or locked period is refused. Ledger never moves it silently to another period.
- Future value dates are refused. Scheduled instructions belong to the owning source domain; Ledger holds no request
  open and posts no journal with a future transaction timestamp.
- Cut-off and timezone come from the institution business calendar version recorded with the decision; a stale or
  ambiguous calendar version is refused.
- Every historical value date records a mandatory recalculation/reconciliation handoff naming the affected accruals,
  fees, interest, limits, statements, reports and reconciliation ranges. Ledger never rewrites an earlier posted
  result; the follow-up is explicit work owned by the named teams.
- A refusal leaves no journal, balance or movement change and records a durable audit outcome.

## 3. Options and impact

| Option | Position | Impact |
| --- | --- | --- |
| A — same business date only | No value date other than today; every correction needs a reopen. | Simplest control, but ordinary corrections and provider same-day value dating become impossible. |
| B — 90/365-day windows with classified historical posting **(approved)** | Ordinary window 90 days; 91–365 days under backdate authority; older requires reopen. | Supports real backdating and corrections while keeping the posted period history intact. Needs a published backdate authority and a recalculation handoff. |
| C — reopen required for any prior date | Any historical value date forces a period reopen. | Strongest period integrity, but every correction touches the close/reopen control and operations cannot correct same-month history without a full cycle. |

## 4. Required implementation evidence

1. Boundary tests at 90 and 91 days, and at 365 and 366 days, including the refusal path.
2. Daylight-saving/offset handling and business-date rollover using the recorded calendar version.
3. Closed and locked period refusal, and refusal of a stale calendar version.
4. Future value date refusal.
5. Exact approval binding: a changed value date, posting date, amount or evidence invalidates the approval.
6. The durable recalculation/reconciliation handoff record.
7. Every refusal leaves no journal, balance or movement behind.

## 5. Approved policy boundary

- Windows, authority scopes, reason codes and the recalculation matrix are published configuration owned by Finance and
  Compliance. Changing them is a configuration publication, not a code default.
- Product interest/accrual conventions and provider value-date rules remain outside this decision.
- Until this policy is implemented and proven, a value date outside the ordinary window is refused.

## 6. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | User | Approved Option B | 2026-11-26 |
| Finance | Project-owner approval | Approved the 90/365-day windows and the current-open-period classified treatment | 2026-11-26 |
| Compliance | Project-owner approval | Approved the evidence, approval-binding and recalculation-handoff requirements | 2026-11-26 |
