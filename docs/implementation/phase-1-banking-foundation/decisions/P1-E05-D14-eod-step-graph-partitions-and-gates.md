---
decision: P1-E05-D14
title: End-of-day step graph, partitions and completion gates
epic: P1-E05 Core Ledger and Financial Posting
owners: [Finance, Core Operations]
required_by: P1-E05-S12
status: Proposed
brief_date: 2026-11-09
related: [P1-E05-D03, P1-E05-D13]
decisions: [P1-E05-D14]
---

# Decision brief: restartable end-of-day processing

The platform needs an institution-specific processing graph before it can advance business date. The mechanics may be
implemented generically, but the actual financial steps and completion gates cannot be invented by engineering.

## 1. Decision requested

Approve the Phase 1 step graph, step owners, dependencies, partition keys, blocking exception rules, retry/resume
authority and the evidence required before business-date advancement.

## 2. Recommended mechanics

- One immutable run snapshot exists per institution, run type and business date. It records the graph/config versions.
- Step and partition identities are deterministic. Every financial item derives an idempotency key from
  run/step/partition/business reference/rule version.
- Dependencies form an acyclic graph validated at publication. A dependent step cannot start after an incomplete or
  failed prerequisite.
- Workers use expiring leases and heartbeats, while database checkpoints—not a lease or queue acknowledgement—are the
  source of truth.
- A checkpoint advances only in the same transaction as its defined effects and summary. Restart resumes the original
  run and identities from the last verified checkpoint.
- Parallel partitions use a published disjoint key and reconcile counts and debit/credit totals at merge.
- Completion requires zero unapproved blocking exceptions, balanced totals by accounting scope/currency, required
  outbox durability and the exact period/reconciliation gates.
- Override, skip and force-complete are separate privileged actions. None may manufacture a checkpoint or bypass a
  financial invariant.

## 3. Candidate Phase 1 graph requiring owner confirmation

`prerequisite validation -> scheduled financial inputs -> approved accrual/fee steps -> balance and event
reconciliation -> period checks -> completion summary -> business-date advancement`.

This is a review shape, not an approved operational sequence. Dormancy, statement, payment and provider steps remain
handoffs to their owning modules and must be named explicitly if owners require them as gates.

## 4. Required implementation evidence

Tests cover graph validation, two-worker claims, crash before/after checkpoint commit, deterministic replay, partition
disjointness, failed dependency, mismatched totals, stale graph version, restricted override and business-date refusal.

## 5. Deferred for approval

Finance/Core Operations must provide the actual step catalogue, ordering, partition sizes, exception severities,
cut-offs, operator authority and completion/sign-off matrix.

## 6. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending | |
| Finance | | Pending: approve financial steps and gates | |
| Core Operations | | Pending: approve run, retry and override process | |

