---
decision: P1-E05-D05
title: Debit/credit sign convention and balance representation
epic: P1-E05 Core Ledger and Financial Posting
owners:
  - Finance
  - Architecture
required_by: P1-E05-S1
status: Approved
approved_on: 2026-11-09
approved_by: Project owner
brief_date: 2026-11-08
related: [P1-E05-D01, P1-E05-D02, P1-E05-D04, P1-E05-D06]
decisions: [P1-E05-D05]
---

# Decision brief: debit/credit sign and balance representation

Two conventions have to be fixed together: how a line carries direction, and what a stored balance *means* when the
same table holds a customer liability and an income account. The epic already forbids mixed sign conventions on lines;
it leaves open how direction is stored, how a net is signed, and what a customer-facing balance is derived from.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D05-A | Do lines carry a positive amount plus a direction, or a signed amount? | Every line contract, insert and assertion |
| D05-B | How is a movement signed? | Movement storage and rebuild arithmetic |
| D05-C | What does the stored balance row mean for a credit-normal account? | Balance queries and statements |
| D05-D | What is a customer-facing balance? | The API shape and the available-funds input |
| D05-E | How is a balance rebuilt and compared? | Rebuild, reconciliation and restore proof |
| D05-F | How is a negative customer balance represented? | Overdraft representation, even though Phase 1 extends none |

## 2. Why the answer is needed now

- Slice 2 defines the `DebitCreditIndicator` and balance contracts; slice 4 encodes the append-only line shape; slice 6
  writes the first movement.
- A customer deposit is a liability, so a credit increases what the customer holds. A convention that treats "positive
  balance" as a debit would show every funded account as negative, and every statement, receipt and channel would have
  to invert it.
- Statement and reporting read models consume the answer directly, and the epic forbids them from re-deriving it.

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Journal line | **Not implemented** | No posting tables |
| Balance row | **Not implemented** | No `account_balance` table |
| Balance contract | **Not implemented** | No ledger posting contract |
| Account-side balance concepts | Implemented as a read model: ledger balance, available, holds, liens and blocks kept distinct | `AccountBalanceSummaryV1`, `P1-E04-S13` |
| Availability source the account module reads | Implemented behind `ILedgerAvailabilitySource`, backed by account metadata today | `P1-E04-S11` |

## 4. Recommended position

### D05-A Line direction

| Item | Recommendation |
| --- | --- |
| Amount | Always positive on a line; a zero or negative line amount is refused |
| Direction | An explicit `DebitCreditIndicator` (`D`/`C`) stored as a constrained column |
| Why | The epic requires direction to be explicit; a signed amount makes an accidental sign flip a silent accounting change, while a direction column is validated at insert |
| Prohibited | A line with both a sign and a direction, or a line whose amount sign is inferred from the account role |

### D05-B Movement signing

| Item | Recommendation |
| --- | --- |
| Convention | Debit-positive: `signed_amount = +amount` for a debit, `-amount` for a credit |
| Storage | `numeric(19,4)` with a check that the sign matches the referenced line's direction |
| Immutability | A movement is inserted once and never updated or deleted |
| Ordering | Each movement records the balance version before and after, and its sequence within the ledger account |

### D05-C Stored balance meaning

| Item | Recommendation |
| --- | --- |
| `net_balance` | The sum of signed movements, debit-positive, for that ledger account and currency |
| `total_debits` and `total_credits` | Stored alongside, so a reader never has to derive one from the other |
| `normal_side` | A property of the resolved chart role (`P1-E05-D02`), not of the row, so it cannot drift per account |
| Reading rule | A balance is only meaningful together with its ledger account's normal side; a raw `net_balance` is never returned to a caller without the derived figure |

### D05-D Customer-facing balance

| Item | Recommendation |
| --- | --- |
| Definition | `visible_balance = net_balance` when the role's normal side is debit, and `-net_balance` when it is credit |
| Customer deposit | Normal side credit, so a customer who deposits 1 000.00 shows `visible_balance = 1000.0000` |
| Response shape | The balance contract carries `ledger_balance` (visible), the component breakdown (holds, liens, blocked, pending), `available_balance`, `balance_version`, `as_of` and `business_date` |
| Available balance | Computed from the visible balance by the approved formula in `P1-E05-D06`; it is a different field and never a synonym |
| Prohibited | Returning `net_balance` under a name a caller could read as the customer's money |

### D05-E Rebuild and comparison

| Item | Recommendation |
| --- | --- |
| Rebuild input | A verified checkpoint row plus movements in sequence order |
| Rebuild output | `net_balance`, `total_debits`, `total_credits` per ledger account and currency |
| Comparison | Exact decimal equality per component; a difference is a reconciliation finding with account, expected, actual and version |
| Restore proof | Rebuilding after restore reproduces every accelerator row bit-for-bit in value |

### D05-F Negative customer balances

| Item | Recommendation |
| --- | --- |
| Representation | A visible balance below zero is stored and returned as a negative decimal; no separate "overdrawn" flag replaces it |
| Phase 1 policy | No Phase 1 account is issued a credit limit, so a posting that would take a customer position below zero is refused by `P1-E05-D06` before it is written |
| Reporting | A negative visible balance is reported as overdrawn even when the account carries no approved limit, so an operational error is visible rather than hidden |

## 5. Constraints

- Do not store a signed amount on a line, and do not store a direction inferred at read time.
- Do not let a caller see `net_balance` without its normal side applied.
- Do not treat the ledger balance, the available balance, holds, liens, blocked amounts and pending amounts as
  interchangeable, and do not collapse them into one field.
- Do not let a movement be updated or deleted to correct a posting.
- This brief does not approve the available-funds formula (`P1-E05-D06`), the chart roles (`D02`) or precision (`D04`).

## 6. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Which accounts may carry a credit limit | Product/Risk | Phase 1 issues none; the representation is what is approved |
| Presentation of an overdrawn balance to channels | Product/Compliance | Channel concern |
| Statement running-balance semantics | Finance/Product | `P1-E05-D15` |
| Multi-currency per customer aggregation | Product | FX is out of Phase 1 scope |

## 7. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | User | Approved recommended position | 2026-11-09 |
| Finance | | Pending: confirm debit-positive movements and the normal-side derivation | |
| Architecture | | Pending: confirm the line direction column and the read shape | |

## 8. Implementation notes (for slices 2, 4 and 6)

1. Model direction as an enum with a database check constraint, and add a check that `signed_amount` matches it.
2. Compute the visible balance from the stored net and the role's normal side in one place, never at each call site.
3. Carry `balance_version` and `as_of` on every balance response so a caller can detect staleness.
4. Ensure the account module's availability handoff reads the ledger's visible balance, not a raw net.
5. Prove with tests: a deposit shows a positive customer balance on a credit-normal account, a withdrawal reduces it,
   an income account's normal side is debit, a movement sign that contradicts its line is refused at the database, and
   a rebuild reproduces all three stored totals exactly.
