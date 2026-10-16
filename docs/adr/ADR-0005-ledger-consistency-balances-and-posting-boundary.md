---
id: ADR-0005
title: "Ledger consistency, balances and posting transaction boundary"
status: Proposed
date: 2026-10-16
owners: [Architecture, Finance]
required_by: "Phase 1 - Core Ledger and Financial Posting, slice 1"
architecture_item: 5
referenced_by:
  - docs/implementation/phase-1-banking-foundation/05-core-ledger-and-posting.md
supersedes: []
approved_by: []
approved_on: null
---

# ADR-0005: Ledger consistency, balances and posting transaction boundary

## Status

`Proposed` - not approved. `05-core-ledger-and-posting.md` states that this ADR must be approved before production implementation and is required by slice 1. It is the gate for any production financial posting.

## Context

- `AGENTS.md` (`## Non-negotiable financial invariants`, `## Commands, queries, and transactions`) requires only the protected Core Banking and Ledger context to change customer balances, requires every posted journal to balance within the same currency and accounting scope, makes posted entries immutable with corrections through linked reversal or adjustment, requires a journal and all of its lines to commit atomically, requires optimistic concurrency by default, and requires the concurrency strategy for balances, limits, sequences, approval transitions and idempotency records to be documented.
- `05-core-ledger-and-posting.md` states that this ADR must fix: the ledger consistency model, balance representation, database transaction boundary, concurrency and locking strategy, overdraft behaviour, account-control freshness policy and reconciliation approach. It also records that the exact isolation/locking strategy and retry limits are fixed here, and that debit/credit sign conventions, overdraft and credit-limit treatment, hold and lien precedence, pending amounts, credit restrictions and balance-snapshot update timing must be approved rather than inferred.
- The epic requires PostgreSQL to be the financial concurrency authority, with distributed locks unable to protect balances, and requires explicit `numeric(precision, scale)` for every monetary and rate column with no `money` or floating-point types.
- Requirement and story traceability: `LED-001` to `LED-013`, `ACC-005`, `ACC-007`, `DAT-001` to `DAT-005`, `DAT-008`, `NFR-PERF-002`, `NFR-SCAL-001`, `NFR-RES-002`, `NFR-DR-001`, `NFR-DR-002`, `SEC-009` to `SEC-011`, `SEC-013`, `AUD-001` to `AUD-004`, `BR-001` to `BR-008`, `BR-010`, `BR-013` to `BR-015`, `US-018` to `US-023`.

## Decision

Not yet decided. This ADR must settle, at minimum:

- Consistency model for balances: transactional balance rows, append-only movements plus projection, or an approved combination, and how a balance is rebuilt from posted records.
- Balance representation: debit/credit sign convention, ledger balance versus available balance, holds, liens, blocks, restrictions and pending authorised amounts as distinct components; when snapshots update; and the single authoritative read path.
- Posting transaction boundary: what commits atomically (transaction, journal, lines, movements, balances, idempotency outcome, audit evidence and outbox record) and what may not.
- Concurrency and locking: isolation level, optimistic concurrency versions, the use of `SELECT ... FOR UPDATE` or equivalent serialisation of balance-affecting operations, deadlock-retry policy and retry limits.
- Overdraft and available-funds policy: whether overdraft, credit limits, uncollected funds and pending authorisations participate in the available-funds calculation, and how insufficiency is rejected.
- Account-control freshness: which account, product and configuration state must be current before posting, and the fail-closed behaviour when freshness cannot be proven.
- Reconciliation approach: how ledger-to-balance, ledger-to-event and ledger-to-statement reconciliation is performed and evidenced.
- Monetary precision and scale per amount and rate, and rounding points.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| Derive every balance from movements on each read | Does not demonstrate the required concurrency performance or balance-rebuild evidence without an approved projection |
| Single global sequence or table lock for posting | Serialises the platform and fails availability and performance expectations |
| Balance updates outside the posting transaction | Violates the atomicity and audit-evidence invariants |
| Distributed locks or Redis for balance protection | Not a financial concurrency authority; prohibited by the epic |

## Consequences

- This decision fixes the schema, migration and test strategy for the ledger and constrains every service that requests postings.
- Contention behaviour, retry policy and error codes for concurrent debit attempts become part of the public contract.
- Balance representation choices propagate into statements, reconciliation, reporting read models and the available-funds APIs used by payments and channels.

## Compliance and evidence

- Concurrency tests using separate connections against real PostgreSQL, never an in-memory provider.
- Unbalanced-journal rejection, duplicate-idempotency-key, concurrent-debit, reversal-linkage, rounding and boundary tests as listed in the epic's test plan.
- Architecture and database tests that prevent non-ledger code or principals from writing protected tables.
- Balance rebuild and restore evidence with no duplicate or missing financial outcome.

## Open questions

- Whether overdraft is in scope for Phase 1 enforcement or only represented.
- The approved accounting period, business-date and backdate windows (separately owned by Finance, but interacting with this decision).
- The retention and partitioning policy applied to ledger tables.

## Approval gate

Do not implement production posting, balance mutation, migration scripts that encode a balance representation, or overdraft behaviour until this ADR is `Accepted`. The epic allows domain, contract and test work with synthetic mappings while production posting stays disabled.

## Related documents

- [ADR index](README.md)
- [ADR-0004](ADR-0004-database-per-service-and-tenant-isolation.md) - database ownership and migration execution
- [ADR-0007](ADR-0007-event-envelope-broker-outbox-inbox-and-replay.md) - outbox and event publication for posted transactions
- [05-core-ledger-and-posting.md](../implementation/phase-1-banking-foundation/05-core-ledger-and-posting.md) - ownership boundaries, invariants, test plan and open decisions
- [AGENTS.md](../../AGENTS.md) - `## Non-negotiable financial invariants`
