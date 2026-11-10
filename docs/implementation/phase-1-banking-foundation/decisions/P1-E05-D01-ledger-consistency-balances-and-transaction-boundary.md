---
decision: P1-E05-D01
title: Ledger consistency, balance representation and the posting transaction boundary
epic: P1-E05 Core Ledger and Financial Posting
owners:
  - Architecture
  - Finance
required_by: P1-E05-S1
status: Approved
approved_on: 2026-11-09
approved_by: Project owner
brief_date: 2026-11-08
related: [ADR-0005, ADR-0004, ADR-0007, P1-E05-D02, P1-E05-D05, P1-E05-D06, P1-E05-D07]
decisions: [P1-E05-D01]
---

# Decision brief: ledger consistency, balances and the posting boundary

[ADR-0005](../../../adr/ADR-0005-ledger-consistency-balances-and-posting-boundary.md) is the gate for every production
posting in Phase 1 and is still `Proposed`. It records the questions rather than the answers, and the epic's slice 1 is
exactly the approval of those answers. This brief is the proposed resolution; accepting it is what moves ADR-0005 to
`Accepted`.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D01-A | Transactional balance rows, movements, or both — and what a rebuild proves | Schema, rebuild and reconciliation design |
| D01-B | What commits in the one posting transaction, and what may not | The atomic unit and every failure path |
| D01-C | Isolation level, locking, optimistic versions and retry limits | Contention behaviour and public error codes |
| D01-D | Overdraft and uncollected funds in Phase 1 | The available-funds floor |
| D01-E | Which state must be fresh before posting, and the fail-closed answer | Control and configuration freshness policy |
| D01-F | The reconciliation set and its evidence | Period close, EOD gates and restore proof |

## 2. Why the answer is needed now

- Slice 2 cannot define `Money`, dates, references or idempotency contracts without knowing what the transaction
  boundary guarantees, and slice 6 cannot post anything before this is accepted.
- Every later epic consumes the answer: payments call it, statements read it, reporting rebuilds from it, and
  reconciliation compares against it.
- The epic already states that read models, caches and reports can never authorise a debit, so the authoritative path
  has to be named once, not per caller.

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Ledger posting domain | **Not implemented** | `CoreBanking.CoreLedger.Domain` holds Institutions, Products and Accounts only |
| Journal, line, movement and balance tables | **Not implemented** | No posting tables exist in `core_ledger_db` |
| Posting contracts | **Not implemented** | `CoreBanking.CoreLedger.Contracts` holds institution, product, account and statement contracts |
| Idempotency infrastructure | Implemented and proven under contention | `IdempotencyService`, `integration.idempotency_records` |
| Transactional outbox | Implemented and proven | `integration.outbox_messages`, `scripts/db-verify.ps1` |
| Account controls and availability handoff | Implemented and proven against PostgreSQL | `P1-E04-S10`, `P1-E04-S11` database suites |
| Advisory conventions | Epic sections 6 to 9 state the invariants but leave the representation open | `05-core-ledger-and-posting.md` |

## 4. Recommended position

### D01-A Balance representation

| Item | Recommendation |
| --- | --- |
| Model | Both: append-only `balance_movement` rows are the financial record, and one `account_balance` row per ledger account per currency is the transactional accelerator |
| Movement content | Journal line reference, signed amount, direction, balance version before and after, business date, posting date, value date |
| Accelerator content | Net signed balance, total debits, total credits, hold total, lien total, version, last movement reference, as-of timestamp |
| Update timing | Inside the posting transaction, after lines are inserted, in the same commit |
| Read path | Ledger balance queries read the accelerator; any consumer needing proof of composition reads movements |
| Rebuild | From a verified checkpoint row plus movements in movement-sequence order, compared against the stored accelerator; a mismatch is a reconciliation finding, never a silent overwrite |
| Prohibited | Deriving a balance by arithmetic SQL outside the ledger module, or repairing one with `UPDATE ... SET balance = balance ± x` outside a movement |

### D01-B The atomic unit

Commits together, or nothing does:

1. the pending idempotency record becoming its stored outcome;
2. `financial_transaction`, `journal_entry`, `journal_line` and `balance_movement` rows;
3. the locked `account_balance` updates and their version increments;
4. audit evidence rows;
5. the outbox rows for the posted facts.

Never inside the transaction: HTTP calls, provider calls, event publication to the broker, statement or document
generation, and any read of another service's database. Everything required from outside is resolved before the
transaction opens, and revalidated from locked local state inside it.

### D01-C Isolation, locking and retry

| Item | Recommendation |
| --- | --- |
| Isolation | PostgreSQL `READ COMMITTED` as the default for posting |
| Locking | `SELECT ... FOR UPDATE` on every affected `account_balance` row, taken in ascending `ledger_account_id` order, after the idempotency claim |
| Optimistic versions | `version` on `account_balance` and on `financial_transaction` state; a mismatch is a retryable or rejectable conflict, never a silent overwrite |
| Unique constraints | Idempotency scope and configured references, so a race loses at the database rather than in code |
| Retry | Only `40001` (serialization) and `40P01` (deadlock), at most 3 attempts with 25/50/100 ms jitter, reusing the same ids, key and request hash |
| Never retried | Business rejections, constraint violations on references, and any timed-out request without authoritative status resolution |
| Statement timeout | 5 seconds per posting statement; a timeout is resolved by idempotency lookup, never by a new key |
| Distributed locks | Not a financial concurrency authority; Redis may schedule but never protect a balance |

### D01-D Overdraft and uncollected funds

| Item | Recommendation |
| --- | --- |
| Overdraft in Phase 1 | Represented, not extended: a ledger account may carry an approved credit limit record, and no Phase 1 account is issued one |
| Available-funds floor | `available` never goes below zero for an account without an approved limit; an attempted debit below zero is refused with `insufficient_available_funds` |
| Uncollected funds | Out of Phase 1 scope; there is no provisional-credit facility to represent |
| Pending authorised amounts | Deducted from available when a hold exists for them; they are never counted as posted balance |

### D01-E Freshness and fail-closed behaviour

| Item | Recommendation |
| --- | --- |
| Account controls | The caller supplies the control version it evaluated, and the ledger compares it to the current version inside the transaction; a mismatch is `control_version_stale` and the posting stops |
| Mapping and configuration | Resolved by exact effective version for the business date before the transaction, then revalidated as still the effective version inside it |
| Reference facts | Institution, branch and currency facts resolved before the transaction; a fact that cannot be proven current refuses the posting rather than defaulting |
| Accounting period | Lookup and lock validation inside the transaction |

### D01-F Reconciliation set

| Comparison | Frequency | Evidence |
| --- | --- | --- |
| Movements to accelerator balances | Every EOD and every restore | Per-account totals compared, mismatches reported with codes |
| Journals to movements | Every EOD | Every posted line has exactly one movement |
| Debits to credits per scope and currency | Every journal, and every EOD summary | Zero difference, stored on the processing run |
| Posted transactions to outbox facts | Every EOD | No posted transaction without its fact, no fact without a transaction |
| Statement data to posted history | On request | The statement source is the posted history, never a report table |

## 5. Constraints

- Do not create a second authoritative balance store, and do not let a cache answer a posting decision.
- Do not hold a database lock across a network call.
- Do not invent a suspense account, an implicit mapping or a default currency.
- Do not accept a caller-supplied balance, control truth or final amount.
- This brief approves representation, boundary, concurrency, freshness and reconciliation. It does not approve the chart
  of accounts (`P1-E05-D02`), the event catalogue (`D03`), precision (`D04`), the sign convention in detail (`D05`), the
  available-funds formula (`D06`), the concrete lock and retry numbers beyond the baseline above (`D07`), reference
  scopes (`D08`) or component treatment (`D09`).

## 6. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Concrete overdraft products and credit limits | Product/Risk | Phase 1 extends none; the representation is what is approved |
| Retention, partitioning and archival of ledger tables | Compliance/DBA | `P1-E05-D16` |
| Load, latency, RTO and RPO targets | Product/Platform | `P1-E05-D17` |
| Whether a period may be reopened and by whom | Finance | `P1-E05-D13` |

## 7. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | User | Approved recommended position | 2026-11-09 |
| Finance | | Pending: confirm the movement-plus-accelerator model and the zero floor | |
| Architecture | | Pending: confirm the transaction boundary, locking baseline and retry ceiling | |

## 8. Implementation notes (for slices 2, 4, 6 and 7)

1. Model the atomic unit as one use case with one `SaveChanges` inside one explicit transaction; nothing outside the
   ledger module may write these tables.
2. Take balance locks in ascending ledger-account order and recompute availability after the locks are held.
3. Derive the posted result from the committed rows, not from the in-memory intent.
4. Add database guards that reject an unbalanced journal and an update or delete of a posted line at the constraint or
   trigger level as well as in code.
5. Prove with separate connections against real PostgreSQL: a concurrent double debit cannot both succeed, an
   unbalanced journal is rejected, a replayed key returns the original outcome, a deadlock retries with the same
   identities, and a rebuild reproduces every accelerator balance.
