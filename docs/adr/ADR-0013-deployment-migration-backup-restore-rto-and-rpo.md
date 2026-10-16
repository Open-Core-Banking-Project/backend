---
id: ADR-0013
title: "Deployment, migration execution, backup, restore, RTO and RPO"
status: Proposed
date: 2026-10-16
owners: [Platform Engineering, Database, Operations]
required_by: "Phase 1 - Operations and Release Readiness"
architecture_item: 13
referenced_by:
  - docs/implementation/phase-1-banking-foundation/12-operations-and-release-readiness.md
supersedes: []
approved_by: []
approved_on: null
---

# ADR-0013: Deployment, migration execution, backup, restore, RTO and RPO

## Status

`Proposed` - artifact, migration, backup and restore decisions drafted and ready for owner approval. Effective only when the owners listed below record `Accepted`. The hosting platform and the numeric RTO and RPO targets remain open and are listed as such below; until they are approved, no document may claim production readiness or a recovery target.

## Context

- `AGENTS.md` (`## Persistence and migrations`) makes migrations append-only once released, requires an explicit rollout, backfill, verification and rollback or forward-fix plan for destructive or long-running changes, forbids holding a database transaction open across network calls, and forbids deleting, rewriting or truncating financial or audit data in an ordinary migration.
- `BACKEND_ARCHITECTURE.md` (`## Deployment model`, `## Configuration model`) requires immutable versioned container images, migrations through a controlled deployment step rather than opportunistically from every replica, graceful shutdown and health checks, horizontally scalable stateless replicas, coordination among background consumers, version-controlled infrastructure, promotion with build, tests, security checks, approval and retained evidence, and documented rollback or forward-fix behaviour.
- `12-operations-and-release-readiness.md` requires decisions on deployment platform, environment topology and promotion model; migration execution; backup, restore, RTO and RPO; service-level objectives, capacity and availability tiers; and software supply-chain controls. It defines release governance, CI/CD promotion, migration governance, service health, capacity, security operations and runbook expectations, and its definition of done requires a release rehearsal with reviewable evidence.
- `02-local-infrastructure.md` requires one documented command to start local dependencies, safe reset, backup and restore verification, and that local configuration cannot target production.
- `11-audit-traceability-and-recovery.md` requires a backup catalogue, recovery-orchestration evidence, integrity verification and cross-service reconciliation after restore.
- Requirement and story traceability: `NFR-PERF-001`, `NFR-PERF-002`, `NFR-SCAL-001`, `NFR-AVAIL-001`, `NFR-RES-001`, `NFR-RES-002`, `NFR-DR-001`, `NFR-DR-002`, `NFR-MNT-001`, `SEC-016`, `SEC-017`, `SEC-018`, `US-074`, `US-079`, `US-080`.

## Decision

### 1. Local and CI environment

Container-based orchestration with Docker Compose under `deploy/compose`, which is the single documented start command required by `02-local-infrastructure.md`. Approved component floors: PostgreSQL 16 or later, Redis 7 or later, a Kafka-compatible broker, and an OpenTelemetry collector. Components are pinned by version tag, never `latest`. Runtime configuration comes from environment variables with a committed `.env.example`; no real secrets or production configuration are committed. Development defaults are synthetic and cannot resolve to production endpoints.

### 2. Artifacts and promotion

One immutable, versioned container image per service, containing that service's API and only the workers that share its ownership and release boundary. Images are built once and promoted by digest; no environment rebuilds from source. Tags follow `<service>:<semver>+<git-sha>`. Runtime images use a non-root user and exclude build toolchains. The image digest, migration version and configuration version of every deployed component are recorded as release evidence.

### 3. Migration execution

- Migrations run as a **separate deployment job per service**, never from an application replica and never at application startup.
- Exactly one migration runner may execute against a given database at a time, guarded by a database lock; a failed migration fails the deployment before traffic shifts.
- The deployed migration assembly version must match the deployed service version.
- Migrations are owned by the bounded context that owns the schema, remain append-only once released, and are corrected by a new migration.
- Changes are expand/contract: additive schema and backfill first, removal of the old shape in a later release, so the previous application version keeps working during rollout.
- Destructive, long-running or data-correcting changes require an approved plan stating rollout, backfill, verification query, expected duration and rollback or forward-fix. Forward-fix is the default; rollback is offered only for genuinely reversible changes.
- Every migration ships with a verification query and its recorded result.

### 4. Backup scope

| Backed up | Method | Notes |
| --- | --- | --- |
| The nine service PostgreSQL databases | Continuous WAL archiving for point-in-time recovery plus scheduled full backups | Each database is owned by one service; `pg_dump` alone is not sufficient for PITR |
| Object storage for documents, statements and evidence | Versioned bucket plus scheduled replication | Includes legal-hold material |
| Repository configuration, release artifacts and migration history | Version control plus artifact retention | Release evidence is retained per release |

Explicitly excluded because they are rebuildable, with a documented rebuild procedure per store: Redis, search indexes, reporting and analytical read models, and telemetry data. A rebuild procedure is a prerequisite for excluding a store.

### 5. Restore, verification and recovery order

- Restore rehearsals are executed into an isolated environment from the backup catalogue, at least once per release cycle and always before a release that changes a migration or a critical invariant.
- Restore targets a single consistency point across the nine databases so cross-service references remain consistent.
- Post-restore verification is mandatory and recorded: constraint and row-count checks, ledger-to-balance reconciliation, ledger-to-event reconciliation, idempotency and outbox state inspection, and service readiness checks.
- Repository default recovery order: institution and platform configuration and Identity and Access, then Core Banking and Ledger, then dependent services, with read models and other rebuildable stores rebuilt last. Each service records its own entry in the recovery plan.
- Measured recovery time and data loss per rehearsal are recorded, so that RTO and RPO can be confirmed rather than asserted.

### 6. Promotion gate

A release is promotable only when all of the following are attached as evidence: build and test results, formatting and analyzer checks, dependency and image scanning, migration verification, restore rehearsal results where required, security approvals, and a documented rollback or forward-fix path for code, configuration, contracts and migrations. Promotion approval and the evidence bundle are retained.

### 7. Not decided here

The following remain owner decisions and must be recorded as an amendment to this ADR: hosting platform and production environment topology; data residency and network placement; numeric RTO and RPO per service and data class; service-level objectives, capacity model and availability tiers; go-live data migration and cutover strategy; and the supply-chain signing or attestation tooling.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| Let each application replica apply migrations at startup | Race conditions, unpublished schema versions and an uncontrolled migration step; prohibited by the architecture document |
| Rebuild images per environment | Breaks artifact immutability and promotion evidence |
| Roll back by restoring a pre-release database backup | Loses data written since the backup; forward-fix or compensating migration is safer |
| Exclude audit, reconciliation and object storage from backup | Audit evidence and recovery obligations do not permit it |
| Back up each database independently with no single consistency point | Produces cross-service reference and reconciliation breaks after restore |
| Claim RTO and RPO from the local rehearsal | Local timings are not production evidence |
| Kubernetes-first local development | Raises the local barrier to entry with no Phase 0 requirement |

## Consequences

- **Enables:** reproducible local and CI environments today; a controlled migration step; a backup and restore model that can be rehearsed and evidenced before any production target exists.
- **Constrains:** every service needs a migration job definition, a recovery-plan entry, a rebuild-or-restore classification for each data store and runbook coverage; every release needs an evidence bundle.
- **Cost:** migration jobs, restore rehearsals and rebuild procedures are ongoing operational work, not one-off setup.
- **Out of scope:** hosting platform, RTO and RPO numbers, SLOs and go-live cutover, all listed in section 7.

## Compliance and evidence

- One documented command starts local dependencies and is verified from a clean machine.
- Migration job evidence: version match, lock behaviour, verification query result and fail-before-traffic behaviour.
- Restore rehearsal report with measured recovery time, data loss, verification queries and reconciliation results.
- Release evidence bundle per promotion, retained.
- Runbook coverage per service for deploy, rollback or forward-fix, restore and rebuild of non-authoritative stores.

## Open questions

- Hosting platform and whether managed PostgreSQL is used, which determines how PITR and restore rehearsals are operated.
- Whether backup and restore are executed by platform tooling or by the database service.
- Numeric RTO and RPO per service and data class, and whether they differ per institution.
- Whether a disaster-recovery environment runs continuously or is provisioned on demand.
- Data migration and cutover strategy for the first live institution.

## Approval gate

Do not commit deployment pipelines, environment configuration, migration automation, backup configuration or any claim of production readiness or recovery target until this ADR is `Accepted`. Local development automation marked development-only may continue under `02-local-infrastructure.md`. Sections 1 to 6 may be accepted independently of section 7 if the owners choose to do so; record that split in the front matter.

Approval required from **Platform Engineering** and **Database**, with **Operations** confirming the runbook and rehearsal obligations, and the hosting platform and RTO and RPO targets confirmed by the stakeholders listed in `BACKEND_ARCHITECTURE.md`. Record acceptance by setting `status: Accepted` and filling `approved_by` and `approved_on`, then updating the [index](README.md).

## Related documents

- [ADR index](README.md)
- [ADR-0004](ADR-0004-database-per-service-and-tenant-isolation.md) - database topology and migration ownership
- [ADR-0010](ADR-0010-observability-audit-evidence-redaction-and-retention.md) - health, alerting and evidence retention
- [ADR-0011](ADR-0011-secret-key-and-certificate-management.md) - secret availability during deployment and recovery
- [11-audit-traceability-and-recovery.md](../implementation/phase-1-banking-foundation/11-audit-traceability-and-recovery.md)
- [12-operations-and-release-readiness.md](../implementation/phase-1-banking-foundation/12-operations-and-release-readiness.md)
