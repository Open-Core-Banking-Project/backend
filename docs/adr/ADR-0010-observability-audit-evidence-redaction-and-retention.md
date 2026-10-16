---
id: ADR-0010
title: "Observability, audit evidence, sensitive-data redaction and retention"
status: Proposed
date: 2026-10-16
owners: [Platform Engineering, Security, Compliance]
required_by: "Phase 0 - Observability and CI; Phase 1 - Audit, Traceability and Recovery"
architecture_item: 12
referenced_by:
  - docs/implementation/phase-0-engineering-foundation/05-observability-and-ci.md
supersedes: []
approved_by: []
approved_on: null
---

# ADR-0010: Observability, audit evidence, sensitive-data redaction and retention

## Status

`Proposed` - decision drafted and ready for owner approval. Effective only when the owners listed below record `Accepted`. Until then the audit envelope, telemetry pipeline and retention configuration must not be finalised; local development telemetry marked development-only may proceed under `02-local-infrastructure.md`.

## Context

- `AGENTS.md` (`## Audit and observability`) states that audit records are business evidence rather than debug logs, requires actor, role, institution, branch, action, timestamp, source, reason, correlation and before/after evidence, prohibits ordinary administrators from editing or deleting audit evidence, requires structured logging with stable event names, requires trace and correlation propagation across HTTP, messaging, workflows, ledger postings and provider calls, requires business-failure metrics alongside technical health, and requires health checks that distinguish liveness from readiness without leaking sensitive detail.
- `AGENTS.md` (`## Security and privacy`) prohibits logging credentials, tokens, keys, PINs, one-time codes and complete identity, account or card numbers, and requires masking of sensitive fields in logs, traces, reports, errors and test snapshots.
- `05-observability-and-ci.md` defines the observability standard, the audit contract field list, the health policy, the required CI checks and the walking-skeleton evidence, and records the CI provider, telemetry storage, retention and scanning tools as open decisions.
- `11-audit-traceability-and-recovery.md` requires an enterprise audit envelope with a source-commit policy, append-only tamper-evident storage, a typed financial trace graph, and retention, legal hold and evidence export.
- Requirement and story traceability: `AUD-001` to `AUD-004`, `NFR-OBS-001`, `NFR-OBS-002`, `NFR-MNT-001`, `SEC-016`, `SEC-017`, `US-070`, `US-071`, `US-079`, `US-080`.

## Decision

1. **Telemetry pipeline.** All services use the OpenTelemetry SDK with OTLP export to a collector, which is the single egress point. Resource attributes carry service name, service version and deployment environment. Export uses batch processing with bounded queues and a non-blocking failure mode: telemetry loss must not fail a business operation, except where this ADR requires audit evidence (see item 4).
2. **Propagation.** W3C Trace Context is propagated across HTTP and message headers, and across background work. The business correlation identifier is distinct from the trace identifier and travels in the event envelope per [ADR-0007](ADR-0007-event-envelope-broker-outbox-inbox-and-replay.md); causation identifiers link a consumer to its producing event.
3. **Logging.** Structured logs with stable, dot-separated event names, emitted to standard output and captured by the platform rather than written to application-managed files. Message templates are constant; values are properties. Interpolated payloads, request and response bodies and SQL parameters are not logged.
4. **Audit evidence.** Audit is a separate, durable record type using the envelope defined in `05-observability-and-ci.md`, including actor, institution, permission, resource, action, decision, reason, source, correlation, before/after reference and policy or configuration version. Audit records commit with the governed state change where the same service owns the data, are append-only, and cannot be updated or deleted through ordinary application or administrative paths. In a financial path, failure to write required audit evidence fails the operation.
5. **Redaction.** A classified field catalogue defines prohibited values: credentials, tokens, keys, PINs, one-time codes, complete identity, account and card numbers, and unredacted provider payloads. Redaction is enforced centrally by the logging, metric and trace facade rather than per call site, and is verified by automated tests that capture telemetry for a sensitive flow and assert the absence of prohibited values.
6. **Metrics.** Technical metrics follow OpenTelemetry semantic conventions; business metrics use a `corebanking` namespace. Label values are bounded: actor, account, card, device and trace identifiers are never used as labels. Required business metrics include denied permissions, outbox backlog and oldest age, inbox duplicates, dead-letter growth and audit-write failures.
7. **Health.** Separate liveness and readiness endpoints. Liveness indicates whether the process should be restarted; readiness indicates whether the service can accept assigned work and reflects only required dependencies. Health output carries no credentials, connection strings or internal topology beyond approved detail. Optional local tooling does not make readiness fail.
8. **Retention and legal hold.** Retention is configured per evidence class (application logs, traces, metrics, audit evidence, security events) and is never hardcoded in services. Development defaults are short; production periods are set only when regulatory country packs are approved. A legal hold suspends deletion for the affected records and is itself audited.
9. **Backends.** Telemetry storage products are an owner decision. The local default remains a collector plus console or file output, with optional local trace and metric backends in the development compose file, so Phase 0 is not blocked.
10. **CI checks.** The pipeline restores with centrally managed dependencies, builds with warnings and analyzers as errors, verifies formatting, runs unit and architecture tests and the dependency audit, and publishes test evidence. The CI provider, additional scanning tools and their exception process are confirmed under this ADR; the currently committed workflow implements only the checks Phase 0 epic 01 requires.
11. **Runbooks.** Each service provides dashboards and first-response runbooks for its alerts before production release, including outbox backlog, dead letters, failed reconciliation and audit-write failure.
12. **Prohibitions.** No payload capture by default, no personal data in span attributes or metric labels, no secrets in exception messages, no telemetry that reveals tenant data across institutions, and no weakening of telemetry, redaction or audit to obtain a green pipeline or pass a test.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| Application logs as the audit system | Logs are mutable, sampled and incomplete; audit is business evidence with retention and legal-hold duties |
| Redaction by developer convention at each call site | Cannot be verified and fails the negative-test expectation |
| Debug-level capture of full requests and responses outside production | Risks real customer data entering non-production environments and test snapshots |
| One retention period for all evidence | Conflicts with differentiated regulatory retention and legal hold |
| Metrics labelled by account or actor | Unbounded cardinality and a privacy exposure |
| Coupling business operations to telemetry availability | Availability risk; only audit evidence in governed paths is allowed to fail the operation |

## Consequences

- **Enables:** one telemetry path with consistent redaction, correlation across services and the financial trace graph, and audit evidence that is testable and immutable.
- **Constrains:** services share a telemetry and audit client, cannot choose their own logging shape, must use bounded metric labels, and must classify every new field that may reach telemetry.
- **Cost:** a collector deployment, storage backends, retention management and per-service dashboards and runbooks.
- **Out of scope:** the telemetry backend product, production retention periods and the tamper-evidence mechanism for audit storage.

## Compliance and evidence

- Automated redaction test over a sensitive flow asserting no prohibited values in logs, traces, metrics or error responses.
- Walking-skeleton evidence: one trace spanning API, database, outbox publisher and consumer with preserved correlation and causation, a recorded permission decision, audit evidence, and no prohibited values.
- Audit immutability test proving ordinary application and administrative paths cannot modify or delete evidence.
- CI evidence: build, format, tests and dependency audit recorded per pull request.

## Open questions

- Telemetry backend products for logs, traces and metrics, and whether they are managed services.
- Whether audit evidence is stored in the owning service database, a dedicated audit store, or both, and the tamper-evidence mechanism (hash chaining, write-once storage or both).
- Production retention periods per evidence class, pending regulatory country packs.
- Sampling policy in production and whether any regulated flow requires unsampled traces.
- Secret-scanning tooling and the exception process, shared with [ADR-0011](ADR-0011-secret-key-and-certificate-management.md).

## Approval gate

Do not finalise the audit envelope, select or commit a telemetry backend, or commit retention configuration until this ADR is `Accepted`. Local development telemetry marked development-only may continue under `02-local-infrastructure.md`. The CI workflow may continue to run the checks it already implements; new scanning steps and branch-protection requirements wait for this ADR.

Approval required from **Platform Engineering**, **Security** and **Compliance**. Record acceptance by setting `status: Accepted` and filling `approved_by` and `approved_on`, then updating the [index](README.md).

## Related documents

- [ADR index](README.md)
- [ADR-0007](ADR-0007-event-envelope-broker-outbox-inbox-and-replay.md) - correlation and causation propagation through events
- [ADR-0009](ADR-0009-roles-permissions-contextual-scopes-and-invalidation.md) - authorisation evidence content
- [ADR-0011](ADR-0011-secret-key-and-certificate-management.md) - prohibition on emitting secrets
- [05-observability-and-ci.md](../implementation/phase-0-engineering-foundation/05-observability-and-ci.md)
- [11-audit-traceability-and-recovery.md](../implementation/phase-1-banking-foundation/11-audit-traceability-and-recovery.md)