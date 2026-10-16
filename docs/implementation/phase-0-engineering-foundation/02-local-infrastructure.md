---
phase: "Phase 0 - Engineering Foundation"
epic: "Local Infrastructure"
owning_service: "Platform-wide"
modules: [PostgreSQL, Messaging, Redis, Observability, Development Secrets]
status: Draft
requirements: [ARC-004, ARC-008, NFR-RES-001, NFR-DR-001, SEC-007, SEC-018]
stories: [US-072, US-079, US-080]
dependencies: [Repository and Build Foundation]
adrs: [ADR-0004, ADR-0007, ADR-0009, ADR-0011]
last_updated: 2026-10-15
---

# Epic: Local Infrastructure

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

- [ ] One documented command starts the approved local dependencies.
- [ ] Health checks report readiness accurately.
- [ ] Nine database ownership boundaries are enforced.
- [ ] No production secret or real customer data is present.
- [ ] Reset, backup and restore instructions are safe and verified.
- [ ] Infrastructure versions and configuration are version-controlled.

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

