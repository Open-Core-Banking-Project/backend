---
id: ADR-0005
title: "Ledger consistency, balances and posting transaction boundary"
status: Accepted
date: 2026-10-16
owners: [Architecture, Finance]
required_by: "Phase 1 - Core Ledger and Financial Posting, slice 1"
architecture_item: 5
referenced_by:
  - docs/implementation/phase-1-banking-foundation/05-core-ledger-and-posting.md
supersedes: []
approved_by: [Project owner]
approved_on: 2026-11-09
---

# ADR-0005: Ledger consistency, balances and posting transaction boundary

## Status

`Accepted` on 2026-11-09 by the project owner through approval of `P1-E05-D01` to `P1-E05-D09`.

## Context

- `AGENTS.md` (`## Non-negotiable financial invariants`, `## Commands, queries, and transactions`) requires only the protected Core Banking and Ledger context to change customer balances, requires every posted journal to balance within the same currency and accounting scope, makes posted entries immutable with corrections through linked reversal or adjustment, requires a journal and all of its lines to commit atomically, requires optimistic concurrency by default, and requires the concurrency strategy for balances, limits, sequences, approval transitions and idempotency records to be documented.
- `05-core-ledger-and-posting.md` states that this ADR must fix: the ledger consistency model, balance representation, database transaction boundary, concurrency and locking strategy, overdraft behaviour, account-control freshness policy and reconciliation approach. It also records that the exact isolation/locking strategy and retry limits are fixed here, and that debit/credit sign conventions, overdraft and credit-limit treatment, hold and lien precedence, pending amounts, credit restrictions and balance-snapshot update timing must be approved rather than inferred.
- The epic requires PostgreSQL to be the financial concurrency authority, with distributed locks unable to protect balances, and requires explicit `numeric(precision, scale)` for every monetary and rate column with no `money` or floating-point types.
- Requirement and story traceability: `LED-001` to `LED-013`, `ACC-005`, `ACC-007`, `DAT-001` to `DAT-005`, `DAT-008`, `NFR-PERF-002`, `NFR-SCAL-001`, `NFR-RES-002`, `NFR-DR-001`, `NFR-DR-002`, `SEC-009` to `SEC-011`, `SEC-013`, `AUD-001` to `AUD-004`, `BR-001` to `BR-008`, `BR-010`, `BR-013` to `BR-015`, `US-018` to `US-023`.

## Decision

The approved position is defined by decision briefs `P1-E05-D01` to `P1-E05-D09`:

- Append-only balance movements are the financial record; transactionally updated account-balance rows are the
  authoritative read accelerator and are rebuildable from a verified checkpoint plus ordered movements.
- Transaction, journal, lines, movements, balances, idempotency outcome, audit and outbox commit atomically.
- PostgreSQL `READ COMMITTED`, deterministic ascending account locks, optimistic versions and bounded retries only for
  `40001`/`40P01` protect concurrent posting.
- Phase 1 extends no overdraft. Available funds use the approved D06 formula and fail closed on stale controls or
  configuration.
- Positive line amounts use an explicit debit/credit indicator; amounts use `numeric(19,4)`, rates `numeric(9,8)`, and
  rounding is explicit at component boundaries.
- Balancing scope is institution, legal entity and currency. Mappings resolve approved roles and never a suspense
  fallback.
- References, components and duplicate/replay behaviour follow D08/D09. Reconciliation compares journals, movements,
  balance accelerators, outbox facts and statement history.

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

- The approved accounting period, business-date and backdate windows (separately owned by Finance, but interacting with this decision).
- The retention and partitioning policy applied to ledger tables.

## Approval gate

The ADR gate is satisfied. Production enablement still requires approved chart/mapping data, later slice evidence and
all release gates; acceptance of this ADR does not itself enable a posting endpoint.

## Related documents

- [ADR index](README.md)
- [ADR-0004](ADR-0004-database-per-service-and-tenant-isolation.md) - database ownership and migration execution
- [ADR-0007](ADR-0007-event-envelope-broker-outbox-inbox-and-replay.md) - outbox and event publication for posted transactions
- [05-core-ledger-and-posting.md](../implementation/phase-1-banking-foundation/05-core-ledger-and-posting.md) - ownership boundaries, invariants, test plan and open decisions
- [AGENTS.md](../../AGENTS.md) - `## Non-negotiable financial invariants`
