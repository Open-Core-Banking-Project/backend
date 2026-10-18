# Architecture Decision Records

## Purpose

This directory holds the Architecture Decision Records (ADRs) for the backend. An ADR records one consequential decision: the context that forced it, the alternatives considered, the decision taken, and the consequences accepted.

`AGENTS.md` places approved ADRs at precedence level 3, below the current work item and the `AGENTS.md` instructions but above the architecture blueprint. `docs/implementation/README.md` requires an epic to name the ADRs it depends on, and requires an agent to inspect those ADRs before implementing.

Use [`template.md`](template.md) when creating a new ADR.

## Status values

| Status | Meaning |
| --- | --- |
| `Proposed` | Drafted and under review. Not binding. Implementation that depends on it must stop. |
| `Accepted` | Approved. Binding on all code, contracts and operations it covers. |
| `Rejected` | Considered and declined. The ADR stays for the record. |
| `Superseded by ADR-NNNN` | Replaced. The replacement and the reason are recorded in both records. |

Numbers are permanent. A superseded or rejected ADR is never deleted, renumbered or reused.

Acceptance is recorded in the ADR front matter: set `status: Accepted` and fill `approved_by` with the approving owners and `approved_on` with the date. A record whose front matter still shows an empty `approved_by` is not approved, whatever its status line says.

## Gate rule

Several epics state that an ADR "must be approved before production implementation", and `docs/implementation/README.md` says only an approved epic is a stable implementation contract. Therefore:

- An epic must not implement the behaviour governed by an ADR that is still `Proposed`.
- An agent that reaches this gate must stop and surface the decision rather than choose a value.
- Scaffolding and contract/test work that does not encode the undecided value may continue, as the epics allow.

Current gates:

- Satisfied 2026-10-16: `ADR-0001`, `ADR-0002`, `ADR-0003` (Phase 0 repository and build), `ADR-0004` (database topology, principals and tenant isolation), `ADR-0007` (messaging, outbox, inbox and replay), `ADR-0008` and `ADR-0009` (authentication, authorisation and entitlement invalidation), `ADR-0010` (observability, audit and retention approach) and `ADR-0011` (secrets, keys and certificates).
- Outstanding, decision drafted and awaiting approval: `ADR-0013` (deployment, migration execution, backup, restore, RTO and RPO) - required by Phase 1 operations and release readiness.
- Outstanding, scope only: `ADR-0005` (any production financial posting in Phase 1), `ADR-0006` (API versioning and problem details), `ADR-0012` (channel security boundaries) and `ADR-0014` (reporting read models and regulatory lineage).

## File naming

```text
ADR-NNNN-kebab-case-title.md
```

## Index

**Accepted 2026-10-16:** ADR-0001, ADR-0002, ADR-0003 (Phase 0 repository and build), ADR-0004 (database topology, principals and tenant isolation), ADR-0007 (messaging), ADR-0008 and ADR-0009 (authentication, authorisation and entitlement invalidation), ADR-0010 (observability, audit and retention approach) and ADR-0011 (secrets, keys and certificates).

**Decision-complete, awaiting owner approval:** ADR-0013 (deployment, migration execution, backup, restore, RTO and RPO).

**Scope only, awaiting an owner decision:** ADR-0005, ADR-0006, ADR-0012, ADR-0014.

Each accepted record states the narrow items that remain open, such as the production broker product, identity provider licensing, telemetry backends, retention periods, row-level security adoption and the production secret store. Those are tracked in the record's Approval gate section and must be closed before the corresponding production claim.

| ID | Title | Architecture item | Referenced by |
| --- | --- | --- | --- |
| [ADR-0001](ADR-0001-csharp-dotnet-only-backend-and-sdk-policy.md) | C#/.NET-only backend and SDK support policy | 1 | [01-repository-and-build](../implementation/phase-0-engineering-foundation/01-repository-and-build.md) |
| [ADR-0002](ADR-0002-nine-initial-microservices-and-progressive-decomposition.md) | Nine initial microservices and progressive decomposition | 2 | [01-repository-and-build](../implementation/phase-0-engineering-foundation/01-repository-and-build.md), [01-institution-and-tenancy](../implementation/phase-1-banking-foundation/01-institution-and-tenancy.md) |
| [ADR-0003](ADR-0003-monorepo-solution-and-project-organisation.md) | Monorepo, solution and project organisation | 3 | [01-repository-and-build](../implementation/phase-0-engineering-foundation/01-repository-and-build.md) |
| [ADR-0004](ADR-0004-database-per-service-and-tenant-isolation.md) | Database-per-service and tenant-isolation model | 4 | [02-local-infrastructure](../implementation/phase-0-engineering-foundation/02-local-infrastructure.md), [01-institution-and-tenancy](../implementation/phase-1-banking-foundation/01-institution-and-tenancy.md) |
| [ADR-0005](ADR-0005-ledger-consistency-balances-and-posting-boundary.md) | Ledger consistency, balances and posting transaction boundary | 5 | [05-core-ledger-and-posting](../implementation/phase-1-banking-foundation/05-core-ledger-and-posting.md) |
| [ADR-0006](ADR-0006-api-versioning-and-problem-details.md) | API versioning and problem-details conventions | 6 | None yet - assigned by list position |
| [ADR-0007](ADR-0007-event-envelope-broker-outbox-inbox-and-replay.md) | Event envelope, broker, outbox/inbox and replay | 7 | [02-local-infrastructure](../implementation/phase-0-engineering-foundation/02-local-infrastructure.md), [04-messaging-and-outbox](../implementation/phase-0-engineering-foundation/04-messaging-and-outbox.md), [01-institution-and-tenancy](../implementation/phase-1-banking-foundation/01-institution-and-tenancy.md) |
| [ADR-0008](ADR-0008-authentication-provider-token-model-and-service-identity.md) | Authentication provider, token model and service identity | 8 | [03-identity-and-permission-foundation](../implementation/phase-0-engineering-foundation/03-identity-and-permission-foundation.md), [02-identity-roles-and-permissions](../implementation/phase-1-banking-foundation/02-identity-roles-and-permissions.md), [01-institution-and-tenancy](../implementation/phase-1-banking-foundation/01-institution-and-tenancy.md) |
| [ADR-0009](ADR-0009-roles-permissions-contextual-scopes-and-invalidation.md) | Roles, permissions, contextual scopes and entitlement invalidation | 9 | [02-local-infrastructure](../implementation/phase-0-engineering-foundation/02-local-infrastructure.md), [03-identity-and-permission-foundation](../implementation/phase-0-engineering-foundation/03-identity-and-permission-foundation.md), [02-identity-roles-and-permissions](../implementation/phase-1-banking-foundation/02-identity-roles-and-permissions.md), [01-institution-and-tenancy](../implementation/phase-1-banking-foundation/01-institution-and-tenancy.md) |
| [ADR-0010](ADR-0010-observability-audit-evidence-redaction-and-retention.md) | Observability, audit evidence, sensitive-data redaction and retention | 12 | [05-observability-and-ci](../implementation/phase-0-engineering-foundation/05-observability-and-ci.md) |
| [ADR-0011](ADR-0011-secret-key-and-certificate-management.md) | Secret, key and certificate management and cryptographic boundaries | 11 | [02-local-infrastructure](../implementation/phase-0-engineering-foundation/02-local-infrastructure.md), [05-observability-and-ci](../implementation/phase-0-engineering-foundation/05-observability-and-ci.md) |
| [ADR-0012](ADR-0012-channel-security-boundaries.md) | Channel security boundaries: USSD, WhatsApp, chatbot and CMS | 10 | [09-channels-cms-and-communications](../implementation/phase-1-banking-foundation/09-channels-cms-and-communications.md) (by topic) |
| [ADR-0013](ADR-0013-deployment-migration-backup-restore-rto-and-rpo.md) | Deployment, migration execution, backup, restore, RTO and RPO | 13 | [12-operations-and-release-readiness](../implementation/phase-1-banking-foundation/12-operations-and-release-readiness.md) (by topic) |
| [ADR-0014](ADR-0014-reporting-read-models-and-regulatory-lineage.md) | Reporting read models, regulatory lineage and analytical isolation | 14 | [10-regulatory-and-scheduled-reporting](../implementation/phase-2-regulated-operations/10-regulatory-and-scheduled-reporting.md), [01-enterprise-data-governance-lineage-and-ownership](../implementation/phase-5-optimisation/01-enterprise-data-governance-lineage-and-ownership.md) (by topic) |

## Required by `BACKEND_ARCHITECTURE.md`

`BACKEND_ARCHITECTURE.md` requires at least fourteen ADRs but does not number them. This register supplies the identifiers that the epic documents already use.

| # | Required decision (`BACKEND_ARCHITECTURE.md`) | ID | Confidence in the mapping |
| --- | --- | --- | --- |
| 1 | C#/.NET-only backend and SDK support policy | ADR-0001 | Evidenced by `01-repository-and-build.md` |
| 2 | Nine initial microservices and progressive decomposition | ADR-0002 | Evidenced by `01-institution-and-tenancy.md` ownership boundaries |
| 3 | Monorepo and solution/project organisation | ADR-0003 | Evidenced by `01-repository-and-build.md`. Confirm whether package-management and analyzer policy belong here or in a separate ADR |
| 4 | Database-per-service and production tenant-isolation model | ADR-0004 | Evidenced by `02-local-infrastructure.md` and `01-institution-and-tenancy.md` |
| 5 | Ledger consistency, balance representation, concurrency and posting transaction boundary | ADR-0005 | Evidenced and summarised by `05-core-ledger-and-posting.md` |
| 6 | API versioning and problem-details conventions | ADR-0006 | Inferred from list position. No document references this identifier yet |
| 7 | Event envelope, broker, outbox/inbox, ordering and replay | ADR-0007 | Evidenced by `04-messaging-and-outbox.md` |
| 8 | Authentication provider, token model, service identity and authorisation enforcement | ADR-0008 | Evidenced and named by `02-identity-roles-and-permissions.md` |
| 9 | Role, permission, contextual scope, entitlement invalidation, maker-checker and segregation-of-duties model | ADR-0009 | Evidenced and named by `02-identity-roles-and-permissions.md` |
| 10 | USSD, WhatsApp chatbot, CMS, human handoff and channel-security boundaries | ADR-0012 | Number assigned by this register. `09-channels-cms-and-communications.md` requires the decision by topic |
| 11 | Secret/key management and cryptographic boundaries | ADR-0011 | Evidenced by the development-secrets module in `02-local-infrastructure.md` and by `05-observability-and-ci.md` |
| 12 | Observability, audit evidence, PII redaction and retention | ADR-0010 | Evidenced by `05-observability-and-ci.md`. Note this is architecture list item 12, not item 10 |
| 13 | Deployment platform, migration execution, backup, restore, RTO and RPO | ADR-0013 | Number assigned by this register. `12-operations-and-release-readiness.md` requires the decision by topic |
| 14 | Reporting/read-model architecture and regulatory data lineage | ADR-0014 | Number assigned by this register. `10-regulatory-and-scheduled-reporting.md` requires the decision by topic |

Mapping notes:

- Identifiers `ADR-0001` to `ADR-0005` and `ADR-0007` to `ADR-0009`, `ADR-0011` are evidenced by explicit references inside epic documents rather than by inference.
- `ADR-0006` is the only identifier no document uses. It is placed at architecture list item 6 because identifiers 1-5 and 7-9 align with list items 1-5 and 7-9. Confirm before relying on it.
- `ADR-0010` is evidenced as observability by `05-observability-and-ci.md`, which means the identifiers do not follow the architecture list order at item 10. The channel-boundary decision therefore takes the next free identifier, `ADR-0012`.
- `ADR-0012` to `ADR-0014` are newly assigned by this register. No epic references them by identifier yet. If the architecture owner prefers a different order, renumber them before any of these ADRs is `Accepted`.

## Decisions referenced by topic name only

The documents below require architectural decisions without using an identifier. An identifier is assigned when the ADR is created, continuing after `ADR-0014`. Use [`template.md`](template.md) and add the record to the index above.

| Document | Decision topics required |
| --- | --- |
| [08-integration-hub-and-providers](../implementation/phase-1-banking-foundation/08-integration-hub-and-providers.md) | Provider adapter and canonical contract strategy; secret manager, key management and certificate lifecycle (see ADR-0011); provider invocation durability and uncertainty model; webhook evidence retention, replay and dead-letter model |
| [09-channels-cms-and-communications](../implementation/phase-1-banking-foundation/09-channels-cms-and-communications.md) | Channel identity, linking and step-up model (see ADR-0012); USSD session and financial-command idempotency model; controlled chatbot intent and tool-execution model; CMS content, localisation and publication governance; notification finality, routing, consent and failover model; conversation, message and campaign retention model |
| [10-documents-and-statements](../implementation/phase-1-banking-foundation/10-documents-and-statements.md) | Document module ownership inside Risk and Reporting (see ADR-0014); object-storage integrity, encryption and immutability model; statement source snapshot and reproducibility model; secure download and time-limited link model; document retention, disposition and legal-hold model; rendering engine, template governance and supported formats |
| [11-audit-traceability-and-recovery](../implementation/phase-1-banking-foundation/11-audit-traceability-and-recovery.md) | Enterprise audit envelope and source-commit policy (see ADR-0010); append-only storage and tamper-evidence model; financial trace identity and typed-link model; audit retention, legal hold and evidence-export model; backup catalogue and recovery-orchestration ownership (see ADR-0013); cross-service PITR reconciliation and replay policy |
| [12-operations-and-release-readiness](../implementation/phase-1-banking-foundation/12-operations-and-release-readiness.md) | Deployment platform, environment topology and promotion model (see ADR-0013); service-level objectives, capacity model and availability tiers; software supply-chain controls and artifact provenance |
| [01-privileged-access-review](../implementation/phase-2-regulated-operations/01-privileged-access-review.md) | Privileged access governance (see ADR-0009) |
| [02-corporate-party-onboarding](../implementation/phase-2-regulated-operations/02-corporate-party-onboarding.md) | Corporate party and relationship model; corporate KYC and screening; mandate boundary |
| [03-term-deposits-and-maturity](../implementation/phase-2-regulated-operations/03-term-deposits-and-maturity.md) | Deposit calculation policy; maturity processing; tax and accounting mappings |
| [04-lending-products-and-origination](../implementation/phase-2-regulated-operations/04-lending-products-and-origination.md) | Lending service and data ownership; credit decision boundary; loan accounting and calculation |
| [05-loan-servicing-collateral-and-recovery](../implementation/phase-2-regulated-operations/05-loan-servicing-collateral-and-recovery.md) | Schedule and allocation policy; collateral and recovery policy |
| [06-risk-fraud-aml-and-sanctions](../implementation/phase-2-regulated-operations/06-risk-fraud-aml-and-sanctions.md) | Risk decision contract; screening and watchlist integration; monitoring rule governance |
| [07-reconciliation-and-settlement](../implementation/phase-2-regulated-operations/07-reconciliation-and-settlement.md) | Reconciliation evidence and matching; settlement calendars and positions; suspense correction |
| [08-branch-teller-and-cash-operations](../implementation/phase-2-regulated-operations/08-branch-teller-and-cash-operations.md) | Branch cash accounting; till/vault custody and dual control; receipt and denomination policy |
| [09-agency-banking-and-offline-capture](../implementation/phase-2-regulated-operations/09-agency-banking-and-offline-capture.md) | Agency module and data ownership; agent identity and device trust; offline cryptography and conflict resolution |
| [10-regulatory-and-scheduled-reporting](../implementation/phase-2-regulated-operations/10-regulatory-and-scheduled-reporting.md) | Regulatory country packs; report rendering and distribution |
| [11-degraded-operations-and-phase-readiness](../implementation/phase-2-regulated-operations/11-degraded-operations-and-phase-readiness.md) | Degraded capability matrix; deferred command and replay; customer status correction |
| [01-corporate-users-mandates-and-entitlements](../implementation/phase-3-growth-channels/01-corporate-users-mandates-and-entitlements.md) | Corporate identity and mandate ownership; delegated administration |
| [02-bulk-and-payroll-payment-processing](../implementation/phase-3-growth-channels/02-bulk-and-payroll-payment-processing.md) | Bulk file formats and partial processing; batch and item idempotency |
| [03-corporate-payment-approval-and-submission](../implementation/phase-3-growth-channels/03-corporate-payment-approval-and-submission.md) | Corporate signing evaluation; approval snapshot binding |
| [04-virtual-accounts-and-collection-matching](../implementation/phase-3-growth-channels/04-virtual-accounts-and-collection-matching.md) | Virtual account ownership and lifecycle; collection finality and matching |
| [05-corporate-apis-and-webhooks](../implementation/phase-3-growth-channels/05-corporate-apis-and-webhooks.md) | Corporate client authentication and signing; webhook delivery; certification |
| [06-card-lifecycle-and-controls](../implementation/phase-3-growth-channels/06-card-lifecycle-and-controls.md) | Card ownership and processor; PCI scope and tokenisation; authorisation and clearing |
| [07-merchant-acquiring-and-settlement](../implementation/phase-3-growth-channels/07-merchant-acquiring-and-settlement.md) | Merchant and acquiring ownership; processor and terminal model; pricing, reserves and settlement |
| [08-partner-apis-and-sandbox](../implementation/phase-3-growth-channels/08-partner-apis-and-sandbox.md) | Partner authentication and signing; API lifecycle and deprecation (see ADR-0006); sandbox isolation |
| [01-treasury-liquidity-and-fx-exposure](../implementation/phase-4-advanced-banking/01-treasury-liquidity-and-fx-exposure.md) | Treasury ownership; position and maturity methodology; market data and FX valuation; revaluation accounting |
| [02-trade-finance-instruments-and-correspondent-messaging](../implementation/phase-4-advanced-banking/02-trade-finance-instruments-and-correspondent-messaging.md) | Trade-finance ownership; instrument lifecycle and legal rules; SWIFT and correspondent messaging; trade accounting and settlement |
| [01-enterprise-data-governance-lineage-and-ownership](../implementation/phase-5-optimisation/01-enterprise-data-governance-lineage-and-ownership.md) | Metadata and lineage model; data access and purpose governance |
| [02-profitability-and-management-analytics](../implementation/phase-5-optimisation/02-profitability-and-management-analytics.md) | Profitability methodology; analytical calculation ownership; currency and allocation policy |
| [03-ai-assisted-service-and-model-governance](../implementation/phase-5-optimisation/03-ai-assisted-service-and-model-governance.md) | AI use-case boundary; model lifecycle and validation; AI provider and data residency; tool and knowledge access |
| [04-predictive-operational-analytics](../implementation/phase-5-optimisation/04-predictive-operational-analytics.md) | Predictive use cases and action boundaries; feature and outcome definitions; shadow and promotion policy |

## Process

1. Confirm that the decision is architectural rather than an ordinary implementation choice. Decisions affecting service boundaries, data ownership, consistency, security, regulatory evidence, deployment or financial processing require an ADR.
2. Copy [`template.md`](template.md) to `ADR-NNNN-kebab-case-title.md` using the next free number.
3. Record the context, the alternatives, the decision, the consequences, the owners and the affected epics or services.
4. Add the record to the index in this file, including the architecture item it satisfies.
5. Obtain approval from the owners named in the epic's open-decisions table. The architecture owner confirms numbering and scope.
6. Set the status to `Accepted`, fill `approved_by` and `approved_on` in the record, and update the epic documents that referenced the decision.
7. To change an accepted decision, create a new ADR and mark the old one `Superseded by ADR-NNNN`.

## Related documents

- [AGENTS.md](../../AGENTS.md) - authoritative context, precedence and definition of done
- [BACKEND_ARCHITECTURE.md](../architecture/BACKEND_ARCHITECTURE.md) - required ADRs and decisions awaiting stakeholder approval
- [Implementation README](../implementation/README.md) - phase, epic and agent workflow rules
