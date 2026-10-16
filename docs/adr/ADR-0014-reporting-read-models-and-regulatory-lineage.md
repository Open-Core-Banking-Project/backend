---
id: ADR-0014
title: "Reporting read models, regulatory lineage and analytical isolation"
status: Proposed
date: 2026-10-16
owners: [Data Governance, Risk and Reporting, Architecture]
required_by: "Phase 1 - Documents and Statements; Phase 2 - Regulatory and Scheduled Reporting; Phase 5 - Enterprise Data Governance"
architecture_item: 14
referenced_by:
  - docs/implementation/phase-2-regulated-operations/10-regulatory-and-scheduled-reporting.md
  - docs/implementation/phase-5-optimisation/01-enterprise-data-governance-lineage-and-ownership.md
supersedes: []
approved_by: []
approved_on: null
---

# ADR-0014: Reporting read models, regulatory lineage and analytical isolation

## Status

`Proposed` - not approved. `10-regulatory-and-scheduled-reporting.md` and `01-enterprise-data-governance-lineage-and-ownership.md` require this decision by topic name. The identifier `ADR-0014` was assigned by the [ADR index](README.md).

## Context

- `AGENTS.md` (`## Tenancy and data ownership`, `## Audit and observability`, `## Prohibited shortcuts`) requires each bounded context to own its schema or database and to publish controlled APIs or events; forbids treating a cache, report, search index or provider response as the ledger source of truth; and requires ownership of shared building blocks to stay domain-neutral.
- `05-core-ledger-and-posting.md` states that general-ledger financial statements and regulatory reporting belong to Risk and Reporting, which consumes governed ledger outputs, and that read models, caches, reports and exported statements cannot authorise a debit or replace ledger state. `10-documents-and-statements.md` states that statement totals must not be generated from account summaries, reporting tables, analytics, Redis, UI transaction lists or document artifacts.
- `10-regulatory-and-scheduled-reporting.md` requires "Reporting read models and lineage" as a decision, alongside regulatory country packs and report rendering and distribution.
- `01-enterprise-data-governance-lineage-and-ownership.md` requires "Analytical platform and tenant isolation" and "Metadata and lineage model" as decisions, and Phase 5 epics 2 to 4 depend on approved analytical-store, ingestion, isolation and lineage choices.
- `BACKEND_ARCHITECTURE.md` requires this decision as architecture item 14 and lists the Risk and Reporting database among the nine logical databases.
- Requirement and story traceability: `RPT-001` to `RPT-008`, `REG-001` to `REG-003`, `REP-001` to `REP-003`, `DAT-001` to `DAT-008`, `SEC-*`, `AUD-001` to `AUD-004`, `NFR-OBS-001`, `NFR-OBS-002`, `US-047`, `US-065`, `US-066`.

## Decision

Not yet decided. This ADR must settle:

- Read-model architecture: which projections Risk and Reporting owns, how they are fed (events, ledger queries or controlled extracts), and the freshness, watermark and rebuild strategy per projection.
- Isolation: whether the analytical or reporting workload runs inside `risk_reporting_db`, in a separate database and cluster, or in a separate deployable service, and whether a tenth service boundary is required (coordination point with [ADR-0002](ADR-0002-nine-initial-microservices-and-progressive-decomposition.md)).
- Tenant isolation in analytical stores and reporting outputs, including how institutions are separated and how cross-institution aggregation is prevented or explicitly approved.
- Regulatory lineage: how each report field traces to source requirement, service, posting or transaction, and how lineage is versioned and evidenced.
- Reporting data retention, restatement history and reproducibility of a previously published return.
- Access and purpose governance for analytical data, including masking, aggregation thresholds and the treatment of personal data (Phase 5 data-governance dependency).
- Report rendering, distribution and secure delivery boundaries, and who owns country-pack rules.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| Reporting directly against each service database | Breaks service-owned data boundaries and cross-service isolation |
| Treating the analytical store as the source of truth for statements or balances | Prohibited; ledger and posted records remain authoritative |
| A single shared analytical schema across all institutions without isolation rules | Fails tenant-isolation and purpose-limitation expectations |
| Generating report totals from caches or pre-aggregated UI data | Cannot be reproduced or reconciled to posted records |

## Consequences

- The projection set defines what Phase 1 statements and Phase 2 regulatory returns can be built from, and constrains Phase 5 analytics.
- Choosing an analytical platform also selects its tenancy, retention, recovery and access controls, interacting with [ADR-0004](ADR-0004-database-per-service-and-tenant-isolation.md) and [ADR-0013](ADR-0013-deployment-migration-backup-restore-rto-and-rpo.md).
- Lineage becomes a maintained artifact rather than documentation, which adds a traceability obligation per report field.

## Compliance and evidence

- Reconciliation evidence between each projection and authoritative posted records, with control totals.
- Lineage evidence per published report field, and reproducibility evidence for a restated return.
- Tenant-isolation negative tests for analytical queries and reporting outputs.
- Rebuild evidence proving a projection can be reconstructed without touching authoritative state.

## Open questions

- Whether analytical storage becomes a separate deployable service or remains a module boundary.
- Whether country-pack rules live as configuration, code or a governed rules engine.
- Which retention and restatement periods apply to regulatory returns, pending country-pack approval.

## Approval gate

Do not build reporting projections, choose an analytical platform, or publish a regulated report contract until this ADR is `Accepted`. Statement-data contracts sourced from the ledger may be drafted under `05-core-ledger-and-posting.md`.

## Related documents

- [ADR index](README.md)
- [ADR-0002](ADR-0002-nine-initial-microservices-and-progressive-decomposition.md) - whether a new deployable boundary is required
- [ADR-0004](ADR-0004-database-per-service-and-tenant-isolation.md) - tenant isolation and data ownership
- [ADR-0013](ADR-0013-deployment-migration-backup-restore-rto-and-rpo.md) - retention, backup and recovery of analytical stores
- [10-regulatory-and-scheduled-reporting.md](../implementation/phase-2-regulated-operations/10-regulatory-and-scheduled-reporting.md)
- [01-enterprise-data-governance-lineage-and-ownership.md](../implementation/phase-5-optimisation/01-enterprise-data-governance-lineage-and-ownership.md)
