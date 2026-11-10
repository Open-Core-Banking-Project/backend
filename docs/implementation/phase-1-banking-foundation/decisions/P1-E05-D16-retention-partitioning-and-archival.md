---
decision: P1-E05-D16
title: Ledger retention, partitioning and archival
epic: P1-E05 Core Ledger and Financial Posting
owners: [Compliance, DBA]
required_by: Before migration approval
status: Proposed
brief_date: 2026-11-09
related: [P1-E11-D06, P1-E11-D10, P1-E11-D16]
decisions: [P1-E05-D16]
---

# Decision brief: financial-data lifecycle

Partitioning is a physical optimisation; it must not weaken immutability, tenant isolation, legal hold, statement access,
reconciliation or restore. Retention periods are legal policy and cannot be inferred from a technical default.

## 1. Decision requested

Approve retention and legal-hold rules by record class, the partition keys and cadence, archival format/location,
authorised retrieval/disposition, and the restore verification required before any online data is removed.

## 2. Recommended position

- Journals, lines, movements, transaction roots, idempotency outcomes, reversal links, period summaries and EOD evidence
  have separately declared retention classes. Related evidence cannot expire earlier than the posting it proves.
- No posted financial record is application-deleted or updated. Expiry, where legally approved, is a controlled
  archive-and-disposition workflow with immutable evidence.
- Legal hold overrides disposition and is scoped, authorised, auditable and independently releasable.
- Initial PostgreSQL partitioning, if volume requires it, uses a posting-date range with institution/legal-entity and
  stable identifiers in indexes; uniqueness and foreign-key invariants must remain enforceable.
- A partition is archived only after row counts, debit/credit totals, hashes, references and a test restore match.
- Archive objects are encrypted, versioned, integrity-protected, access-audited and searchable by protected reference.
- Statement, reversal, reconciliation and regulatory queries define whether they read online plus archive or require a
  controlled rehydrate job. “Archived” never means unavailable without an explicit service level.
- Partition detach/drop is prohibited until approved disposition, legal-hold check and independently retained evidence.

## 3. Required implementation evidence

Migration tests prove uniqueness and tenant indexes across partitions. Lifecycle tests prove hold precedence, failed
archive verification prevents disposition, archived restore reconciles, and ordinary principals cannot detach/drop.

## 4. Deferred for approval

Compliance must provide periods, residency and hold rules. DBA/Platform must select storage, partition thresholds,
archive/retrieval tooling, encryption and restore cadence. No partition migration should be approved before both exist.

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending | |
| Compliance | | Pending: approve retention, residency, hold and disposition | |
| DBA/Platform | | Pending: approve partition and archive design | |

