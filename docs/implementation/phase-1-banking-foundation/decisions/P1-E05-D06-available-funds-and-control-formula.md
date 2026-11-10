---
decision: P1-E05-D06
title: Available-funds, overdraft, hold, lien and pending formula
epic: P1-E05 Core Ledger and Financial Posting
owners:
  - Finance
  - Risk
  - Product
required_by: P1-E05-S1
status: Approved
approved_on: 2026-11-09
approved_by: Project owner
brief_date: 2026-11-08
related: [P1-E05-D01, P1-E05-D05, P1-E04-D10, P1-E04-D11]
decisions: [P1-E05-D06]
---

# Decision brief: the available-funds formula

The epic requires available-funds validation to use "the approved formula", and forbids encoding a universal formula
until it is approved. Four Phase 1 epics already depend on the answer: payments will debit against it, the account module
publishes liens into it, controls deny against it, and statements report it.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D06-A | Which components enter available funds, with which sign? | The formula and the balance contract |
| D06-B | Does Phase 1 enforce overdraft? | Whether a debit can take a balance below zero |
| D06-C | How do holds, liens, blocks, restrictions and pending amounts differ in the formula? | Ledger versus account ownership and the refusal code |
| D06-D | What exactly is refused, and with which code? | The caller-visible contract |
| D06-E | When is the formula evaluated relative to locks and controls? | The posting sequence |
| D06-F | How is a shortfall reported? | Operational visibility without inventing a balance |

## 2. Why the answer is needed now

- Slice 7 proves concurrent debit safety against this formula. Without it, "safe" has no definition.
- The account module already keeps the concepts distinct in its summary contract, and the ledger must not collapse them
  when it becomes the authority.
- Two epics in progress (`P1-E04` controls and liens) hand values to the ledger; the ledger has to know which of them
  subtracts from available and which denies outright.

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Available-balance contract | Implemented as a read model with distinct fields; not yet computed by a ledger | `AccountBalanceSummaryV1` |
| Control evaluation | Implemented and database-proven; returns allow, deny or review | `P1-E04-S10`, `AccountControlDatabaseTests` |
| Lien outstanding amounts | Implemented and database-proven, with ledger availability handoff | `P1-E04-S11`, `AccountLienDatabaseTests` |
| Holds | **Not implemented** | Slice 8 |
| Pending authorised amounts | **Not implemented** | Payments epic |
| Credit limits | **Not implemented**, and Phase 1 issues none | No ledger account limit column |

## 4. Recommended position

### D06-A The formula

```text
available = visible_ledger_balance
          + approved_credit_limit                 (Phase 1: always 0)
          - active_hold_remaining
          - outstanding_lien_amount
          - pending_authorised_amount             (Phase 1: 0 until payments exist)
```

| Component | Sign | Authority |
| --- | --- | --- |
| Visible ledger balance | Positive when the customer holds funds, per `P1-E05-D05` | Ledger |
| Approved credit limit | Positive; only where an approved limit record exists | Ledger, risk-approved |
| Active hold remaining | Negative; the un-captured remainder only | Ledger |
| Outstanding lien amount | Negative; the outstanding, not the claimed, amount | Account metadata, enforced by ledger |
| Pending authorised amount | Negative; authorised but not yet posted | Ledger/payment contract |
| Blocked and restricted amounts | **Not in the formula** | Account controls deny by direction and scope, they do not reserve money |

Restrictions, freezes and blocks never appear as an amount. They are evaluated first, and a denial stops the posting
before the formula is reached.

### D06-B Overdraft in Phase 1

| Item | Recommendation |
| --- | --- |
| Enforcement | No Phase 1 account carries a credit limit, so a posting that would take available below zero is refused |
| Representation | The limit is a ledger-account attribute that exists and defaults to zero, so Phase 2 enables it by configuration and an approved limit, not by a schema change |
| Uncollected funds | Not represented; there is no provisional-credit facility in Phase 1 |
| Why | An unplanned overdraft is a credit decision, and Phase 1 has no credit approval process for it |

### D06-C The five concepts

| Concept | Reduces available? | Denies? | Owner |
| --- | --- | --- | --- |
| Hold | Yes, by the active remainder | No | Ledger |
| Lien | Yes, by the outstanding amount | No | Account module, published to the ledger |
| Block | No | Yes, by declared operation scope | Account module |
| Restriction | No | Yes, by direction, channel and transaction type | Account module |
| Pending authorised | Yes, by the authorised amount | No | Ledger with the payment contract |

### D06-D Refusals

| Condition | Code | Outcome |
| --- | --- | --- |
| Applicable prohibitive control | `account_control_denied` (with the control type suffix already produced by the account module) | Refused, no journal |
| Control version older than current | `control_version_stale` | Refused, no journal |
| Available below the debit amount | `insufficient_available_funds` | Refused, no journal |
| Debit that would breach a zero floor | `insufficient_available_funds` with the shortfall in the evidence | Refused, no journal |
| Account not in a posting status | `account_not_postable` | Refused, no journal |

### D06-E Evaluation order

Inside the posting transaction, after the idempotency claim and the balance locks:

1. re-read account status and the control set;
2. evaluate controls and stop on deny or stale;
3. recompute available from the locked balance rows, holds, liens and pending amounts;
4. compare against the requested debit for each affected account;
5. only then build and insert the journal.

Never evaluate availability before the locks are held, and never trust a caller-supplied available figure.

### D06-F Reporting a shortfall

| Item | Recommendation |
| --- | --- |
| Available field | Reported as the arithmetic result; a deduction set larger than the balance is reported as zero available plus an explicit `deficit` field |
| Deficit | `max(0, deductions - visible_balance - credit_limit)`, so an operator sees how far an account is over-committed without a negative available figure being mistaken for a credit |
| Statement and summary | Both report the deficit; neither invents a balance |

## 5. Constraints

- Do not let a control reserve money, and do not let a hold or lien deny a posting by itself.
- Do not use the claimed lien amount where the outstanding amount is the deduction.
- Do not let available be computed outside the ledger, or cached into a posting decision.
- Do not enable an overdraft by configuration alone; it needs an approved limit record and a credit decision.
- This brief does not approve the control precedence rules (`P1-E04-D10`, approved), lien priority (`P1-E04-D11`,
  approved), the chart (`D02`) or the transaction boundary (`P1-E05-D01`).

## 6. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Credit-limit products and their approval | Product/Risk | Phase 2 credit scope |
| Provider pending-authorisation semantics | Payments owner | `P1-E07` |
| Disclosure of a deficit to the customer | Compliance | Channel communication |
| Multi-currency availability | Treasury | FX is out of Phase 1 scope |

## 7. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | User | Approved recommended position | 2026-11-09 |
| Finance | | Pending: confirm the formula and the zero floor | |
| Risk | | Pending: confirm no Phase 1 overdraft | |
| Product | | Pending: confirm the deficit reporting shape | |

## 8. Implementation notes (for slices 6, 7 and 8)

1. Compute available in one domain function used by posting, holds and the balance query, so the three cannot drift.
2. Take the balance locks, then re-read controls, then compute — in that order, inside one transaction.
3. Reduce a hold by capture and release amounts only, never below zero, and never let a hold exceed the balance it
   reserves.
4. Emit the shortfall and the component breakdown in the refusal evidence without exposing another customer's data.
5. Prove with tests on real PostgreSQL: two concurrent debits cannot both pass a one-balance account, a lien reduces
   available without denying, a block denies a scoped debit regardless of balance, a stale control version refuses, and
   a capture cannot exceed the active hold remainder.
