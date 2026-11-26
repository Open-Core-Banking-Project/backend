---
decision: P1-E05-D11
title: Manual-journal and customer-account permissions
epic: P1-E05 Core Ledger and Financial Posting
owners: [Finance, Security]
required_by: P1-E05-S10
status: Approved
brief_date: 2026-11-09
approved_on: 2026-11-26
approved_by: Project owner
selected_option: B - four-permission maker-checker with tiered thresholds
related: [P1-E05-D02, P1-E05-D03, P1-E06-D12]
decisions: [P1-E05-D11]
---

# Decision brief: manual financial authority

Manual journals are the only Phase 1 path that may name ledger accounts directly. That exceptional power needs a
smaller authority surface than ordinary posting, especially where a customer ledger account is involved.

## 1. Decision requested

| Question | Required answer |
| --- | --- |
| Authority | Who may draft, submit, approve and post a manual journal? |
| Account classes | Which account classes need their own grant, and which may a general grant reach? |
| Customer accounts | May a manual journal ever post to a customer ledger account in Phase 1? |
| Thresholds | How many approvers does each amount tier require? |
| Evidence | Which reason, evidence, retention and immutability rules apply? |
| Emergency | What may break-glass authority do, and what may it never do? |

## 2. Approved position

Option B was approved on 2026-11-26.

- Four separate permissions: `ledger.manual_journal.draft`, `ledger.manual_journal.submit`,
  `ledger.manual_journal.approve` and `ledger.manual_journal.post`. A general ledger permission implies none of them.
- One actor may hold draft and submit together. The approver must be a different stable actor from both the drafter and
  the submitter across roles, sessions and channels. The approver posts the approved content when it also holds
  `ledger.manual_journal.post`; posting never changes the approved request.
- All manual journals require step-up authentication, a bounded reason code, structured evidence reference, correlation
  ID and an exact content hash bound to the Workflow decision.
- Direct customer-account lines are denied by default. They require an explicit `customer_account_adjustment` class,
  institution/customer/account scope, a named compensating account and the enhanced approval tier.
- Cash, settlement, tax, suspense, control and income/expense classes each require their own class grant; a general
  `ledger.manual_journal.*` grant does not imply access to them.
- The approved chart controls which account-class combinations balance. Closed, dormant or restricted customer
  accounts still pass the current account-control decision.
- An approved request is immutable. A changed line, amount, date, currency, narrative or evidence reference creates a
  new request, new hash and new approval.
- Posting remains subject to balance, currency, accounting-period, idempotency and account-state validation after
  approval. A manual journal may not post into a closed or locked period; that needs the separate period decision.
- Break-glass authority cannot bypass balancing, immutability, period, currency or idempotency checks and must create
  independent security evidence.

### Approval thresholds

Amounts are in the institution's approved currency; no conversion or cross-currency manual journal is permitted.

| Manual journal amount | Required approval |
| --- | --- |
| Up to and including 100,000 | One `ledger.manual_journal.approve` actor who is not the maker |
| Above 100,000 and up to 1,000,000 | Two distinct approvers who are neither the maker nor each other |
| Above 1,000,000 | Two distinct approvers, one of whom holds the Finance head role |

A customer-account line always uses at least the two-approver tier regardless of amount.

### Reason, evidence and retention

- Reason codes are a bounded catalogue owned by Finance and published through the approved configuration lifecycle.
  Free text is allowed only as a bounded evidence reference, never as the only reason.
- Every manual journal, its approval binding, the posted result and any refusal are immutable audit evidence.
- Evidence is retained under the approved audit-retention policy. Until the retention decision is approved, manual
  journal evidence is retained indefinitely and never deleted or edited.

### Break-glass

- `ledger.manual_journal.break_glass` is held only by named Security operations roles, requires dual control, step-up
  and MFA, is bound to a specific reason and account scope, and expires automatically at the end of the business date.
- It may bring a manual journal to post without the normal approval chain. It may never change a posted journal, bypass
  balancing or idempotency, post into a closed period, cross a currency, or operate without independent Security
  evidence and an audit-owner alert.

## 3. Options and impact

| Option | Position | Impact |
| --- | --- | --- |
| A — single permission with role-based maker-checker | One `ledger.manual_journal` permission plus a role rule for the approver. | Simplest to administer, but a role change silently widens posting authority and the step-up and class boundaries are harder to enforce. |
| B — four-permission maker-checker with tiered thresholds **(approved)** | Draft, submit, approve and post are separate; approval count scales with amount; customer lines need an explicit class and a compensating account. | Strongest separation for the only path that names accounts directly. More grants to administer and a published threshold table to maintain. |
| C — draft/approve split, submitter may post | Fewer permissions, submitter executes. | Reverses the separation the epic requires and lets the maker's own session carry the financial effect. Rejected. |

## 4. Required implementation evidence

1. Exact-hash approval binding, including refusal after any line, amount, date, currency or evidence change.
2. Maker-checker separation: the maker never approves, and the two-approver tiers use two distinct non-maker actors.
3. Cross-tenant, out-of-scope and class-less denial, including cash, settlement, tax, suspense, control and
   income/expense classes reached with only a general grant.
4. Customer-account default denial, and permitted posting only with the `customer_account_adjustment` class, account
   scope and named compensating account.
5. Threshold tiers enforce the correct approver count at each boundary.
6. Closed-period and closed-account refusal.
7. Balanced atomic posting, idempotent replay and immutable audit/outbox evidence.
8. Break-glass cannot bypass balancing, immutability, period, currency or idempotency, and creates independent Security
   evidence.

## 5. Approved policy boundary

- Manual posting is permitted only through the four permissions above and only for accounts their class grants reach.
- Customer accounts remain denied unless the explicit adjustment class, scope and compensating account are present.
- Amount thresholds, reason codes and class grants are published configuration, not code defaults; changing them is a
  configuration publication that requires Finance approval, not an implicit code change.
- This decision does not approve a specific account-class matrix beyond the classes named here, and does not approve
  closed-period reopening, backdating or cross-currency manual journals; those remain with `P1-E05-D12` and
  `P1-E05-D13`.
- Until this policy is implemented and proven, manual posting remains unavailable.

## 6. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | User | Approved Option B | 2026-11-26 |
| Finance | Project-owner approval | Approved four-permission model, customer-account adjustment class and the 100,000/1,000,000 approval tiers | 2026-11-26 |
| Security | Project-owner approval | Approved class grants, step-up, dual-control break-glass with auto-expiry and independent security evidence | 2026-11-26 |

