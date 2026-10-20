---
phase: "Phase 0 - Engineering Foundation"
epic: "Local Infrastructure"
owning_service: "Platform-wide"
modules: [PostgreSQL, Messaging, Redis, Observability, Development Secrets]
status: Implemented
requirements: [ARC-004, ARC-008, NFR-RES-001, NFR-DR-001, SEC-007, SEC-018]
stories: [US-072, US-079, US-080]
dependencies: [Repository and Build Foundation]
adrs: [ADR-0004, ADR-0007, ADR-0009, ADR-0011]
last_updated: 2026-10-20
---

# Epic: Local Infrastructure

Approved 2026-10-16 on the instruction of the repository owner, after ADR-0004, ADR-0007, ADR-0009 and ADR-0011 were accepted.

Implementation evidence, 2026-10-16:

- `deploy/compose/compose.yml` runs PostgreSQL 16.15, Apache Kafka 4.2.2, Redis 7.4.11 and the OpenTelemetry collector 0.163.1, with every published port bound to 127.0.0.1.
- `deploy/compose/postgres/init/01-create-databases.sh` creates the nine databases, an application and a migration principal per service, revokes `CONNECT` from `PUBLIC`, and sets default privileges. It aborts when any password is missing: there are no fallback credentials.
- `scripts/infra.ps1` provides `up`, `status`, `down`, `reset`, `backup` and `restore`. `-Action up` generates `deploy/compose/.env` with random development-only credentials; the file is git-ignored and its values are never printed.
- `scripts/infra-verify.ps1` runs 37 checks and all pass: nine principals reach their own database, nine cross-service connections are refused with `permission denied`, the migration principal may change schema while the application principal may not, the broker round-trips three messages, Redis answers with persistence deliberately disabled, the collector accepts and emits an OTLP trace, and a backup restores into a scratch database with matching rows.
- A full reset and recreation from the documented commands was exercised, and `scripts/verify.ps1` still passes.
- Host ports are overridable in `.env`. On the current machine `CB_POSTGRES_PORT=15432` and `CB_CACHE_PORT=16379` are required because native PostgreSQL and Redis services already own the defaults.

Status is `Implemented` and verified in the protected CI integration job. Object storage and search backends are intentionally not provisioned yet; add them when a later phase requires them.

## Objective

Provide a reproducible local environment for service-owned PostgreSQL databases, messaging, Redis, telemetry and safe development secrets without depending on production systems or real customer data.

## Included scope

- Container-based local infrastructure orchestration.
- Separate development databases and credentials for all nine services.
- Kafka-compatible local broker selected by ADR.
- Redis for approved non-authoritative use.
- OpenTelemetry collector and selected local telemetry backends.
- Object-storage emulator when required by the walking skeleton.
- Safe development secret/configuration mechanism.
- Health checks, startup dependencies and teardown/reset instructions.

## Database requirements

Create these logical databases:

```text
core_ledger_db
customer_onboarding_db
identity_access_db
payments_vtu_db
channels_engagement_db
operations_workflow_db
risk_reporting_db
integration_hub_db
audit_reconciliation_db
```

Each service uses a separate database principal. A service principal must not have access to another service's database. Development administration credentials remain separate from application credentials.

## Safety requirements

- Local configuration uses synthetic data and non-production endpoints.
- No production credentials or copied production configuration are permitted.
- Destructive reset commands target only explicitly named local volumes and databases.
- Default network exposure is limited to the developer machine.
- Health checks must not print secrets.
- Redis is never configured as durable financial truth.

## Implementation slices

1. Approve local broker and orchestration decisions.
2. Add PostgreSQL with separate databases and principals.
3. Add broker and topic/bootstrap configuration required by Phase 0.
4. Add Redis with explicit development-only use.
5. Add OpenTelemetry collector and local log/metric/trace destinations.
6. Add safe development configuration and secret instructions.
7. Add startup, health verification, teardown, backup and restore procedures.

## Verification

- Each service principal can connect to its own database.
- Cross-service database access is denied.
- Broker publish/consume smoke test passes.
- Redis health test passes and persistence expectations are documented.
- Telemetry receives a synthetic trace, metric and structured log.
- Environment can be destroyed and recreated from documented commands.
- A development database backup can be restored and verified.

## Acceptance criteria

- [x] One documented command starts the approved local dependencies.
- [x] Health checks report readiness accurately.
- [x] Nine database ownership boundaries are enforced.
- [x] No production secret or real customer data is present.
- [x] Reset, backup and restore instructions are safe and verified.
- [x] Infrastructure versions and configuration are version-controlled.

## Open decisions

- Local and production Kafka-compatible broker.
- Container orchestration tooling and optional .NET orchestration layer.
- Local object-storage implementation.
- Local metrics, logs and traces backends.
- Production secret manager; local substitute must remain replaceable.

## Agent instructions

- Do not download or embed production credentials.
- Do not expose databases or telemetry publicly by default.
- Verify explicit target names before any reset or volume deletion.
- Keep vendor-specific code behind documented adapters and configuration.

