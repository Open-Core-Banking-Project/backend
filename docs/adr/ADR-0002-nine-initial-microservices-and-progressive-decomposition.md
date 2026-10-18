---
id: ADR-0002
title: "Nine initial microservices and progressive decomposition"
status: Accepted
date: 2026-10-16
owners: [Architecture]
required_by: "Phase 0 - Repository and Build Foundation; Phase 1 - Institution and Tenancy"
architecture_item: 2
referenced_by:
  - docs/implementation/phase-0-engineering-foundation/01-repository-and-build.md
  - docs/implementation/phase-1-banking-foundation/01-institution-and-tenancy.md
supersedes: []
approved_by: [Architecture]
approved_on: 2026-10-16
---

# ADR-0002: Nine initial microservices and progressive decomposition

## Status

`Accepted` 2026-10-16. The nine-service topology, the module-first rule, the extraction criteria and the contract and building-block prohibitions are binding on every service and phase.

Approval recorded on the instruction of the repository owner (`adamsondamilola <adamsondamilola@gmail.com>`) against the owner named in the front matter. Replace the role name with the individual approver identity when the approval record is formalised.

## Context

- `AGENTS.md` (`## Architecture`) fixes nine coarse-grained, independently deployable microservices and permits the remaining logical domains to begin as modules inside them. It forbids a tenth deployable service without an approved boundary and operational justification, and forbids sharing internal models, bypassing contracts or accessing another service's database.
- `README.md` describes twenty logical service domains and states that not every domain must be deployed separately in the first release.
- `BACKEND_ARCHITECTURE.md` (`## Initial microservices`, `## Per-microservice structure`, `## Building-block policy`, `## Database topology`) is the target structure, including the approved internal module layout for Channels and Engagement.
- `01-institution-and-tenancy.md` allocates capability ownership across five services, so the topology decision gates the tenancy epic.
- `docs/implementation/README.md` states that later phases keep the nine services and that a new deployable microservice requires an ADR and operational justification.
- Requirement and story traceability: `ARC-004`, `ARC-006`, `ARC-007`, `ARC-009`, `US-079`.

## Decision

1. **Approved initial deployable set.** Exactly nine independently deployable services, named and scoped as follows. These names are the repository directory names, project prefixes and ownership units.

| Service | Directory | Owns |
| --- | --- | --- |
| Core Banking and Ledger | `src/Services/CoreLedger` | Institution and platform configuration, ledger, balances, products, accounts, cash and branch modules |
| Customer and Onboarding | `src/Services/CustomerOnboarding` | Parties, customers, KYC, onboarding applications, privacy data |
| Identity and Access | `src/Services/IdentityAccess` | Authentication, sessions, devices, roles, permissions, grants, entitlement versions |
| Payments and VTU | `src/Services/PaymentsVtu` | Payment orchestration, transfers, bills, VTU, virtual accounts |
| Channels and Engagement | `src/Services/ChannelsEngagement` | USSD, WhatsApp, chatbot, CMS, notifications, campaigns, human handoff |
| Operations and Workflow | `src/Services/OperationsWorkflow` | Workflow definitions and instances, approvals, maker-checker, cases, SLAs |
| Risk and Reporting | `src/Services/RiskReporting` | Risk, fraud, AML, sanctions, reporting read models, documents and statements |
| Integration Hub | `src/Services/IntegrationHub` | Provider registry, adapters, canonical contracts, webhook intake and dispatch |
| Audit and Reconciliation | `src/Services/AuditReconciliation` | Immutable audit, trace graph, reconciliation, settlement, recovery evidence |

2. **Module first, service later.** Every remaining logical domain from `README.md` starts as an explicit module inside one of the nine services, using the approved Channels and Engagement module layout as the pattern: `Domain/Modules/<Module>` and `Application/Modules/<Module>`, coupled only through application interfaces and local domain events. Modules must not share persistence classes.
3. **Extraction criteria.** A module becomes a candidate deployable service only when at least one of these is demonstrated with evidence: independent scaling need, isolation or compliance requirement, distinct data ownership, distinct availability target, or a release cadence that the host service cannot satisfy. Extraction requires its own ADR and an operational justification.
4. **No tenth service without an ADR.** Adding a deployable service, splitting an existing one or merging two requires an accepted ADR that states the boundary, data ownership, migration approach and operational cost. This applies equally to analytical, AI and reporting workloads (see [ADR-0014](ADR-0014-reporting-read-models-and-regulatory-lineage.md)).
5. **Cross-service interaction.** Synchronous calls only where an immediate response is required; durable events for confirmed facts and asynchronous work. No service references another service's `Domain`, `Application`, `Infrastructure` or migration assembly, reads another service's database, or shares EF entities as contracts. Only deliberately public `Contracts` projects and the domain-neutral building blocks may cross a boundary.
6. **Building blocks.** The approved shared set is limited to `src/BuildingBlocks/*` as enumerated in the architecture document. Domain models, shared EF entities, a generic business-service layer, a universal repository and cross-service data access are prohibited there. Duplication is preferred over accidental domain coupling.
7. **Enforcement.** Boundary rules are enforced by automated tests in `tests/CoreBanking.ArchitectureTests`, not by review alone.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| One deployable service per logical domain (twenty) at first release | Operational complexity with no demonstrated need; contradicts progressive decomposition |
| A single modular monolith | Loses service-owned data, independent deployment and isolation guarantees that later phases depend on |
| Ten or more services including cards, lending and treasury from the start | No approved boundaries, owners or operational evidence exist for them |
| Shared database with a schema per domain across services | Breaks the ownership model and cross-service isolation |

## Consequences

- **Enables:** a stable repository, CI, ownership and migration baseline for every later phase; honest scope for Phases 2 to 5, which extend modules before extracting services.
- **Constrains:** reporting, analytical and AI workloads must justify any new deployable boundary; module boundaries must be maintained deliberately inside services.
- **Cost:** explicit module discipline and architecture tests inside each service, plus an ADR whenever a boundary moves.
- **Out of scope:** the physical deployment topology and hosting platform, which belong to [ADR-0013](ADR-0013-deployment-migration-backup-restore-rto-and-rpo.md).

## Compliance and evidence

- Architecture tests failing on a forbidden project reference, a namespace outside the naming pattern, or a contract that exposes EF entities.
- Solution review against the architecture document during Phase 0 acceptance.
- Recorded evidence for approval: the nine-service list above, the module inventory per service, and the extraction criteria.

## Open questions

- The module inventory per service for Phase 1, which each epic declares in its own scope section.
- Whether risk, fraud and sanctions modules eventually need an isolation boundary for compliance reasons.
- Whether analytical storage becomes a tenth service or remains a module (deferred to [ADR-0014](ADR-0014-reporting-read-models-and-regulatory-lineage.md)).

## Approval gate

Satisfied 2026-10-16. The nine service directories, project references and architecture tests may now be created. A tenth deployable service still requires its own accepted boundary ADR; this ADR grants no exception.

## Related documents

- [ADR index](README.md)
- [ADR-0003](ADR-0003-monorepo-solution-and-project-organisation.md) - repository layout expressing these boundaries
- [ADR-0004](ADR-0004-database-per-service-and-tenant-isolation.md) - data ownership and isolation
- [AGENTS.md](../../AGENTS.md) - `## Architecture`
- [BACKEND_ARCHITECTURE.md](../architecture/BACKEND_ARCHITECTURE.md) - `## Initial microservices`, `## Building-block policy`
