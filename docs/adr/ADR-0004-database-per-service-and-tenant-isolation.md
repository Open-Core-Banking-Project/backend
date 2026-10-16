---
id: ADR-0004
title: "Database-per-service and tenant-isolation model"
status: Proposed
date: 2026-10-16
owners: [Architecture, Database, Security]
required_by: "Phase 0 - Local Infrastructure; Phase 1 - Institution and Tenancy"
architecture_item: 4
referenced_by:
  - docs/implementation/phase-0-engineering-foundation/02-local-infrastructure.md
  - docs/implementation/phase-1-banking-foundation/01-institution-and-tenancy.md
supersedes: []
approved_by: []
approved_on: null
---

# ADR-0004: Database-per-service and tenant-isolation model

## Status

`Proposed` - not approved. `02-local-infrastructure.md` may provision local databases for development, but no production provisioning, connection topology or tenant-isolation mechanism may be committed until this ADR is `Accepted`.

## Context

- `BACKEND_ARCHITECTURE.md` (`## Database topology`, `## Logical database structures`, `## Common database conventions`) fixes one logically separated database per service: `core_ledger_db`, `customer_onboarding_db`, `identity_access_db`, `payments_vtu_db`, `channels_engagement_db`, `operations_workflow_db`, `risk_reporting_db`, `integration_hub_db` and `audit_reconciliation_db`.
- `AGENTS.md` (`## Tenancy and data ownership`, `## Persistence and migrations`) requires every tenant-owned record and operation to carry a trusted institution identifier, requires tenant identity to come from trusted infrastructure context rather than an untrusted request field, requires enforcement in application authorisation and persistence queries, requires cross-tenant negative tests for every tenant-owned resource, forbids a fallback tenant in production, and forbids sharing database tables across bounded contexts as an integration mechanism.
- `02-local-infrastructure.md` requires reproducible local PostgreSQL instances, explicit safety rules so local defaults cannot reach production endpoints, and no real customer data.
- `01-institution-and-tenancy.md` contains the tenant-isolation epics and evidence expectations.
- `BACKEND_ARCHITECTURE.md` lists "Physical PostgreSQL isolation and tenant-isolation model" under decisions awaiting stakeholder approval.
- Requirement and story traceability: `ARC-004`, `ARC-008`, `DAT-005`, `DAT-007`, `NFR-RES-001`, `NFR-DR-001`, `SEC-007`, `SEC-018`, `US-072`, `US-079`, `US-080`.

## Decision

Not yet decided. This ADR must settle:

- Physical isolation model: separate PostgreSQL clusters, instances, databases or schemas per service, per environment.
- How institution tenancy is enforced inside a shared database: row-level security, mandatory tenant predicate enforced by the persistence layer or a database principal per institution, and the fail-closed rule when tenant context is missing.
- Database principals per service and per environment, and the prohibition on one service's principal reading or writing another service's schema.
- Migration ownership, migration assembly placement, and who may execute migrations in each environment.
- Connection-string and secret sourcing, and the rule that local defaults cannot resolve to a production endpoint.
- Cross-service data access mechanism (controlled API or event-fed read model) and the prohibition on cross-service foreign keys and joins.
- Retention, partitioning and backup/RPO/RTO expectations per database, or the ADR that will own them (see [ADR-0013](ADR-0013-deployment-migration-backup-restore-rto-and-rpo.md)).

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| One shared database with a tenant column | Weakens the service-owned data boundary and makes cross-service leakage harder to prevent |
| Schema-per-service inside one database per environment | Weaker isolation than the architecture's logical database-per-service model |
| Fully separate clusters per institution | Highest isolation with operational cost that is not yet justified by an approved target |

## Consequences

- Local development must run and reset nine databases reproducibly, which fixes the local infrastructure epic's tooling.
- Cross-service queries become explicit contracts rather than joins, which affects reporting and reconciliation epics.
- Tenant isolation becomes testable through mandatory negative tests on every tenant-owned resource.
- Migration governance in `12-operations-and-release-readiness.md` depends on this ADR.

## Compliance and evidence

- Cross-tenant negative tests for every tenant-owned resource, as required by `AGENTS.md`.
- Architecture and database tests that fail when a principal writes another service's tables.
- Migration review evidence per service and per environment.
- A documented, verified check that local configuration cannot target production endpoints.

## Open questions

- Whether row-level security is used in addition to application-level predicates.
- Whether a per-institution database deployment mode is offered later.
- Who owns database-provisioning automation and how it is versioned.

## Approval gate

Do not commit production connection topology, database principals, migration execution policy or a tenant-isolation mechanism until this ADR is `Accepted`. Local, clearly marked development-only infrastructure may continue under `02-local-infrastructure.md`.

## Related documents

- [ADR index](README.md)
- [ADR-0002](ADR-0002-nine-initial-microservices-and-progressive-decomposition.md) - service ownership boundaries
- [ADR-0013](ADR-0013-deployment-migration-backup-restore-rto-and-rpo.md) - migration execution, backup, restore and recovery targets
- [AGENTS.md](../../AGENTS.md) - `## Tenancy and data ownership`, `## Persistence and migrations`
- [BACKEND_ARCHITECTURE.md](../architecture/BACKEND_ARCHITECTURE.md) - `## Database topology`, `## Logical database structures`
