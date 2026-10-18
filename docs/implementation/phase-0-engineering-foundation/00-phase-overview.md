---
phase: "Phase 0 - Engineering Foundation"
status: In progress
owning_services: [Platform-wide]
requirements: [ARC-001, ARC-002, ARC-003, ARC-004, ARC-005, ARC-008, ARC-010, NFR-SCAL-001, NFR-RES-002, NFR-DR-001, NFR-OBS-001, NFR-OBS-002, NFR-MNT-001, SEC-001, SEC-007, SEC-016]
stories: [US-068, US-070, US-071, US-072, US-079, US-080]
dependencies: []
last_updated: 2026-10-18
---

# Phase 0 - Engineering Foundation

## Objective

Create a repeatable, secure, observable, and testable delivery foundation for the nine C#/.NET microservices before implementing banking products or moving money.

Phase 0 proves that the architecture can build, run, communicate, persist owned data, enforce permissions, publish and consume an event safely, produce audit evidence, and be traced end to end.

## Non-goals

Phase 0 does not implement:

- Customer onboarding.
- Production authentication enrolment.
- Deposit products or customer accounts.
- Ledger balances or journal posting.
- Payment-provider execution.
- Production USSD, WhatsApp or notification providers.
- Institution-specific accounting, regulatory or pricing rules.

Synthetic foundation entities and test-only flows may be used to prove infrastructure. Do not disguise placeholders as completed banking capabilities.

## Epics

| Order | Epic | Outcome |
| --- | --- | --- |
| 1 | Repository and Build Foundation | Deterministic .NET solution, central standards, nine service boundaries, architecture tests |
| 2 | Local Infrastructure | Reproducible PostgreSQL, broker, Redis, observability and development-secret environment |
| 3 | Identity and Permission Foundation | Authentication/authorisation abstractions, permission policies, tenant context and negative tests |
| 4 | Messaging and Outbox | Versioned event envelope, transactional outbox/inbox, idempotent consumer and replay safety |
| 5 | Observability and CI | End-to-end telemetry, audit foundation, automated build/test/security pipeline and evidence |

## Dependency sequence

```text
Repository and Build
        ↓
Local Infrastructure
        ↓
Identity and Permission Foundation
        ↓
Messaging and Outbox
        ↓
Observability and CI
        ↓
Phase 0 walking-skeleton verification
```

Some work may overlap, but contracts and central build decisions from an earlier epic must stabilise before dependent projects adopt them.

## Required architectural decisions

Approve or explicitly time-box these before Phase 0 is verified:

1. Supported .NET SDK and upgrade policy.
2. Solution and project naming.
3. Nine-service monorepo topology.
4. Package management and analyzer policy.
5. PostgreSQL database-per-service development model.
6. Broker implementation for local and production environments.
7. Authentication provider and service-to-service identity direction.
8. Permission naming, contextual scopes and entitlement invalidation.
9. Event envelope, outbox/inbox and replay model.
10. OpenTelemetry pipeline and telemetry redaction.
11. CI provider and required branch checks.
12. Development secret storage and production secret-management boundary.

Unresolved production vendor choices may use replaceable local implementations when the interface and decision boundary are explicit.

## Walking skeleton

Phase 0 culminates in a non-financial, synthetic flow:

```text
Authenticated test administrator
  -> API validates institution context and permission
  -> foundation command persists an owned test record
  -> same transaction writes an outbox message
  -> background publisher sends a versioned event
  -> a second service consumes the event idempotently
  -> immutable audit evidence is recorded
  -> correlation and causation are visible across logs and traces
```

The implementation must prove:

- Cross-tenant access is denied.
- Missing permission is denied.
- A repeated idempotency key does not duplicate state or events.
- A repeated broker message does not duplicate the consumer outcome.
- Broker or consumer interruption can recover without data loss.
- Telemetry contains no secrets or prohibited sensitive data.

## Shared contracts produced

Phase 0 establishes versioned contracts for:

- Correlation and causation context.
- Tenant/institution context.
- Authenticated actor and service identity.
- Permission requirement and authorisation result.
- Problem-details error response.
- Idempotent command metadata.
- Integration-event envelope.
- Outbox and inbox processing state.
- Audit-event envelope.
- Health and readiness response policy.

Shared contracts must remain domain-neutral. Banking entities do not belong in foundation libraries.

## Security gates

- Deny-by-default authorisation works in every reference API.
- Tenant identity cannot be overridden by an untrusted body or query parameter.
- Service identities and human identities are distinguishable.
- Secrets do not appear in source control, logs, traces, test snapshots or exception responses.
- Dependencies and container images are scanned through the selected CI policy.
- Local defaults cannot accidentally connect to production endpoints.
- Audit evidence is append-only through ordinary application paths.

## Quality gates

- Central build configuration is applied to every project.
- Compiler warnings and configured analyzers fail CI.
- Architecture tests prevent invalid dependency direction and cross-service implementation references.
- Integration tests run against disposable real dependencies.
- Tests are deterministic and do not depend on public networks.
- A clean checkout can restore, build and test using documented commands.
- Database migrations can be applied to empty databases and verified.
- The walking skeleton passes twice without duplicate effects.

## Operational gates

- Every service has liveness and readiness behaviour.
- Graceful shutdown does not lose an authoritative local commit or duplicate message processing.
- Logs, metrics and traces carry service, environment, institution where safe, correlation and causation context.
- Dashboards or documented queries expose HTTP failures, consumer lag, outbox backlog, inbox duplicates and failed migrations.
- Backup and restore procedures are documented for the development reference environment, while production RTO/RPO remain explicit open decisions.

## Current implementation evidence (2026-10-18)

- The Release solution build succeeds with zero warnings and zero errors.
- The offline-safe test filter passes all 63 tests: 14 architecture, 46 Identity Access, and 3
  Audit Reconciliation tests. Database- and integration-category tests are excluded.
- `dotnet format CoreBanking.sln --verify-no-changes --no-restore` passes.
- EF reports no pending model changes for Identity Access or Audit Reconciliation.
- Local infrastructure verification reports 37/37 checks passing, including PostgreSQL isolation,
  Kafka round-trip and persistence across a broker restart, Redis, telemetry collector, backup and
  restore.
- `scripts/db-verify.ps1 -Configuration Release` applies migrations to both service databases and
  passes 25 Identity Access database/integration tests plus all 5 Audit Reconciliation tests.
  These prove replay deduplication, durable malformed-message quarantine, audit insertion and
  rejection of application-principal audit updates and deletes.
- The Audit Reconciliation worker now consumes the foundation topic using its own database; the
  Identity Access authorisation writer persists evidence using an insert-only database grant.
- Shared OpenTelemetry tracing and metrics export are registered in all nine APIs and both
  messaging workers; W3C
  context is persisted with outbox metadata and propagated in Kafka headers. Collector-level
  smoke verification confirmed an Identity Access API span reached the local collector; inspection
  of a complete walking-skeleton trace remains outstanding.
- Every API exposes liveness and readiness routes, enforced by an architecture test. CI now runs
  build, format, offline tests, dependency audit, disposable infrastructure verification,
  migrations and database/broker integration tests.
- End-to-end walking-skeleton trace inspection, an approved secret-scanning policy and protected
  branch configuration remain open. Phase 0 acceptance therefore remains open.

## Phase acceptance

Phase 0 is complete when:

- [x] The repository foundation builds deterministically.
- [x] All nine service boundaries exist and are enforced by architecture tests.
- [x] Local infrastructure starts from documented commands and passes health checks.
- [x] Authentication, tenant context, roles and permissions work in a reference API.
- [x] Outbox publication and idempotent consumption survive restart and replay tests.
- [ ] Audit and distributed tracing cover the walking skeleton end to end.
- [ ] CI performs restore, build, tests, formatting, analysis and approved security checks.
- [x] No banking feature is falsely represented as production-ready.
- [ ] All Phase 0 ADRs, runbooks and verification evidence are reviewed.

## Agent instructions

- Implement Phase 0 epic by epic; do not scaffold banking behaviour ahead of its approved epic.
- Keep reference flows explicitly named as foundation/test flows.
- Do not choose production vendors, identity licensing, RTO/RPO or regulatory policies without approval.
- After each epic, update this overview with status, evidence and accepted deviations.
