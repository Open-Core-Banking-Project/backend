---
decision: P1-E03-D11
title: Merge eligibility, freezes, required consumers and reconciliation
epic: P1-E03 Customer and Onboarding
owners:
  - Architecture
  - Operations
  - Compliance
required_by: Slice 11
status: Approved
approved_on: 2026-10-29
brief_date: 2026-10-29
related: [P1-E03-D04, P1-E03-D10]
decisions: [P1-E03-D11]
---

# Decision brief: customer merge eligibility, consumer convergence and reconciliation

## 1. Decision

| # | Question | Answer |
| --- | --- | --- |
| D11-A | Who may propose a merge? | A staff actor with the merge permission, citing both customer references, a reason and the duplicate-review evidence. The proposal computes an immutable plan hash over survivor, source, reason and required consumers |
| D11-B | When is a merge eligible? | Both customers must exist in the institution, be different, and be in a mergeable state (`active`, `restricted` or `dormant`). A `pending` onboarding, `deceased`, `closed` or `rejected` customer is never merged by this path: onboarding must finish first, and closing a pending record would misrepresent it |
| D11-C | What approval is required? | An explicit Workflow decision reference before applying. The reference is recorded on the plan and becomes the approval reference of the source customer's closing transition |
| D11-D | Can the plan change after approval? | No. Survivor, source and required consumers are frozen once approved (`merge_plan_immutable`); a change is a new request |
| D11-E | What happens to the source? | It is closed through the normal status transition with the merge reason, its history is retained, an alias redirects its identifiers to the survivor, and `CustomersMerged.v1` is published. Nothing is deleted |
| D11-F | Which consumers must acknowledge? | The consumers named on the plan, supplied as approved configuration codes by Architecture and Operations. The merge reports outstanding consumers and requires reconciliation instead of claiming completion early |
| D11-G | Does a merge move money or close accounts? | No. Account behaviour belongs to Core Banking and Ledger, which consumes the event and applies its own approved rules |

## 2. Lifecycle

`proposed → analysed → pending_approval → approved → applying → completed`, with
`applying → reconciliation_required → completed`, and `rejected` / `withdrawn` from any pre-approval state.
`completed` means every required consumer acknowledged the merge locally; otherwise the plan stays in
`reconciliation_required` and is visible to operations.

## 3. Constraints

- One active merge per source customer, enforced by a partial unique index rather than caller discipline.
- Only the proposing service applies its local part; downstream convergence is event-driven and reconciled.
- Alias rows are permanent: source identifiers keep resolving to the survivor and are never reused.
- A merge never deletes customers, KYC evidence, status history, preferences or audit records.
- The plan hash is stored and cited by events so a consumer can prove which plan it applied.

## 4. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| The launch consumer list and acknowledgement timeouts | Architecture and Operations | Requires the deployment topology and SLA |
| Account-closure preconditions for a source customer | Core Banking and Product | Owned by Core Banking and Ledger |
| Merge of legal entities or across institutions | Legal and Compliance | Outside Phase 1 scope |
| Operational runbook thresholds and escalation | Operations | Needs the operations calendar and staffing |

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved frozen plans after approval, source closure with retained history and aliases, event-driven consumer convergence and explicit reconciliation | 2026-10-29 |
| Architecture | | Named confirmation required before the consumer list is fixed | |
| Operations | | Named confirmation required before merges are enabled in production | |
