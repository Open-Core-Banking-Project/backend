---
decision: P1-E04-D16
title: Capacity, latency, RTO and RPO targets
epic: P1-E04 Products and Accounts
owners:
  - Product
  - Platform
required_by: Before release testing
status: Proposed
brief_date: 2026-11-08
related: [P1-E04-D15, P1-E04-D13, P1-E05-D07]
decisions: [P1-E04-D16]
---

# Decision brief: capacity, latency, recovery and the shape of the evidence

The epic requires performance, capacity, backup and restore evidence and forbids inventing the numbers that decide
whether the evidence passes. This brief does not propose a target: it fixes what will be measured, on what, with which
statistics, and what a recovery objective has to include before anyone may claim it. The values stay with Product and
Platform.

## 1. Decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D16-A | Which operations carry a latency objective? | The measured journey set |
| D16-B | How is each one measured: percentile, concurrency, warm or cold, data volume? | The measurement contract so two runs are comparable |
| D16-C | Where do the values live, and what fails when they are exceeded? | A release gate that is configuration, not code |
| D16-D | What do RTO and RPO mean here, and which mechanisms must exist before either is claimed? | Recovery scope and the prerequisites |
| D16-E | What may never be asserted from a workstation run? | The honesty boundary of the current evidence |
| D16-F | Which recovery events are drilled, and how often? | The drill calendar and its evidence |

## 2. Why the answer is needed now

- `scripts/db-verify.ps1` proves correctness under contention, and the new `scripts/restore-drill.ps1` proves a
  dump/restore round trip, but neither can say whether the result is acceptable: there is nothing to compare against.
- The slice ledger records `P1-E04-D16` as the last open decision blocking `P1-E04-S15`, and the routing script reports
  `resolve_decision` for it.
- Every later epic inherits the gate. Payments contention, ledger posting throughput and end-of-day runtimes are all
  measured against whatever this decision defines, so a loose definition now is measured forever.

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Migration and privilege evidence | Implemented: nine migrations applied by their migration principal, suites run as each application principal, 54 effective-privilege tests | `scripts/db-verify.ps1` |
| Database evidence per service | Implemented: 30 Core Ledger, 102 Customer Onboarding, 105 Identity, 6 Workflow, 2 Audit Reconciliation scenarios | `Category=Database` suites |
| Concurrency evidence | Implemented for allocation, control versions, opening and closure | `AccountOpeningDatabaseTests`, `AccountControlDatabaseTests` |
| Database-free evidence harness | Implemented: repository gate, per-project counts, permission grammar, supply chain | `scripts/phase1-evidence.ps1` |
| Backup/restore round trip | Implemented 2026-11-08 for all nine databases: dump, restore into a scratch database, per-table row-count comparison | `scripts/restore-drill.ps1` |
| Latency and throughput measurement | **Not implemented** | No load generator, no percentile capture, no recorded profile |
| Approved test environment and data volume | **Not implemented** | Local compose stack only |
| Point-in-time recovery | **Not configured** | No WAL archiving, no retained base backup, no restore-to-timestamp drill |
| Off-site copy, encryption at rest, key custody | **Not configured** | Compose volume on the development host |
| Alert thresholds and dashboard targets | **Not configured** | Operations concern; no numbers exist to configure |

## 4. What has been observed so far

Measured on 2026-11-08 by `scripts/restore-drill.ps1` against the local compose stack: one host, eight logical
processors, PostgreSQL 16.15, containerised, with the development and test data that the suites leave behind. The
figures are observations of a round trip, not targets and not a capacity statement.

| Service | Tables | Rows | Dump ms | Restore ms |
| --- | --- | --- | --- | --- |
| Core Ledger | 42 | 2208 | 1897 | 4530 |
| Customer Onboarding | 36 | 86204 | 9689 | 8686 |
| Identity Access | 33 | 1440 | 1371 | 2310 |
| Payments and VTU | 6 | 2 | 853 | 636 |
| Channels and Engagement | 6 | 2 | 741 | 783 |
| Operations and Workflow | 9 | 16 | 930 | 1003 |
| Risk Reporting | 6 | 2 | 721 | 648 |
| Integration Hub | 8 | 3 | 818 | 900 |
| Audit and Reconciliation | 8 | 72 | 1034 | 1356 |

Every table's row count matched after restore. Restore time here excludes detection, decision, environment
provisioning, service start-up and verification, so it is one component of an RTO and not an RTO.

## 5. Recommended position

### D16-A The measured journeys

| Journey | Why it is in the set |
| --- | --- |
| Account opening, end to end: application, eligibility, approval, number allocation, opening, activation | The epic's main synchronous journey |
| Control change: restriction, freeze and release with dual control | Contention-sensitive write path |
| Lien create, partial release, statutory release | Financial write path with an availability handoff |
| Account summary read | The read path every channel uses |
| Statement request and handoff | Authoritative query plus document handoff |
| Dormancy and closure assessment | Batch-shaped evaluation path |
| Concurrent allocation and concurrent control updates | Proves the lock strategy under load, not just under six callers |

Posting, holds and reversal join the set when `P1-E05` implements them; they are listed here so the profile is not
renegotiated later.

### D16-B The measurement contract

| Dimension | Rule to agree |
| --- | --- |
| Statistic | p50, p95 and p99 per journey, plus error rate; an average alone is not evidence |
| Concurrency | Named levels, for example 1, 8 and 32 concurrent callers, fixed per journey rather than "as many as possible" |
| Warm or cold | Both recorded separately; a cold run includes connection establishment and first-query planning |
| Data volume | An approved dataset size per institution, including a realistic number of accounts, applications and controls |
| Duration | A fixed steady-state window after a warm-up, with the sample count recorded |
| Environment | An approved environment, identical for every run and identified in the report; workstation figures are labelled as such |
| Isolation | One journey per run unless a mixed profile is explicitly approved |

### D16-C Where the values live

| Item | Recommendation |
| --- | --- |
| Target values | Configuration or an approved document versioned with the release, never hard-coded in a test |
| Enforcement | A separate gated suite that reads the approved profile and fails only against it; it is excluded from the ordinary gate until the environment exists |
| Reporting | The evidence report states the profile identity, environment identity, sample counts and every percentile it measured |
| Regression | A run is compared with the previous run of the same profile, so a degradation is visible before it breaches the target |

### D16-D Recovery objectives

| Term | What it must state |
| --- | --- |
| RPO | Expressed in committed financial transactions, not in minutes alone. No RPO may permit silent loss of a posted journal, an idempotency outcome or an audit row |
| RTO | Detection, decision, environment availability, restore, service start-up and verification, each with its own allowance |
| Mechanisms required before any RPO claim | Continuous WAL archiving with retention, a retained base backup, an off-site copy, and a restore that is proven from a fresh cluster |
| Mechanisms required before any RTO claim | A rehearsed restore into an environment that then serves traffic, with the elapsed time recorded per stage |
| Prohibited | An RPO or RTO inferred from a workstation dump duration, or asserted from a configuration that does not exist |

### D16-E What may never be asserted from the current evidence

- No capacity figure: nothing here has been run against a concurrent load profile.
- No latency objective met: no accepted objective exists yet.
- No RTO or RPO: no WAL archiving, no off-site copy and no restore-to-timestamp drill.
- No availability or failover claim: single-node compose stack with no replica.
- No production data-volume claim: the observed row counts are development and test residue.

### D16-F Drill calendar

| Drill | Cadence to approve |
| --- | --- |
| Dump and restore round trip, all databases | Every verification pass, as `scripts/restore-drill.ps1` does now |
| Restore into a fresh cluster, then serve the smoke journey | Per release, on the approved environment |
| Restore to a timestamp inside the retention window | Per release once archiving exists |
| Loss of one service database with dependent services live | Per release once messaging and replay are implemented |

## 6. Constraints

- Do not put a latency or capacity number into a test, a document or a dashboard before it is approved.
- Do not treat the observation table above as a baseline that a future run must not exceed; it is one host on one day.
- Do not claim recovery evidence from a dump that has never been restored.
- Do not measure against production data, or copy production data into a development environment to measure.
- This brief does not approve the statement retention policy (`P1-E04-D15`), the closure conditions (`P1-E04-D13`) or
  the ledger's locking strategy (`P1-E05-D07`).

## 7. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Latency, throughput and error-rate targets per journey | Product/Platform | Operational commitment with a cost |
| The approved load profile and dataset volumes | Platform | Requires the approved environment |
| RTO and RPO values, including the cost of each tier | Product/Platform | Business continuity decision |
| Archive, retention and restoration of historical partitions | Compliance/DBA | Records policy |
| Alert thresholds and dashboard targets | Operations | Derived from the approved objectives |
| Disaster-recovery site and failover model | Platform | Phase 2 platform scope |

## 8. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending | |
| Product | | Pending: approve the measured journey set and the objective values | |
| Platform | | Pending: approve the measurement contract, the environment and the recovery mechanisms | |

## 9. Implementation notes

1. Keep `scripts/restore-drill.ps1` in the verification set it is in now: it is the only recovery evidence that runs
   without a decision.
2. When the profile is approved, add one suite that reads it and asserts the percentiles; do not add thresholds to the
   ordinary database suites, which must stay deterministic on any workstation.
3. Record the profile identity and environment identity in every future evidence report, and keep the observation
   tables labelled as observations.
4. Add WAL archiving and a retained base backup to the compose stack as a drill target, then prove one restore to a
   timestamp before any RPO is published.
5. Extend the drill calendar to the ledger journeys as `P1-E05` lands, so the first posting measurement is not the first
   time the profile is discussed.
