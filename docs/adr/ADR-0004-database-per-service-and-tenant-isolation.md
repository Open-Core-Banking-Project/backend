---
id: ADR-0004
title: "Database-per-service and tenant-isolation model"
status: Accepted
date: 2026-10-16
owners: [Architecture, Database, Security]
required_by: "Phase 0 - Local Infrastructure; Phase 1 - Institution and Tenancy"
architecture_item: 4
referenced_by:
  - docs/implementation/phase-0-engineering-foundation/02-local-infrastructure.md
  - docs/implementation/phase-1-banking-foundation/01-institution-and-tenancy.md
supersedes: []
approved_by: [Architecture, Database, Security]
approved_on: 2026-10-16
---

# ADR-0004: Database-per-service and tenant-isolation model

## Status

`Proposed` - decision drafted and ready for owner approval. Effective only when the owners listed below record `Accepted`. Local development infrastructure may proceed under `02-local-infrastructure.md`; production provisioning and connection topology may not.

## Context

- `BACKEND_ARCHITECTURE.md` (`## Database topology`, `## Logical database structures`, `## Common database conventions`) fixes one logically separated database per service: `core_ledger_db`, `customer_onboarding_db`, `identity_access_db`, `payments_vtu_db`, `channels_engagement_db`, `operations_workflow_db`, `risk_reporting_db`, `integration_hub_db` and `audit_reconciliation_db`.
- `AGENTS.md` (`## Tenancy and data ownership`, `## Persistence and migrations`) requires every tenant-owned record and operation to carry a trusted institution identifier resolved from trusted infrastructure context rather than an untrusted request field, requires enforcement in both authorisation and persistence queries, requires cross-tenant negative tests for every tenant-owned resource, forbids a fallback tenant in production, and forbids sharing tables across bounded contexts as an integration mechanism.
- `02-local-infrastructure.md` requires a separate database principal per service, denies a service principal access to another service's database, and keeps development administration credentials separate from application credentials.
- `01-institution-and-tenancy.md` owns tenant provisioning and isolation evidence.
- Requirement and story traceability: `ARC-004`, `ARC-008`, `DAT-005`, `DAT-007`, `NFR-RES-001`, `NFR-DR-001`, `SEC-007`, `SEC-018`, `US-072`, `US-079`, `US-080`.

## Decision

1. **Topology.** Nine logical databases, one per service, carrying exactly the names in the architecture document. Each service owns its schemas; no schema is shared between services, and no service reads or writes a table it does not own.
2. **Principals.** Per environment, each service receives an application principal scoped to its own database, a migration principal with schema-change rights on that database only, and, where a governed read feed is approved, a read-only principal. Application configuration never contains a superuser. Development administration credentials are separate from application credentials.
3. **Tenant column.** Every tenant-owned table carries a non-null institution identifier column, indexed, and set from trusted context on write. Composite uniqueness and foreign keys include the institution column so a reference cannot cross tenants.
4. **Trusted tenant context.** Institution identity is resolved from the authenticated session or service identity, never from a request body, query string or client-supplied header. Missing, conflicting or unverifiable tenant context fails closed. A fallback or default tenant is prohibited in production code.
5. **Enforcement in persistence.** Reads are constrained by a mandatory institution predicate applied in the persistence layer (EF Core global query filter or equivalent), in addition to authorisation checks in the application layer. A UI filter is never treated as a boundary.
6. **Defence in depth.** Application-level enforcement is the primary control for Phase 0 and Phase 1. Adopting PostgreSQL row-level security on tenant-owned tables is a required pre-production security gate; the evaluation and decision are recorded as an amendment to this ADR before the first production institution is onboarded.
7. **Cross-service access.** No cross-service database access, no cross-service foreign keys and no shared tables. Integration happens through versioned contracts and durable events. Reporting and analytics consume governed feeds or projections owned by the consuming service (see [ADR-0014](ADR-0014-reporting-read-models-and-regulatory-lineage.md)).
8. **Migrations.** Owned by the service's `Infrastructure` project, executed by the controlled migration job in [ADR-0013](ADR-0013-deployment-migration-backup-restore-rto-and-rpo.md), and append-only once released.
9. **Environment isolation.** Distinct databases, principals and credentials per environment. Local defaults are synthetic, are marked development-only and cannot resolve to production endpoints.
10. **Sensitive data.** Platform storage encryption plus field-level protection for classified identifiers per [ADR-0011](ADR-0011-secret-key-and-certificate-management.md). Retention and partitioning policies are declared per table by the owning service, with audit and ledger tables partitioned by period.
11. **Evidence.** Every tenant-owned resource has cross-tenant negative tests, and architecture or database tests fail when a principal can reach another service's tables.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| One shared database with a tenant column across services | Destroys the service-owned data boundary and makes cross-service leakage hard to prevent |
| Schema-per-service in one database per environment | Weaker isolation than the documented logical database-per-service model, and shared server-level blast radius |
| A database per institution | Highest isolation, high operational cost, and no approved institution-scale target yet |
| Enforcing tenancy only in the UI or only in application authorisation | A UI filter is not a boundary, and authorisation alone does not protect bulk queries and background jobs |
| Deferring the tenant column until tenancy is implemented | Retrofitting the column, indexes and constraints into financial tables is riskier than carrying it from the start |

## Consequences

- **Enables:** independently owned schemas and migrations, per-service credentials, and a tenancy model that can be tested with negative cases.
- **Constrains:** every tenant-owned table, index, unique constraint and foreign key must include the institution key; every query path must be tenant-scoped; reporting must use governed feeds rather than joins.
- **Cost:** local development must run and reset nine databases with separate principals; row-level security adoption carries later migration cost if it is approved.
- **Out of scope:** physical hosting and environment topology (ADR-0013), reporting read models (ADR-0014) and retention periods.

## Compliance and evidence

- Cross-tenant negative tests for every tenant-owned resource, as required by `AGENTS.md`.
- Architecture and database tests that fail when a service principal reaches another service's tables, and when a tenant-owned entity lacks the institution key.
- Migration review evidence showing schema, principal and constraint changes per service.
- Recorded evidence for approval: the database and principal inventory, the tenant column convention, and one worked cross-tenant denial test.

## Open questions

- Whether row-level security is adopted in addition to application-level predicates, and on which tables (mandatory pre-production gate).
- Whether a per-institution database deployment mode is offered commercially.
- Retention, partitioning and archival periods per table class.
- Whether reporting receives read-only replicas or governed extract feeds only.

## Approval gate

Satisfied 2026-10-16 for the topology, principals, tenant model, enforcement approach and migration ownership. Still outstanding before production: the row-level security evaluation and adoption decision, retention and partitioning periods, the production hosting topology in [ADR-0013](ADR-0013-deployment-migration-backup-restore-rto-and-rpo.md), and a production provisioning plan confirming the database and principal inventory.

## Related documents

- [ADR index](README.md)
- [ADR-0002](ADR-0002-nine-initial-microservices-and-progressive-decomposition.md) - service ownership boundaries
- [ADR-0011](ADR-0011-secret-key-and-certificate-management.md) - credential and field-level protection
- [ADR-0013](ADR-0013-deployment-migration-backup-restore-rto-and-rpo.md) - migration execution, backup and recovery
- [02-local-infrastructure.md](../implementation/phase-0-engineering-foundation/02-local-infrastructure.md)