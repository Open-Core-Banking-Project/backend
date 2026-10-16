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

`Proposed` - not approved. `05-observability-and-ci.md` lists this ADR; the audit envelope, telemetry pipeline, redaction rules and retention periods must not be finalised until it is `Accepted`.

## Context

- `AGENTS.md` (`## Audit and observability`) states that audit records are business evidence rather than debug logs, requires recording of actor, role, institution, branch, action, timestamp, source, reason, correlation ID and before/after values where policy permits, forbids ordinary administrators from editing or deleting audit evidence, requires structured logging with stable event names, requires trace and correlation propagation across HTTP, messaging, workflows, ledger postings and provider calls, requires business-failure and financial-exception metrics alongside technical health, and requires health checks to distinguish liveness from readiness without leaking sensitive detail.
- `AGENTS.md` (`## Security and privacy`) prohibits logging passwords, PINs, OTPs, tokens, private keys, provider secrets, complete identity numbers or full account and card numbers, and requires masking of sensitive fields in logs, traces, reports, errors and test snapshots.
- `05-observability-and-ci.md` defines the observability standard, audit foundation, health policy, CI pipeline and walking-skeleton evidence.
- `11-audit-traceability-and-recovery.md` requires an enterprise audit envelope and source-commit policy, append-only storage with tamper evidence, a financial trace identity and typed-link model, and an audit retention, legal-hold and evidence-export model.
- `12-operations-and-release-readiness.md` requires "Observability, audit evidence, sensitive-data redaction and retention" as a decision, with `SEC-016`, `SEC-017` and `SEC-018` in scope.
- `BACKEND_ARCHITECTURE.md` (`## Observability`, `## Security boundaries`) and `README.md` (`## Observability`, `## Security`) capture current expectations.
- Requirement and story traceability: `AUD-001` to `AUD-004`, `NFR-OBS-001`, `NFR-OBS-002`, `NFR-MNT-001`, `SEC-016`, `SEC-017`, `US-070`, `US-071`, `US-079`, `US-080`.

## Decision

Not yet decided. This ADR must settle:

- OpenTelemetry pipeline: collector topology, exporter and backend choice, sampling policy, and the propagation format for trace and correlation context across HTTP, messaging and background work.
- The audit envelope: required fields, immutability mechanism, tamper-evidence approach, and the rule that audit records commit with the state change they describe.
- Redaction and masking: the classified field catalogue, redaction rules per log, trace, metric and error channel, and the enforcement mechanism that prevents prohibited values from being emitted.
- Metric naming, labels and the prohibition on sensitive or high-cardinality labels such as account or card identifiers.
- Health and readiness policy: what liveness and readiness may disclose and how dependencies are represented without leaking sensitive detail.
- Retention: retention periods for logs, traces, metrics and audit evidence; the legal-hold mechanism; and the evidence-export procedure.
- Alerting and runbook expectations, and which business failures must raise operational alerts.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| Application logs as the audit system | Logs are mutable, sampled and incomplete; audit records are business evidence |
| Redaction by developer convention only | Cannot be verified and fails the negative-test expectation |
| Debug-level capture of full requests and responses in non-production | Risks real customer data entering non-production environments and test snapshots |
| One retention period for all evidence | Conflicts with regulated retention and legal-hold obligations |

## Consequences

- Every service inherits the same telemetry pipeline, audit client and redaction rules, which fixes shared building blocks.
- Trace and causation identifiers become part of the financial trace graph, so their format and propagation are contract-level decisions.
- Retention and legal hold affect storage cost, partitioning and restore scope, and interact with [ADR-0013](ADR-0013-deployment-migration-backup-restore-rto-and-rpo.md).

## Compliance and evidence

- Automated test that prohibited values never appear in logs, traces or error responses for a sensitive flow.
- Walking-skeleton evidence of end-to-end correlation and causation as required by `05-observability-and-ci.md`.
- Audit immutability tests confirming ordinary administrative paths cannot edit or delete evidence.

## Open questions

- Telemetry backend and whether it is managed or self-hosted.
- Whether audit evidence is stored in the owning service database, a dedicated audit store or both.
- The exact retention periods per evidence class, which depends on unapproved regulatory country packs.

## Approval gate

Do not finalise the audit envelope, choose a telemetry backend, or commit retention configuration until this ADR is `Accepted`. Local development telemetry marked as development-only may continue under `02-local-infrastructure.md`.

## Related documents

- [ADR index](README.md)
- [ADR-0007](ADR-0007-event-envelope-broker-outbox-inbox-and-replay.md) - correlation and causation propagation through events
- [ADR-0009](ADR-0009-roles-permissions-contextual-scopes-and-invalidation.md) - authorisation evidence content
- [ADR-0011](ADR-0011-secret-key-and-certificate-management.md) - prohibition on emitting secrets
- [11-audit-traceability-and-recovery.md](../implementation/phase-1-banking-foundation/11-audit-traceability-and-recovery.md)
