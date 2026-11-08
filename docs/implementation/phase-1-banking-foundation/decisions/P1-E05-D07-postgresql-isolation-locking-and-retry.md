---
decision: P1-E05-D07
title: PostgreSQL isolation, locking and retry policy
epic: P1-E05 Core Ledger and Financial Posting
owners:
  - Architecture
  - DBA
required_by: P1-E05-S1
status: Proposed
brief_date: 2026-11-08
related: [P1-E05-D01, P1-E05-D06, ADR-0004]
decisions: [P1-E05-D07]
---

# Decision brief: isolation, locking and retry

The epic fixes the principle — PostgreSQL is the financial concurrency authority, balances are locked deterministically,
availability is recomputed after the lock — and explicitly leaves "the exact isolation/locking strategy and retry
limits" to ADR-0005. Those numbers decide whether a busy account overspends, stalls or fails a customer with the wrong
error.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D07-A | Which isolation level do posting transactions use? | Concurrency semantics and failure modes |
| D07-B | Which rows are locked, in which order, and for how long? | Deadlock avoidance and throughput |
| D07-C | Where do unique constraints carry the concurrency instead of locks? | Idempotency and reference races |
| D07-D | Which errors are retried, how many times and with what delay? | Customer-visible behaviour |
| D07-E | What timeouts bound a posting statement and a transaction? | Failure detection and lock release |
| D07-F | How do background processing runs coordinate? | EOD lease, checkpoint and restart |

## 2. Why the answer is needed now

- Slice 5 implements the idempotency claim under concurrency and slice 7 proves concurrent debit safety; both are
  written against a specific policy.
- Payment callers in `P1-E07` need to know whether a retry is safe and with which key.
- A retry policy chosen per service is how one caller double-posts and another loses a legitimate debit.

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Idempotency under contention | Implemented and proven: unique insert, loser classified, one outcome | `IdempotencyService`, `P1-E04` database suites |
| Optimistic concurrency | Implemented on configuration, accounts and controls | `version` concurrency tokens |
| Row locking | Implemented for account-number allocation and control changes | `FOR UPDATE` in `AccountNumberAllocator`, `AccountControlService` |
| Balance and journal tables | **Not implemented** | No posting schema |
| Retry strategy | **Not implemented**; no caller retries a posting yet | — |
| Processing runs | **Not implemented** | Slice 12 |

## 4. Recommended position

### D07-A Isolation level

| Item | Recommendation |
| --- | --- |
| Posting transactions | `READ COMMITTED` |
| Why not `SERIALIZABLE` | Every balance-affecting row is taken under `FOR UPDATE` before the decision, so the write-skew class that `SERIALIZABLE` protects against cannot occur; paying its retry rate on every posting would fail the latency budget for no correctness gain |
| Why not `REPEATABLE READ` | It adds serialization failures on a workload that already serialises the same rows explicitly |
| Read-only balance queries | `READ COMMITTED`; a query never blocks a posting and never needs a consistent snapshot across accounts |
| Reporting and reconciliation | `REPEATABLE READ` inside a single reconciliation job, so one comparison run sees one snapshot |

### D07-B Locking

| Item | Recommendation |
| --- | --- |
| Locked rows | Every affected `account_balance` row, and the `ledger_account` row when status or classification can change the decision |
| Order | Ascending `ledger_account_id`, always, including for a transfer between two accounts |
| Statement | `SELECT ... FOR UPDATE` on the balance rows only, not a table lock, not an advisory lock |
| Hold on the lock | Inside the posting transaction only; no network call, no queue publish, no file write while held |
| Control and period rows | Read `FOR SHARE` or not at all; they are validated by version and effective date, and are not updated by a posting |
| Prohibited | `LOCK TABLE`, `pg_advisory_lock` for balances, `SELECT FOR UPDATE` in a different order per code path, and any lock held across an `await` on an external call |

### D07-C Unique constraints instead of locks

| Race | Serialised by |
| --- | --- |
| Two requests with the same idempotency key | Unique scope on `integration.idempotency_records`; the loser reads the winner's outcome |
| Two postings claiming one external reference | Unique scope approved in `P1-E05-D08` |
| Two holds with one source reference | Unique scope on the hold source |
| One journal per financial transaction | Unique `journal_entry(financial_transaction_id, sequence)` |

A constraint failure is a terminal outcome resolved by reading the existing row, not a retry loop.

### D07-D Retry

| Item | Recommendation |
| --- | --- |
| Retried | Only SQLSTATE `40001` (serialization failure) and `40P01` (deadlock detected) |
| Attempts | 3 in total (the first attempt plus 2 retries) |
| Delay | 25 ms, 50 ms, 100 ms, each with ±20% jitter |
| Identity | The same idempotency key, request hash, transaction id and journal ids are reused |
| Not retried | `23505` reference or idempotency conflicts, `23503`/`23514` integrity failures, business rejections, statement timeouts, and any request whose outcome was not observed without an authoritative status check |
| Client-visible behaviour | `deadlock_retry_exhausted` and `serialization_retry_exhausted` after the ceiling, with the correlation id so an operator can find the attempts |
| Caller contract | A caller may retry only with the same idempotency key; a new key requires an authoritative status resolution first |

### D07-E Timeouts

| Setting | Value | Reason |
| --- | --- | --- |
| `lock_timeout` on the posting connection | 2 s | A posting should never queue behind a slow statement; failing fast gives the caller a retryable outcome |
| `statement_timeout` on the posting connection | 5 s | Bounds a pathological statement without cutting a normal posting |
| `idle_in_transaction_session_timeout` | 10 s | Prevents an abandoned transaction from holding a balance lock |
| Connection pool | Bounded per instance; the pool is not raised to hide lock contention | — |

### D07-F Background processing runs

| Item | Recommendation |
| --- | --- |
| Ownership | A lease row with an expiry and heartbeat, so one worker owns a run |
| Authority | Database checkpoints and idempotency remain authoritative; a lost lease never re-executes committed work |
| Partitioning | Partitions are disjoint by a proven key, and a merge step proves debit and credit totals |
| Restart | Resume from the last verified checkpoint with the same identities |
| Locking | No long-lived table lock; each item's posting takes the same per-account locks as an interactive posting |

## 5. Constraints

- Do not retry a financial request under a new idempotency key.
- Do not hold a lock while calling a provider, publishing a message or writing a file.
- Do not take balance locks in different orders on different paths.
- Do not use an advisory lock, a Redis lock or an in-process semaphore as the balance authority.
- Do not raise the timeout values to hide contention; contention is an operational signal.
- This brief does not approve reference scopes (`P1-E05-D08`), the formula (`D06`) or the atomic unit (`D01`).

## 6. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Connection-pool sizing and instance count | Platform | `P1-E05-D17` load targets |
| Read-replica use for balance queries | Platform | Phase 1 reads the primary |
| EOD partition count and batch size | Core operations | `P1-E05-D14` |
| Archival vacuum and retention tuning | DBA | `P1-E05-D16` |

## 7. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending | |
| Architecture | | Pending: confirm `READ COMMITTED`, deterministic locks and the retry ceiling | |
| DBA | | Pending: confirm the timeout values and the lock ordering requirement | |

## 8. Implementation notes (for slices 5, 6, 7 and 12)

1. Use one helper to take balance locks in ascending order and never call it with an unsorted set.
2. Classify every database exception once, in one place, into retryable, conflict and terminal.
3. Reuse the identity set on a retry; assert in code that the retry path cannot mint a new key.
4. Set the three timeouts on the posting data source, not per statement, so no path can miss them.
5. Prove with separate connections on real PostgreSQL: a deliberate two-row deadlock retries and then converges, a
   one-balance account refuses the second of two concurrent debits, a concurrent duplicate key produces one outcome,
   and a statement timeout leaves no committed partial journal.
