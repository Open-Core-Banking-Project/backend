---
phase: "Phase 0 - Engineering Foundation"
epic: "Observability, Audit and Continuous Integration"
owning_service: "Platform-wide"
modules: [OpenTelemetry, Audit Foundation, Health, CI, Security Checks]
status: Implemented
requirements: [NFR-OBS-001, NFR-OBS-002, NFR-MNT-001, SEC-016, SEC-017, AUD-001, AUD-002, AUD-003, AUD-004]
stories: [US-070, US-071, US-079, US-080]
dependencies: [Repository and Build Foundation, Local Infrastructure, Identity and Permission Foundation, Messaging and Outbox]
adrs: [ADR-0010, ADR-0011]
last_updated: 2026-10-20
---

# Epic: Observability, Audit and Continuous Integration

Approved 2026-10-16 on the instruction of the repository owner, after ADR-0010 and ADR-0011 were accepted.

Implementation evidence, 2026-10-18:

- All nine APIs expose separate liveness and readiness routes, enforced by an architecture test.
- Relational Identity Access persists permission allow/deny evidence in
  `audit.authorisation_events`; the application principal is granted select/insert only.
- Audit Reconciliation consumes the foundation event in its independent database and atomically
  records inbox identity with append-only audit evidence.
- `scripts/db-verify.ps1 -Configuration Release` applies both databases' migrations and passes 25
  Identity Access database/integration tests and 5 Audit Reconciliation tests. Application-role
  inserts succeed while updates and deletes of authorisation and operator-replay evidence are
  rejected.
- Local infrastructure verification reports 37/37 checks passing, including the telemetry
  collector and a committed Kafka message surviving an actual broker restart.
- The shared observability building block configures OTLP tracing and metrics export for all nine
  APIs and both messaging workers, including ASP.NET Core, HttpClient, Npgsql, messaging spans and
  bounded messaging instruments.
- W3C trace context is persisted in outbox metadata and propagated through Kafka `traceparent` and
  `tracestate` headers. Telemetry processing removes URL path/query and database statement tags.
- A Development smoke request to Identity Access `/health/live` returned 200 and the local
  collector emitted its `corebanking.identity-access.api` span. A focused unit test verifies
  removal of URL and SQL tags.
- CI runs restore, build, formatter verification, architecture/unit tests, dependency audit,
  disposable infrastructure verification, migrations, database/broker integration tests and the
  complete walking-skeleton verifier.
- `scripts/walking-skeleton-verify.ps1` records one trace through the API, Npgsql, outbox publisher,
  Kafka consumer and Audit Reconciliation database. It verifies the correlation ID, permission
  evidence, consumer audit evidence and absence of a sensitive canary and raw URL/SQL tags in
  exported telemetry, host logs and the response. A separate exporter-level unit test proves the
  redaction processor runs before export.
- GitHub CodeQL analyses C# with `security-extended`; Gitleaks scans complete Git history using the
  official container pinned by digest. Pull request #2 passed both gates before merge.
- `main` requires pull requests, current branches and the build/test, infrastructure integration,
  secret scan and CodeQL checks. Administrator bypass, force pushes and deletion are disabled.
- No container artifact is currently produced, so container scanning remains deferred until an
  artifact exists.

## Objective

Make every foundation request traceable, every sensitive action auditable, every service operationally diagnosable, and every contribution automatically verified before merge.

## Observability standard

Each service emits:

- Structured logs with stable event names.
- Distributed traces for HTTP, database, messaging and background work.
- Technical metrics for latency, failures, saturation, retries and queue depth.
- Business/operational metrics for denied permissions, outbox backlog, inbox duplicates and audit failures.
- Separate liveness and readiness checks.

Telemetry carries service, version, environment, trace, correlation and causation context. Institution identifiers may be included only according to the approved data-classification policy.

Never record secrets, credentials, tokens, private keys, PINs, OTPs, full identity/account/card numbers, prohibited PII or unredacted provider payloads.

## Audit foundation

Audit evidence is distinct from diagnostic logging. The initial audit contract includes:

```text
audit_event_id
occurred_at
institution_id
actor_id
actor_type
role_or_service_context
permission
resource_type
resource_id
action
decision
reason
source
correlation_id
before_and_after_reference
policy_or_configuration_version
```

Audit records are append-only through normal application paths. Ordinary administrators cannot edit or delete them.

## Health policy

- Liveness answers whether the process should be restarted.
- Readiness answers whether the service can safely accept assigned work.
- A transient optional dependency does not automatically make all endpoints unavailable.
- A required database or critical contract dependency affects readiness according to service policy.
- Health output reveals no credentials, connection strings or internal topology beyond approved detail.

## CI pipeline

Required checks:

1. Restore with locked/centrally managed dependencies.
2. Build with warnings and configured analyzers treated as failures.
3. Formatting verification.
4. Unit tests.
5. Architecture tests.
6. Integration and contract tests using disposable dependencies.
7. Dependency and secret scanning.
8. Container/build-artifact scanning when artifacts are produced.
9. Test and coverage evidence publication without arbitrary coverage-only approval.

Performance, security and end-to-end suites may run in separate pipelines but must become required before their affected release gate.

## Walking-skeleton evidence

The reference flow must show:

- One trace spanning API, database, outbox publisher and consumer.
- Correlation and causation identifiers preserved.
- Permission allow/deny decision recorded appropriately.
- Audit evidence for the sensitive action.
- Metrics for success, failure, duplicate and backlog states.
- No prohibited values in collected telemetry.

## Implementation slices

1. Approve telemetry, audit and CI ADRs.
2. Add shared OpenTelemetry configuration without domain coupling.
3. Add consistent correlation/causation propagation.
4. Add liveness/readiness conventions.
5. Define and persist the foundation audit envelope.
6. Instrument the Phase 0 walking skeleton.
7. Create CI workflows and required checks.
8. Add telemetry-redaction and pipeline-failure tests.
9. Document dashboards, alerts and first-response runbooks.

## Acceptance criteria

- [x] The walking skeleton is traceable end to end.
- [x] Sensitive actions generate immutable audit evidence.
- [x] Logs and traces contain no prohibited sensitive data.
- [x] Health endpoints distinguish liveness and readiness.
- [x] CI verifies build, formatting, analysis and required tests.
- [x] Security and secret checks run automatically.
- [x] Failed outbox, inbox and permission states are observable.
- [x] A clean pull request cannot merge when a required check fails.

## Open decisions

- Telemetry storage and dashboard products.
- Audit retention and tamper-evidence mechanism.
- Coverage policy and performance baselines.

## Agent instructions

- Do not log payloads merely to improve diagnostics.
- Do not make readiness permanently fail for optional local tooling.
- Do not weaken analyzers or CI checks to obtain a green pipeline.
- Include exact pipeline or local verification results in completion reports.
