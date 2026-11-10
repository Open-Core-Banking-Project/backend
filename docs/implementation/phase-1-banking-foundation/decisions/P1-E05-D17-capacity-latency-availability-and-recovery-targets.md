---
decision: P1-E05-D17
title: Ledger capacity, latency, availability, RTO and RPO targets
epic: P1-E05 Core Ledger and Financial Posting
owners: [Product, Platform]
required_by: Before release testing
status: Proposed
brief_date: 2026-11-09
related: [P1-E04-D16, P1-E11-D11, P1-E12-D07, P1-E12-D08, P1-E12-D09]
decisions: [P1-E05-D17]
---

# Decision brief: ledger release targets

Performance and recovery tests need approved pass conditions. Observations from a developer workstation are useful
diagnostics but cannot become production SLO, RTO or RPO commitments.

## 1. Decision requested

Product and Platform must approve the workload model, latency percentiles, contention profile, availability objective,
degraded-mode rules, RTO/RPO and recovery verification point for each institution/service tier.

## 2. Inputs owners must provide

| Target area | Required input |
| --- | --- |
| Volume | Accounts, transactions/day, peak postings/second, lines/posting, history/statement queries |
| Shape | Event/component mix, hot-account concentration, tenant distribution, EOD overlap, reversal/hold rates |
| Latency | p50/p95/p99 for posting, idempotent replay, balance, history, hold and reversal |
| Availability | Measurement window, exclusions, dependency budget and permitted read-only/degraded behaviour |
| Recovery | RPO, RTO, detection/decision assumptions, backup/WAL/off-site requirements and restore scale |
| Growth | Forecast horizon, headroom, data-retention/partition assumptions and re-test trigger |

## 3. Recommended safety conditions

- The posting latency clock distinguishes accepted request, database commit and response delivery. A response timeout
  after commit is measured separately and resolved through idempotent status lookup.
- Load tests use real PostgreSQL and include hot-account locking, duplicate keys, concurrent debits, reversals, holds,
  EOD and outbox lag. Passing throughput never compensates for an invariant failure.
- Saturation must fail closed without overspend, duplicate posting, unbalanced journal, skipped checkpoint or stale
  success response.
- Recovery evidence includes WAL/off-site loss assumptions and reconciles journals, lines, movements, balances,
  idempotency, reversals, periods, EOD checkpoints and outbox before traffic reopens.
- RTO includes detection, authority, provisioning, restore, verification and controlled reopen—not only database restore.
- Every result records hardware/topology, dataset, commit, configuration, test duration and percentile method.

## 4. No figures proposed

This brief deliberately contains no numeric target. SRS examples and local restore durations are not owner-approved
commitments. The eventual approval must publish units, scope and measurement method alongside every number.

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | | Pending | |
| Product | | Pending: provide workload and service targets | |
| Platform/SRE | | Pending: approve topology, availability and recovery targets | |

