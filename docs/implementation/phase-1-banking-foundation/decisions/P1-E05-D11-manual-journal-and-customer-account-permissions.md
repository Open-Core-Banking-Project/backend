---
decision: P1-E05-D11
title: Manual-journal and customer-account permissions
epic: P1-E05 Core Ledger and Financial Posting
owners: [Finance, Security]
required_by: P1-E05-S10
status: Proposed
brief_date: 2026-11-09
related: [P1-E05-D02, P1-E05-D03, P1-E06-D12]
decisions: [P1-E05-D11]
---

# Decision brief: manual financial authority

Manual journals are the only Phase 1 path that may name ledger accounts directly. That exceptional power needs a
smaller authority surface than ordinary posting, especially where a customer ledger account is involved.

## 1. Decision requested

Approve the permitted account classes, contextual scopes, approval thresholds, prohibited combinations and emergency
process for manual journals and adjustments.

## 2. Recommended position

- Separate permissions are required to draft, submit, approve and post; no actor may perform more than the approved
  combination, and a maker never approves their own request.
- All manual journals require step-up authentication, reason code, structured evidence, correlation ID and an exact
  content hash bound to the Workflow decision.
- Direct customer-account lines are denied by default. They require an explicit `customer_account_adjustment` class,
  customer/account scope, a named compensating account and enhanced Finance approval.
- Cash, settlement, tax, suspense and control-account classes require separate grants; a general permission does not
  imply access to them.
- The approved chart controls which account-class combinations balance. Closed, dormant or restricted customer
  accounts still pass the current account-control decision.
- An approved request is immutable. A changed line, amount, date, currency, narrative or evidence creates a new request.
- Posting remains subject to balance, currency, period, idempotency and account-state validation after approval.
- Break-glass authority cannot bypass balancing or immutability and must create independent security evidence.

## 3. Prohibited shortcuts

- No free-text-only reason, wildcard institution scope or arbitrary customer-account access.
- No approval inferred from a role name or from the person who drafted the journal.
- No direct SQL, balance arithmetic update, edit/delete after posting or suspense fallback.
- No unbounded narrative containing customer or secret data.

## 4. Required implementation evidence

Tests must prove exact-hash approval, maker-checker separation, cross-tenant and out-of-scope denial, customer-account
default denial, closed-period refusal, balanced atomic posting, idempotent replay and immutable audit/outbox evidence.

## 5. Deferred for approval

Finance and Security must provide the account-class matrix, amount thresholds, approval quorum, reason catalogue,
evidence retention and break-glass authority. Until then, manual posting remains unavailable.

## 6. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending | |
| Finance | | Pending: approve account-class and threshold matrix | |
| Security | | Pending: approve grants, step-up and break-glass controls | |

