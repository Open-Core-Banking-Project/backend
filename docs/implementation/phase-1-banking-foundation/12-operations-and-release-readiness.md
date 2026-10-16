---
phase: "Phase 1 - Banking Foundation"
epic: "Operations and Release Readiness"
owning_service: "Platform-wide, coordinated by Operations and Workflow"
modules:
  - Staff Operations APIs
  - Release Governance
  - CI/CD and Artifact Promotion
  - Database Migration Governance
  - Service Health and Observability
  - Capacity and Resilience
  - Security Operations
  - Incident and Runbook Management
status: Draft
requirements:
  - NFR-PERF-001
  - NFR-PERF-002
  - NFR-SCAL-001
  - NFR-AVAIL-001
  - NFR-RES-001
  - NFR-RES-002
  - NFR-DR-001
  - NFR-DR-002
  - NFR-OBS-001
  - NFR-OBS-002
  - NFR-USA-001
  - NFR-ACC-001
  - NFR-I18N-001
  - NFR-MNT-001
  - SEC-016
  - SEC-017
  - SEC-018
stories: [US-074, US-079, US-080]
dependencies:
  - Phase 0 engineering foundation, CI, local infrastructure and security foundations
  - 01 Institution and Tenancy
  - 02 Identity, Roles and Permissions
  - 03 Customer and Onboarding
  - 04 Products and Accounts
  - 05 Core Ledger and Financial Posting
  - 06 Workflow and Maker-Checker
  - 07 Payments, Collections and VTU
  - 08 Integration Hub and Providers
  - 09 Channels, CMS and Communications
  - 10 Documents and Statements
  - 11 Audit, Traceability and Recovery
adrs:
  - Deployment platform, environment topology and promotion model
  - Observability, audit evidence, sensitive-data redaction and retention
  - Migration execution, backup, restore, RTO and RPO
  - Service-level objectives, capacity model and availability tiers
  - Software supply-chain controls and artifact provenance
last_updated: 2026-10-15
---

# Epic 12: Operations and Release Readiness

## 1. Objective

Make the Phase 1 backend safe to operate and release as nine independently deployable C#/.NET microservices. A release engineer must be able to promote tested immutable artifacts through controlled environments with approvals, migration plans, verification and recoverable failure handling. A site reliability engineer must be able to identify service and business degradation through tenant-safe logs, metrics, traces and alerts. Authorised bank staff must receive role-, institution- and branch-aware backend operations APIs without direct database access.

This epic converts production readiness from an informal checklist into versioned contracts, automated gates and retained evidence. It does not declare the platform production-ready until the open hosting, capacity, availability, recovery and cutover decisions have been approved and verified.

## 2. Success measures

- Every deployable service has a repeatable build, test, scan, package, migration, deployment, verification and recovery path.
- The same immutable artifact is promoted between environments; production is not rebuilt from a branch or workstation.
- Promotion cannot proceed when required tests, security checks, approvals, migration evidence or recovery plans are missing.
- Infrastructure and environment configuration are version controlled, while secrets remain in approved secret management.
- Database migrations run once through a controlled service-owned step and never opportunistically from application replicas.
- Staff operations endpoints enforce permission, institution, branch, resource, approval and step-up context in backend code.
- Critical staff actions return their approval state and immutable audit reference.
- Each service exposes distinct liveness, readiness and startup behaviour without revealing secrets or dependency details.
- Requests and financial workflows retain correlation and causation across HTTP, messaging, jobs, workflows, ledger posting and provider calls.
- Alerts cover service failure, latency, saturation, queue and outbox backlog, failed jobs, financial exceptions, provider degradation, audit failure and recovery failure.
- Operational dashboards are tenant-aware where business data is presented and never expose secrets or prohibited personal data.
- Incident records link affected services, tenants, transactions, traces, deployments, recovery actions and audit evidence.
- Rollback or forward-fix behaviour is documented and exercised for code, configuration, contracts and migrations.
- Capacity, availability, RTO and RPO evidence uses stakeholder-approved targets and workloads rather than invented values.
- Production data is absent from non-production unless formally approved and irreversibly masked.

## 3. Scope and ownership boundaries

### Included

- Backend contracts for a role-aware staff operations portal or BFF.
- A release manifest covering service versions, image digests, contracts, migrations, configuration versions, approvals and evidence.
- Continuous integration and environment-promotion gates.
- Immutable container artifacts, dependency inventory, security scanning and retained provenance.
- Controlled Entity Framework Core migration packaging and execution.
- Backward-compatible rollout, deployment verification, rollback and forward-fix planning.
- Standard logging, metrics, tracing, health checks, dashboards and alerts across all services.
- Service and business SLIs whose thresholds are approved outside code and versioned.
- Capacity, soak, resilience, failover and restore testing under approved workloads.
- Incident classification, response evidence, runbook execution and post-incident follow-up.
- Secret, certificate, dependency, vulnerability and patch-expiry monitoring.
- Production readiness assessment and Phase 1 exit evidence.

### Platform-wide responsibilities

Every microservice owns:

- its build definition, image, deployment descriptor and service inventory entry;
- its API and event compatibility checks;
- its database migrations and least-privilege migration principal;
- its liveness, readiness, startup, metrics, trace and structured-log instrumentation;
- its dashboards, actionable alerts and service-specific runbooks;
- its capacity profile and graceful shutdown behaviour;
- its rollback or forward-fix notes;
- evidence that replay and restart do not create duplicate business or financial outcomes.

### Operations and Workflow responsibilities

- Own release, deployment, migration and incident approval cases when application-level approval tracking is required.
- Apply maker-checker, segregation-of-duties, reason and evidence-link rules to operational actions.
- Coordinate operational cases without becoming the deployment executor or storing other services' private operational data.

### Audit and Reconciliation responsibilities

- Retain immutable evidence of privileged operational actions, approvals, deployments, rollbacks, migration outcomes and incident recovery decisions.
- Link release and incident evidence to transaction traces where financial outcomes are affected.
- Detect and alert on audit-ingestion failure without using logs as a substitute for audit evidence.

### Delivery-platform responsibilities

- Build, test, scan, attest, store and promote immutable artifacts.
- Enforce protected-environment approvals and deployment identities.
- Retain pipeline execution evidence and artifact provenance for the approved retention period.
- Execute approved infrastructure, configuration and database deployment steps.

The delivery platform is infrastructure, not a new banking microservice. Its product and hosting choice require an approved ADR.

### Excluded

- Building the staff web user interface. This epic defines and verifies backend contracts for US-074; frontend delivery is a separate workstream.
- Selecting a cloud, container orchestrator, CI/CD vendor, SIEM, APM product or secret manager without approval.
- Inventing production load, service-level, retention, availability, RTO or RPO targets.
- Replacing the backup and restore evidence model from Epic 11.
- Direct database repair through the staff portal.
- A shared production database, shared migration assembly or cross-service administrative principal.
- Automatic financial correction based only on an alert.
- Phase 2 reporting, multi-country operations or active-active topology unless separately approved.

### Hard boundaries

- The backend remains entirely C#/.NET.
- The nine-service microservices topology remains the initial deployment model.
- No frontend, operator or pipeline queries another service's private tables.
- Release tooling never bypasses application permissions, financial invariants, tenant isolation, maker-checker or audit requirements.
- Feature flags may change availability but cannot disable authentication, authorisation, tenant checks, balanced posting, audit, reconciliation or idempotency.
- Observability data is not authoritative banking data and cannot prove a financial outcome on its own.
- A green deployment does not prove business readiness until synthetic and business-level verification passes.

## 4. Actors, roles and permissions

| Actor | Permission | Contextual scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Bank staff user | `operations.dashboard.view` | Permitted institution, branch and function | Policy driven | None |
| Operations analyst | `operations.case.view` | Permitted institution, branch and case type | For sensitive evidence | None |
| Incident responder | `operations.incident.manage` | Assigned incident and environments | Production actions | Required for controlled recovery |
| Runbook operator | `operations.runbook.execute` | Approved runbook, service and environment | Production | Maker-checker for privileged steps |
| Release engineer | `release.plan.create` | Approved services and environments | Production | Cannot approve own production release |
| Release approver | `release.approve` | Release scope and protected environment | Production | Maker-checker |
| Deployment identity | `release.deploy.environment` | Exact environment and signed manifest | Workload identity | Prior human/system approval |
| Migration operator | `migration.execute` | One service database and approved migration set | Production | Approved migration plan |
| Migration approver | `migration.plan.approve` | One service and environment | Production | Cannot execute same plan if policy forbids |
| Reliability engineer | `operations.telemetry.view` | Permitted environment and tenant scope | Sensitive traces | None |
| Security approver | `security.release.approve` | Security gate and approved exception | Production | Independent approval where required |
| Feature-flag operator | `operations.feature_flag.manage` | Approved flag and environment | Security-sensitive flag | Maker-checker where risk requires |
| Auditor | `operations.evidence.view` | Approved institution, period and evidence class | Sensitive export | Export may require approval |

Rules:

- Identity and Access owns permission definitions and entitlements; each receiving service enforces the relevant permission and contextual scope.
- Code checks permissions and claims, never hard-coded job titles.
- Production deployment uses a non-human, short-lived workload identity scoped to an approved manifest.
- A release creator cannot be the sole approver of the same protected production release.
- Emergency access is time-bound, reason-bound, independently reviewed and fully audited; it is not a permanent role.
- Observability queries must enforce environment and tenant boundaries when they contain tenant-derived business data.
- Viewing a dashboard never grants authority to perform a recovery or financial action.
- Pipeline administrator access does not imply banking application permissions.

## 5. Operational domain model

### Control-plane concepts

| Concept | Responsibility | System of record | Important relationships |
| --- | --- | --- | --- |
| ReleaseManifest | Identifies the exact releasable set | Approved artifact repository | References artifacts, migrations, contracts and configuration revisions |
| ReleaseApprovalCase | Records release approval policy and decisions | Operations and Workflow | References manifest digest and audit IDs |
| DeploymentPlan | Orders environment-specific deployment and verification steps | Version-controlled delivery definition | References one immutable manifest |
| MigrationPlan | Describes service-owned migrations, impact, backup, verification and recovery | Version-controlled release evidence | One database owner only |
| DeploymentExecution | Captures pipeline execution and outcome | Delivery platform | References plan, environment and deployment identity |
| ReadinessAssessment | Records gate results for a service or release | Evidence repository with audit reference | Cannot replace individual test evidence |
| OperationalIncident | Coordinates impact, ownership, response and recovery | Operations and Workflow | Links alerts, traces, transactions and deployments |
| RunbookDefinition | Versioned executable or manual recovery procedure | Version control | Includes prerequisites, stop conditions and verification |
| RunbookExecution | Records operator, approved version, steps and outcome | Operations and Workflow plus audit | Links incident and evidence |
| ServiceInventoryEntry | Declares owner, dependencies, endpoints and support data | Version-controlled service catalogue | One per deployed service/environment |
| SliDefinition | Defines a measurable service or business indicator | Version control | Threshold supplied by approved service tier |
| FeatureFlagDefinition | Defines safe scope, default, owner and expiry | Approved configuration system | Cannot bypass invariant controls |

This epic creates no tenth platform database. Delivery execution data remains in the approved delivery platform. Application-level cases reside in `operations_workflow_db`. Immutable privileged-action evidence resides in `audit_reconciliation_db`. Large evidence bundles reside in approved immutable artifact or object storage and are referenced by digest.

### Value objects

- `ReleaseId`, `ManifestDigest`, `ArtifactDigest`, `PipelineRunId` and `DeploymentId`.
- `ServiceName`, `ServiceVersion`, `ContractVersion` and `MigrationId`.
- `EnvironmentName`, `ConfigurationRevision` and `InfrastructureRevision`.
- `ApprovalPolicyVersion`, `ApprovalDecision`, `EvidenceUri` and `EvidenceDigest`.
- `IncidentId`, `Severity`, `ImpactScope`, `RunbookVersion` and `RecoveryActionId`.
- `TraceId`, `CorrelationId`, `CausationId`, `TransactionReference` and `AuditEventId`.
- `SliName`, `MeasurementWindow`, `ServiceTier` and an approved target reference.

### Invariants

1. A release manifest is immutable after approval; any content change produces a new identifier and digest.
2. The artifact promoted to production is byte-for-byte the artifact tested and approved.
3. Every artifact, contract, migration and configuration revision in a release is explicit; floating tags are prohibited for deployment evidence.
4. A protected-environment deployment requires all applicable gates and approvals to pass or a separately approved, time-bound exception.
5. An exception records owner, reason, risk, compensating control, expiry and review evidence; it cannot silently convert a failed gate to success.
6. One service owns and executes each database migration against only its database.
7. Application replicas never race to apply production migrations at startup.
8. A released migration is append-only; correction uses a new migration.
9. Destructive or long-running changes require impact analysis, backup/restore dependency, backfill, verification and forward-fix or rollback strategy.
10. Deployment and migration identities are least privilege and environment scoped.
11. Backend authorisation and tenant scope are enforced even when the caller is a trusted staff portal.
12. Telemetry contains no credentials, private keys, tokens, full PINs, OTPs or prohibited personal data.
13. A health endpoint does not disclose connection strings, dependency addresses, exception details or secrets.
14. Retry, restart, replay, rollback and scale-out cannot create duplicate financial outcomes.
15. An incident recovery action affecting money or customer access requires the same permission, workflow and audit controls as the ordinary action.
16. Observability loss is detectable and alertable; it cannot fail open for mandatory audit evidence.
17. A feature flag has an owner, safe default, scope, expiry or review date and removal plan.
18. Production targets and workloads are approved inputs, not constants inferred by implementers.

## 6. State models

### Release candidate

```text
Draft -> Validating -> Rejected
                  -> ReadyForApproval -> Rejected
                                     -> Approved -> Superseded
                                                 -> Promoting -> Failed
                                                              -> Deployed -> Verified
                                                                          -> RolledBack
                                                                          -> ForwardFixed
```

- `Validating` executes deterministic build, test, contract, security and packaging gates.
- `Approved` binds decisions to the manifest digest; changing content invalidates approval.
- `Deployed` means infrastructure accepted the deployment, not that business verification passed.
- `Verified` requires technical health plus defined synthetic/business checks.
- A failed release remains inspectable with retained evidence.

### Migration execution

```text
Planned -> Reviewed -> Approved -> PreflightPassed -> Executing
                                                    -> Failed
                                         Executing -> Applied -> Verified
                                                              -> ForwardFixRequired
                                                              -> RolledBackWhereSafe
```

- Rollback is allowed only when the migration plan proves it is safe.
- Data-destructive rollback is not assumed. Forward-fix is the default when reversal would lose committed information.
- Contract steps occur only after all active application versions no longer depend on removed schema.

### Incident

```text
Detected -> Triage -> Mitigating -> Monitoring -> Resolved -> Reviewed -> Closed
                -> FalsePositive ---------------------------> Closed
```

- Severity, impact and notification rules come from approved incident policy.
- Financial uncertainty remains explicit until authoritative verification or reconciliation resolves it.
- Closing an incident requires recovery evidence and assigned follow-up actions.

## 7. Release manifest and promotion gates

The release manifest must include:

- release identifier, source commit and creation time;
- each service name, semantic or approved version and immutable image digest;
- .NET SDK/runtime and centrally managed dependency-lock evidence;
- API and event schema versions with compatibility results;
- each service's ordered migration identifiers and migration-package digest;
- infrastructure and deployment-definition revision;
- non-secret configuration revision and required secret/certificate references by identifier only;
- feature-flag defaults and rollout scope;
- test-suite results and coverage evidence required by policy;
- dependency, vulnerability, secret, container and infrastructure scan results;
- SBOM or approved dependency inventory and artifact provenance;
- performance, resilience, restore and provider-certification evidence where applicable;
- open exceptions with approval and expiry;
- deployment, verification, rollback and forward-fix plans;
- approval policy version, decisions and immutable audit references.

### Mandatory gate order

1. Restore dependencies from locked and approved sources.
2. Verify formatting, analyzers, warnings policy and architecture tests.
3. Build all affected .NET projects reproducibly.
4. Run unit, integration, contract, security and applicable end-to-end tests.
5. Generate and compare OpenAPI and event schemas for compatibility.
6. Validate EF Core migrations against an empty database and an approved representative prior schema.
7. Scan source, dependencies, secrets, artifacts, containers and infrastructure definitions.
8. Produce immutable artifacts, dependency inventory and provenance.
9. Deploy to the required non-production environment using the release manifest.
10. Run migration, smoke, synthetic, resilience and performance evidence required by change risk.
11. Obtain independent approvals for the exact manifest digest.
12. Promote the same artifacts to production using the approved plan.
13. Run technical and business verification, observe the defined stabilization window and retain evidence.

The exact tooling, approval count and stabilization window require approved policy. The pipeline must make these configurable and auditable, not hard-coded guesses.

## 8. Staff operations backend

US-074 is allocated here only for its backend and operational-readiness concerns. Each domain service continues to own its commands and data. A staff BFF or gateway may compose responses but cannot become a second domain layer or database integration point.

### Required response context

- authenticated staff subject and session assurance;
- active institution and permitted branches;
- effective permissions and any resource/amount scope needed by the client;
- action availability derived from current server state;
- pending approval state where applicable;
- audit or trace reference for critical completed actions;
- localized date, time, currency and stable status codes;
- safe pending, uncertain and failed states that do not imply financial success.

### Rules

- Navigation hints improve usability but are not an authorisation boundary.
- Each downstream service revalidates permission and scope using trusted identity context.
- The BFF calls versioned APIs; it never reads service databases.
- Collection endpoints are paginated and bounded by approved filters.
- Critical commands support confirmation context, idempotency where applicable and explicit outcome state.
- Backend contracts provide accessibility-supporting labels/status semantics, while WCAG conformance remains a frontend responsibility.
- Locale, timezone and currency are explicit context; storage and financial precision do not depend on display formatting.
- Cross-tenant resource identifiers return the approved non-disclosing denial behaviour.

### Minimum backend contracts

| Method | Route | Permission | Purpose | Idempotent |
| --- | --- | --- | --- | --- |
| GET | `/api/v1/staff/context` | Authenticated staff | Return trusted institution, branch and permission context | Yes |
| GET | `/api/v1/operations/work-queue` | `operations.case.view` | Return bounded role/scope-aware work items | Yes |
| GET | `/api/v1/operations/incidents/{incidentId}` | `operations.incident.view` | View incident and safe evidence links | Yes |
| POST | `/api/v1/operations/incidents` | `operations.incident.manage` | Open an operational incident | By idempotency key |
| POST | `/api/v1/operations/incidents/{incidentId}/actions` | `operations.incident.manage` | Record a controlled recovery action | By action ID |
| POST | `/api/v1/operations/runbook-executions` | `operations.runbook.execute` | Start an approved runbook execution case | By idempotency key |
| GET | `/api/v1/operations/releases/{releaseId}` | `operations.evidence.view` | View release state and safe evidence references | Yes |

Domain-specific staff operations remain in their owning service APIs and epics. Stable problem details must distinguish forbidden scope, approval required, stale state, conflict, uncertain result and temporarily unavailable outcomes.

## 9. CI/CD and artifact controls

- CI runs from reviewed version-controlled definitions.
- Pull-request validation does not hold production credentials.
- Protected branch and release policies require the approved reviews and status checks.
- Build workers use ephemeral or equivalently isolated execution with least privilege.
- Dependencies are restored from approved registries with locked versions and integrity verification.
- Generated artifacts are immutable, content-addressable where supported and retained by policy.
- Artifact signing or attestation is required if selected by the supply-chain ADR; verification occurs before deployment.
- Secret detection blocks exposure and triggers rotation procedures; deleting the committed text alone is insufficient.
- Security findings are risk classified. Blocking thresholds and exception authority require security policy approval.
- Pipeline logs redact secrets and do not contain production customer data.
- Production deployment credentials are not accessible from ordinary build steps.
- Manual reruns preserve the same manifest and idempotently detect already-applied deployment steps.
- Pipeline evidence includes who or what initiated, approved and executed every protected action.

## 10. Database migration and deployment strategy

### Migration packaging

- Each service builds a dedicated C# migration bundle or approved equivalent from its own EF Core migration assembly.
- The package contains ordered migration IDs, target schema state, source digest and verification instructions.
- A separate least-privilege migration identity owns only the required DDL/DML permissions for that service database.
- Application runtime identities do not receive schema-administration privileges.

### Expand-migrate-contract

1. Expand with backward-compatible schema additions.
2. Deploy code able to operate with old and new representations where required.
3. Backfill in bounded, restartable, observable batches.
4. Verify completeness and consistency using approved queries and business checks.
5. Switch reads/writes using a controlled, reversible configuration transition.
6. Remove old schema only after compatibility windows and rollback dependencies have expired.

### Preflight

- Confirm service, database, environment and current migration state.
- Confirm backup/PITR evidence and restore dependencies for the approved risk class.
- Detect locks, long-running transactions, disk capacity and expected duration risks.
- Verify compatibility with currently deployed and immediately previous approved application versions as defined by ADR.
- Verify no migration crosses a service-owned database boundary.
- Require explicit approval for destructive, blocking or long-running work.

### Deployment sequencing

- Dependency order is declared in the deployment plan, never inferred from repository folder order.
- API/event consumers are made tolerant before incompatible producers change.
- Consumers can safely pause, resume and deduplicate around deployments.
- Readiness remains false while mandatory local dependencies or migrations are unavailable.
- Graceful shutdown stops accepting new work, completes or safely abandons bounded work and commits offsets/checkpoints only after durable outcomes.
- Canary, blue/green, rolling or another strategy is selected by the deployment ADR and service risk; implementers must not assume one.

### Recovery choice

- Roll back application code only when its contracts and schema remain compatible.
- Roll back configuration to a known version only when doing so cannot violate new data or policy assumptions.
- Never reverse a migration by deleting committed financial or audit data.
- Use an approved forward-fix when rollback is unsafe.
- Stop and invoke the incident process when authoritative financial outcome is uncertain.

## 11. Observability and health standards

### Structured logs

Every service uses stable event names and includes only applicable safe fields:

- timestamp, severity, service, version, environment and event name;
- trace, span, correlation and causation identifiers;
- institution and branch as protected structured attributes where approved;
- actor/service identity reference, permission and action for privileged operations;
- safe resource reference, outcome, duration and stable error code;
- provider, queue, job, workflow or release reference without secret payloads.

Logs exclude secrets, tokens, credentials, private keys, PINs, OTPs, full payment credentials, document content and unrestricted request/response bodies. Personal identifiers are omitted, tokenised or masked according to classification policy.

### Metrics

Technical metrics include:

- request rate, latency, errors and saturation;
- process/runtime health, resource use and connection-pool pressure;
- queue depth, consumer lag, retry count, dead-letter count and oldest-message age;
- outbox/inbox backlog, dispatch age and duplicate suppression;
- scheduled-job success, duration, late start and checkpoint age;
- dependency latency, timeout, circuit state and provider health;
- deployment, migration and health-transition outcomes.

Business and control metrics include:

- posting failures and imbalance-prevention attempts;
- uncertain, failed and reversed payment counts;
- reconciliation breaks and ageing;
- stuck approvals, cases and workflows;
- EOD state, checkpoint delay and restart count;
- audit ingestion/integrity failures;
- notification/provider failover and delivery failure;
- security control, certificate and secret-expiry warnings.

High-cardinality values such as customer, account, transaction, trace and free-text reason are not metric labels.

### Distributed traces

- W3C trace context or the approved standard propagates through HTTP and messages.
- Message links preserve causation across asynchronous boundaries without assuming a single long-lived span.
- Provider spans record safe endpoint/operation categories and outcome, not credentials or prohibited payloads.
- Sampling policy retains the diagnostic value required for errors and financial exceptions without violating privacy or cost controls.
- Audit identifiers may be linked as safe references; audit evidence is not copied into traces.

### Health endpoints

| Endpoint | Purpose | Behaviour |
| --- | --- | --- |
| `/health/live` | Process should be restarted only when it cannot make progress | Avoid routine external dependency checks |
| `/health/ready` | Replica may receive traffic or work | Validate mandatory local readiness and safe dependency state |
| `/health/startup` | Slow initialization has completed | Remains false during startup initialization |

Responses expose only coarse status to untrusted callers. Detailed component diagnostics require authenticated operational access and still exclude secrets.

### Dashboards and alerts

- Fleet dashboards show service version, deployment, traffic, errors, latency, saturation and dependency state.
- Business dashboards show tenant-scoped financial and workflow exceptions only to authorised viewers.
- Release dashboards correlate deployment markers with service and business health.
- Alerts are actionable, severity-classified, deduplicated and linked to an owner and runbook.
- Alert definitions state detection condition, evaluation window, missing-data behaviour, routing, escalation and recovery condition.
- Alerts do not include sensitive payloads in notification channels.
- Every production alert is tested or otherwise verified before relying on it.

## 12. Service levels, capacity and resilience

- Service tiers, SLIs, SLOs, availability and error-budget policy require stakeholder approval.
- NFR performance values are treated as targets under an agreed production workload, excluding approved external-provider latency where specified.
- Load models state tenant count, active users, request mix, data volume, concurrency, message rates, batch overlap, provider latency and failure assumptions.
- Performance tests cover normal, peak, burst, soak and recovery conditions appropriate to risk.
- Posting tests measure correctness under concurrency before throughput.
- Stateless APIs demonstrate horizontal scale without loss of tenant isolation, idempotency or required ordering.
- Consumers demonstrate safe rebalance, restart, redelivery and poison-message handling.
- Provider dependencies demonstrate timeout, retry, circuit-breaker, queueing and degraded-operation behaviour.
- Capacity evidence includes database, broker, cache, object storage, connection pools, worker concurrency and observability pipeline limits.
- A capacity result without workload definition, dataset, environment, version and raw evidence is not an approval artifact.
- Approved RTO/RPO and restore exercises are governed by Epic 11 and referenced by the release readiness assessment.

## 13. Incident response and runbooks

### Minimum incident evidence

- detection source and first-observed time;
- affected environments, services, institutions, branches and journeys;
- safe transaction, trace, case, deployment and audit references;
- severity, customer/financial/compliance impact and uncertainty;
- incident commander, responders and decision log;
- containment, mitigation, recovery and verification actions;
- communications references without copying sensitive content;
- resolved time, residual risk and follow-up actions;
- post-incident review and control improvements.

### Required Phase 1 runbooks

- Service deployment, verification and failed-deployment handling.
- Application rollback and migration forward-fix decision.
- Database migration failure, blocked migration and backfill restart.
- Provider outage, timeout storm and circuit-open state.
- Uncertain or duplicated payment investigation without unsafe replay.
- Queue backlog, poison message, dead-letter and consumer-lag recovery.
- Outbox/inbox backlog and idempotent replay.
- Stuck workflow, approval case and controlled reassignment.
- Audit ingestion failure and evidence integrity alert.
- Ledger posting failure and financial exception escalation.
- EOD failure, checkpoint inspection and safe restart.
- Secret or certificate expiry, rotation and suspected exposure.
- Backup restoration and post-restore verification, referencing Epic 11.
- Observability pipeline degradation and monitoring blind spot.
- Security event escalation to the approved SIEM/incident process.

Each runbook defines prerequisites, permission, step-up and approval needs, exact safe commands or interfaces, expected evidence, stop conditions, escalation, verification and reversal/forward-fix. Runbooks must not instruct operators to edit ledger, audit or service tables directly.

## 14. Use cases

### UC-01 - Build and validate a release candidate

- Actor: Release engineer or CI identity.
- Preconditions: Reviewed source revision and version-controlled pipeline.
- Permission and scope: Build identity; no production access.
- Main flow: Build, test, scan, generate contracts, package immutable artifacts, produce provenance and create manifest.
- Failure outcomes: Candidate rejected with retained gate evidence.
- Idempotency: Rebuilding the same approved inputs must not overwrite prior artifacts.
- Audit evidence: Source, artifacts, gates, actor and pipeline run.

### UC-02 - Approve a protected-environment release

- Actor: Release and security approvers as policy requires.
- Preconditions: All mandatory gates pass; exact manifest digest exists.
- Permission and scope: `release.approve` or `security.release.approve`.
- Main flow: Review evidence, conflicts and exceptions; record signed decision against digest.
- Alternative: Reject or request remediation.
- Invariant: Creator cannot be the sole approver where maker-checker applies.
- Audit evidence: Decision, reason, scope, policy version and digest.

### UC-03 - Promote and verify a release

- Actor: Deployment identity after approval.
- Preconditions: Approved immutable manifest and healthy target prerequisites.
- Main flow: Preflight, migrate in declared order, deploy, verify health, run synthetic/business checks and mark verified.
- Failure outcomes: Stop rollout, preserve uncertainty, invoke rollback/forward-fix plan and open incident when thresholds require.
- Idempotency: Repeated execution detects completed steps by release, environment and step ID.
- Audit evidence: Execution identity, steps, versions, outcome and verification.

### UC-04 - Execute a controlled migration

- Actor: Migration identity.
- Preconditions: Approved plan, backup dependency satisfied, preflight passed.
- Main flow: Acquire approved coordination lock, apply package, verify schema/data/business checks and record outcome.
- Failure outcomes: Stop, release lock safely and follow the approved recovery choice.
- Transaction boundary: Defined per migration; long backfills are bounded and restartable.
- Audit evidence: Database/service, migration IDs, package digest and safe result.

### UC-05 - Use a role-aware staff operation

- Actor: Bank staff user.
- Preconditions: Authenticated session with active institution and branch context.
- Permission and scope: Endpoint-specific permission plus contextual policy.
- Main flow: BFF retrieves allowed context, domain service reauthorises, command/query executes and response includes status plus audit/approval reference when critical.
- Failure outcomes: Stable denial, approval-required, conflict, pending or unavailable result.
- Invariant: No frontend or BFF database access.

### UC-06 - Detect and manage an incident

- Actor: Reliability engineer or automated alert identity.
- Preconditions: Actionable signal or reported impact.
- Main flow: Create/deduplicate incident, assess scope, link deployment/traces/transactions, execute approved mitigation and verify recovery.
- Failure outcomes: Escalation and preservation of financial uncertainty.
- Audit evidence: Decisions, actors, actions, links and outcome.

### UC-07 - Execute an approved runbook

- Actor: Runbook operator.
- Preconditions: Current approved runbook, required permissions and incident/change reference.
- Main flow: Record version, validate prerequisites, execute bounded steps, capture evidence and verify.
- Failure outcomes: Stop condition triggers escalation; no improvised destructive database work.
- Idempotency: Each step declares whether repeat is safe and how completion is detected.

### UC-08 - Change a feature flag safely

- Actor: Feature-flag operator.
- Preconditions: Approved flag definition, safe default and impact plan.
- Main flow: Request scoped change, obtain approval where required, roll out gradually if supported, observe defined signals and retain audit evidence.
- Failure outcomes: Revert to safe value or invoke incident plan.
- Invariant: Flag cannot bypass a security, tenant, accounting, idempotency, audit or reconciliation control.

## 15. Event and evidence contracts

### Operational events

| Event | Producer | Trigger | Minimum safe fields | Consumers |
| --- | --- | --- | --- | --- |
| `ReleaseCandidateValidated` | Delivery integration | Required gates pass | Release ID, manifest digest, evidence refs | Operations and Workflow, Audit |
| `ReleaseApproved` | Operations and Workflow | Approval completed | Release ID, digest, policy version, audit ID | Delivery integration, Audit |
| `DeploymentStarted` | Delivery integration | Protected deployment begins | Release, environment, service set, execution ID | Observability, Audit |
| `DeploymentCompleted` | Delivery integration | Verification completes | Release, environment, outcome, evidence refs | Observability, Audit |
| `MigrationCompleted` | Service migration runner | Migration verification ends | Service, environment, migration set, outcome | Delivery integration, Audit |
| `OperationalIncidentOpened` | Operations and Workflow | Incident accepted | Incident, severity, safe scope refs | Audit, notifications |
| `RecoveryActionRecorded` | Operations and Workflow | Controlled action completes | Incident, action type, result, audit ID | Audit, trace model |
| `FeatureFlagChanged` | Configuration integration | Approved value changes | Flag ID, scope, before/after refs, approval | Audit, observability |

Operational events use the approved event envelope, outbox/inbox where application state participates, schema versioning, deduplication, bounded retry and dead-letter handling. They contain evidence references rather than pipeline logs or sensitive payloads. Delivery-platform webhooks are treated as untrusted integration input until signature, source, replay and manifest-digest validation succeeds.

### Evidence package

Every evidence object records:

- evidence type, schema version and creation time;
- release, deployment, migration, incident or runbook identity;
- producing system and authenticated identity;
- environment and safe service scope;
- content digest, immutable URI and retention class;
- correlation and immutable audit reference;
- classification and authorised viewer policy.

## 16. Database design and evidence ownership

### No new platform database

This epic does not create an `operations_platform_db` or shared schema.

| Data | Owner | Storage |
| --- | --- | --- |
| Service migration history | Owning microservice | Its PostgreSQL database |
| Release/incident approval case | Operations and Workflow | `operations_workflow_db` |
| Privileged action evidence | Audit and Reconciliation | `audit_reconciliation_db` |
| Build artifacts and large evidence | Delivery/artifact platform | Approved immutable repository |
| Dashboards, alert rules and runbooks | Platform engineering | Version control and approved observability systems |
| Secret values | Security/platform owner | Approved secret manager only |

### Operations and Workflow additions

Any required tables must follow Epic 6 patterns and migrations. Candidate records include release approval case metadata, incident metadata and runbook execution metadata. They store references and digests, not copies of artifacts, credentials, unrestricted logs or other services' business data.

Required constraints include:

- institution ownership where an operational record is tenant specific;
- uniqueness of external release/deployment/incident references within source and environment;
- optimistic concurrency for decision/state changes;
- append-only decision and action history;
- valid state and severity checks;
- evidence digest required before a privileged action can be marked verified;
- no cross-service database foreign keys.

### Migration evidence

Each migration change supplies:

- generated migration identifier and owning assembly;
- data classification and affected objects;
- compatibility, locking, size and duration analysis;
- expand/backfill/contract plan where applicable;
- preflight, verification and reconciliation queries;
- backup/restore dependency;
- rollback or forward-fix decision;
- integration-test evidence from empty and prior representative schemas.

## 17. Consistency, concurrency and idempotency

- Approval state and application-level incident/runbook records use local transactions plus outbox publication.
- Delivery-platform state is integrated asynchronously; no distributed transaction spans workflow, artifact store, pipeline and audit.
- Release approval is bound to the immutable manifest digest to prevent time-of-check/time-of-use substitution.
- Deployment steps use `(ReleaseId, Environment, StepId)` or an approved equivalent as the deduplication scope.
- Migration execution uses one service/database coordination lock and verifies the exact current schema before applying.
- Webhook ingestion deduplicates by trusted provider event identity and content evidence after authentication.
- Concurrent feature-flag or configuration updates use version comparison and reject stale writes.
- Alert-to-incident integration deduplicates by rule, affected scope and active time window according to approved policy.
- Retry of an operational command returns the prior result or current state; it never repeats an unsafe financial effect.
- Cross-system disagreement opens an explicit reconciliation or incident state rather than overwriting evidence.

## 18. Security, privacy and supply chain

### Threats

- Artifact substitution, dependency confusion and compromised build workers.
- Approval bypass, self-approval or use of stale approval after manifest change.
- Privilege escalation through deployment, migration, observability or emergency access.
- Cross-tenant exposure in staff operations, dashboards, traces or incident records.
- Secret leakage through configuration, build logs, health details or telemetry.
- Malicious or replayed delivery-platform callbacks.
- Destructive migrations, unauthorised repair and tampering with release evidence.
- Production data copied into test or developer environments.
- Alert suppression or monitoring blind spots used to hide abuse.

### Controls

- Short-lived workload identities, environment-scoped roles and protected credentials.
- Trusted build inputs, locked dependencies, integrity checks, vulnerability monitoring and approved registries.
- Immutable digests, provenance and signing/verification where required by ADR.
- Independent approval and conflict-of-role evaluation.
- Signature, timestamp, audience and replay validation on integration callbacks.
- Encryption in transit and at rest with key ownership and rotation.
- Central redaction libraries plus automated secret/PII leakage tests.
- Tenant and branch enforcement in application APIs and authorised observability access.
- Version-controlled alerting with monitored ingestion health.
- Time-bound exceptions and emergency access with retrospective review.
- Synthetic data by default; approved irreversible masking before any exceptional production-derived non-production dataset.
- Dependency, patch, certificate and secret-expiry alerts with named owners.
- SIEM/incident integration for applicable security events.

## 19. Failure, retry and recovery

| Failure | Required safe behaviour | Recovery evidence |
| --- | --- | --- |
| Build/test/scan gate fails | Reject candidate; do not publish as approved | Gate output and remediation link |
| Artifact digest differs | Stop promotion and raise security incident | Expected/actual digest and identity |
| Approval service unavailable | Protected deployment remains blocked | Availability signal and deferred case |
| Migration preflight fails | Do not mutate schema | Preflight result and plan reference |
| Migration fails | Stop dependent rollout; preserve applied state; follow approved plan | Migration output, schema state, decision |
| Readiness fails after deploy | Remove affected replica from traffic and stop/limit rollout | Health, logs, traces and deployment step |
| Consumer backlog grows | Apply bounded scaling or pause/recovery runbook; preserve ordering/idempotency | Lag, action and drain verification |
| Provider becomes unreliable | Enforce timeout/circuit/degraded path; retain uncertain outcomes | Provider health and reconciliation refs |
| Audit ingestion fails | Alert and block privileged action where mandatory evidence cannot be retained | Buffer/repair and integrity verification |
| Telemetry pipeline fails | Alert through independent path where feasible; record blind spot | Incident and restored-ingestion proof |
| Rollback unsafe | Do not force reversal; execute approved forward-fix | Decision and post-fix verification |
| Secret exposure suspected | Revoke/rotate, contain, investigate and audit; do not merely delete text | Rotation and incident evidence |
| Production data found in non-production | Isolate access, initiate security/privacy response and remove only under approved process | Incident and remediation evidence |

Retries use bounded exponential backoff with jitter where appropriate and respect operation semantics. Non-idempotent migration, deployment and recovery steps must expose explicit completion detection and operator stop conditions.

## 20. Configuration

| Setting | Owner | Secret | Validation | Reload behaviour |
| --- | --- | --- | --- | --- |
| Environment identity | Platform engineering | No | Required and allow-listed | Restart/deploy |
| Artifact repository endpoint | Platform engineering | No | HTTPS and approved host | Restart/deploy |
| Artifact credential reference | Security/platform | Reference only | Must resolve for scoped identity | Rotation without source change |
| Approval policy version | Operations/security | No | Published and effective | New cases use approved version |
| Deployment strategy | Platform/service owner | No | Approved values only | Per deployment plan |
| Health-check thresholds | Service owner/SRE | No | Positive, bounded and tier-linked | Versioned rollout |
| Telemetry exporter endpoint | Platform engineering | No | Approved encrypted endpoint | Controlled reload |
| Telemetry credential reference | Security/platform | Reference only | Must resolve and be scoped | Rotation supported |
| Trace sampling policy | SRE/security | No | Bounded and privacy reviewed | Controlled reload |
| Alert route reference | SRE | Sensitive reference | Approved destination | Controlled reload |
| Feature flag | Domain owner | Usually no | Owner, safe default, scope and expiry | Audited controlled reload |
| Service-level target reference | Product/SRE/risk | No | Approved and versioned | New measurement policy |

Configuration validation fails startup or readiness when a mandatory security or correctness dependency is invalid. Secrets are never emitted in configuration dumps or diagnostic endpoints.

## 21. Implementation slices

1. Define and approve the deployment, observability, migration/recovery and supply-chain ADRs.
2. Create a versioned service inventory and release-manifest schema.
3. Standardise .NET build, analyzer, test, contract and artifact packaging across all services.
4. Add dependency inventory, secret, vulnerability, container and infrastructure scanning gates.
5. Implement protected environment promotion with immutable artifacts and approval binding.
6. Package service-owned migrations and validate expand-migrate-contract behaviour.
7. Add shared technical observability conventions without sharing domain models.
8. Implement liveness, readiness, startup, graceful shutdown and deployment verification per service.
9. Add role-aware staff context and operations case/incident APIs with cross-tenant negative tests.
10. Define dashboards, alerts and business health signals for every Phase 1 service.
11. Implement incident/runbook evidence and immutable audit integration.
12. Run approved performance, resilience, restore and replay exercises.
13. Execute a full non-production release rehearsal including failed migration and rollback/forward-fix scenarios.
14. Assemble production-readiness evidence and obtain the required business, security, operations and compliance decisions.

Each slice leaves the repository buildable and deployable. No unsafe partial operational endpoint is exposed before permission, audit and failure controls exist.

## 22. Test plan

| Test ID | Requirement/story | Level | Scenario | Expected evidence |
| --- | --- | --- | --- | --- |
| OPS-001 | US-079 | CI | Required build and automated tests pass | Versioned pipeline result |
| OPS-002 | US-079 | CI negative | Unit/integration/security/E2E gate fails | Promotion blocked |
| OPS-003 | US-079 | Supply chain | Artifact digest is changed after approval | Deployment rejected |
| OPS-004 | US-079 | Supply chain | Locked dependency integrity fails | Build rejected |
| OPS-005 | SEC-016 | Security | Blocking vulnerability or expired exception exists | Promotion blocked and owner alerted |
| OPS-006 | SEC-016 | Security | Certificate/secret approaches approved expiry threshold | Actionable alert produced |
| OPS-007 | US-079 | Authorisation | Release creator attempts sole self-approval | Denied and audited |
| OPS-008 | US-079 | Authorisation | Deployment identity targets unapproved environment | Denied |
| OPS-009 | US-079 | Recovery | Failed deployment executes approved rollback path | Prior compatible version verified |
| OPS-010 | US-079 | Recovery | Migration makes rollback unsafe | Forward-fix path selected and evidenced |
| OPS-011 | NFR-MNT-001 | Contract | Breaking OpenAPI change lacks approved version plan | Gate fails |
| OPS-012 | NFR-MNT-001 | Contract | Breaking event change lacks compatible consumer path | Gate fails |
| OPS-013 | US-079 | Migration | Migration runs from two application replicas | Application cannot execute DDL |
| OPS-014 | US-079 | Migration | Empty database receives full migration set | Target schema verified |
| OPS-015 | US-079 | Migration | Representative prior schema upgrades | Data and constraints verified |
| OPS-016 | US-079 | Migration | Backfill restarts after interruption | Resumes without duplicate/corrupt outcome |
| OPS-017 | US-079 | Migration negative | Migration attempts another service database | Access denied |
| OPS-018 | US-074 | API | Staff context reflects institution, branch and permissions | Scoped response |
| OPS-019 | US-074 | Security | Staff requests another tenant's resource | Non-disclosing denial and audit |
| OPS-020 | US-074 | Security | UI hint says allowed but backend permission is absent | Backend denies action |
| OPS-021 | US-074 | API | Critical action completes | Response contains status and audit reference |
| OPS-022 | US-074 | API | Critical action needs maker-checker | Pending approval state returned |
| OPS-023 | US-074 | Architecture | Staff BFF references a service persistence project | Architecture test fails |
| OPS-024 | US-080 | Observability | HTTP-to-message-to-worker path executes | Correlation/causation trace retained |
| OPS-025 | US-080 | Privacy | Secret, OTP or prohibited PII enters diagnostic input | Redacted/omitted from telemetry |
| OPS-026 | US-080 | Health | Optional dependency fails | Liveness remains correct; readiness follows policy |
| OPS-027 | US-080 | Health | Process cannot make progress | Liveness reports failure without detail leak |
| OPS-028 | US-080 | Alert | Queue lag exceeds approved rule | Deduplicated alert links runbook |
| OPS-029 | US-080 | Alert | Financial exception occurs | Tenant-safe alert and trace reference produced |
| OPS-030 | US-080 | Alert | Audit ingestion fails | Independent actionable alert fires |
| OPS-031 | US-080 | Dashboard | Viewer lacks tenant scope | Tenant business panel is denied |
| OPS-032 | NFR-OBS-001 | E2E | Financial journey spans services/providers | End-to-end trace and audit links resolve |
| OPS-033 | NFR-OBS-002 | Security | Health/detail endpoint queried anonymously | No dependency or secret details exposed |
| OPS-034 | NFR-SCAL-001 | Performance | API replicas scale horizontally | Idempotency and tenant isolation retained |
| OPS-035 | NFR-RES-002 | Resilience | Consumer restarts and replays messages | No duplicate financial/audit outcome |
| OPS-036 | NFR-RES-001 | Resilience | Provider timeout/circuit opens | Degraded path and explicit uncertainty |
| OPS-037 | NFR-PERF-001 | Performance | Normal reads under approved workload | Approved latency evidence captured |
| OPS-038 | NFR-PERF-002 | Performance | Internal posting under approved load | Correctness and approved latency evidence |
| OPS-039 | NFR-AVAIL-001 | Resilience | Service-tier availability measurement | SLI calculation matches approved definition |
| OPS-040 | NFR-DR-001 | Recovery | Required backup is absent before risky migration | Deployment blocked |
| OPS-041 | NFR-DR-002 | Recovery | Restore exercise completes | Approved RTO/RPO evidence referenced |
| OPS-042 | SEC-017 | Security | Qualifying security event occurs | SIEM/incident integration receives safe event |
| OPS-043 | SEC-018 | Privacy | Unmasked production record detected in test data | Gate/incident path blocks use |
| OPS-044 | Feature control | Security | Flag attempts to bypass authorisation/audit | Change rejected |
| OPS-045 | Incident | Idempotency | Duplicate alert opens same active incident | Existing incident linked, not duplicated |
| OPS-046 | Incident | Audit | Operator executes approved runbook | Version, steps, results and audit IDs retained |
| OPS-047 | Release | E2E | Full non-production release rehearsal | Deployment, verification and recovery evidence complete |
| OPS-048 | Release negative | E2E | Business verification fails after green health | Release not marked verified |

Performance tests cannot be accepted until the workload, environment, dataset and target are approved. Security and recovery evidence must use synthetic data.

## 23. Acceptance criteria

- [ ] US-074 backend contracts are role-, institution- and branch-aware.
- [ ] No frontend or staff BFF accesses service databases directly.
- [ ] Critical staff actions expose confirmation context, approval state and immutable audit references.
- [ ] US-079 build, automated test, security scan, approval, promotion and retained rollback evidence are implemented.
- [ ] Infrastructure and non-secret configuration are version controlled.
- [ ] Immutable artifacts and exact manifest digests are promoted between environments.
- [ ] Service-owned migrations execute through a controlled, least-privilege deployment step.
- [ ] Compatibility, backfill, verification and rollback/forward-fix plans exist for applicable migrations.
- [ ] US-080 logs, metrics, traces, queues, SLIs and business events are observable.
- [ ] Alerts cover service failure, latency, backlog, failed jobs, financial exceptions, provider state and audit failure.
- [ ] Dashboards enforce operational and tenant scope and expose no secrets.
- [ ] Incident evidence links affected transactions, traces, deployments and recovery actions.
- [ ] Liveness, readiness and startup checks have distinct, tested semantics.
- [ ] Graceful shutdown, restart, retry and replay preserve idempotency and financial correctness.
- [ ] Capacity, availability, RTO and RPO use approved targets and have retained test evidence.
- [ ] Vulnerability, dependency, patch, secret and certificate monitoring has owners and runbooks.
- [ ] Security events integrate with the approved incident/SIEM path.
- [ ] Non-production data satisfies SEC-018.
- [ ] Required operational runbooks are versioned, reviewed and exercised.
- [ ] Traceability runs from requirement/story through design, implementation, test and evidence.

## 24. Definition of Done and Phase 1 exit gate

In addition to the repository Definition of Done:

- All nine microservices are represented in the service inventory and release manifest.
- Every enabled API and worker has owner, health, dashboard, alert and runbook coverage.
- Every service's API/event compatibility and migration checks run in CI.
- A complete Phase 1 release has been rehearsed in an approved production-like environment.
- At least one failed deployment, failed migration, queue/replay, provider outage and restore scenario has produced usable evidence.
- Production permissions and segregation-of-duties mappings are reviewed.
- Release, migration, incident and runbook actions create immutable audit evidence.
- Operational evidence is retained in approved systems with tested access controls.
- Known vulnerabilities and production-readiness exceptions have approved disposition and expiry.
- The go-live cutover and rollback/forward-fix decision tree is approved.
- Business, engineering, security, operations, risk/compliance and release authorities approve their assigned gates.
- No unresolved decision is concealed as a code default.

Completing this document or its code tasks does not by itself authorise production go-live.

## 25. Open decisions

| Decision | Impact | Owner | Required by | Status |
| --- | --- | --- | --- | --- |
| Hosting platform and environment topology | Deployment, identity, networking, scale and recovery | Architecture/Platform | Deployment ADR | Open |
| CI/CD, artifact and provenance products | Pipeline implementation and evidence retention | Platform/Security | CI implementation | Open |
| Container signing/attestation policy | Supply-chain gate | Security/Platform | Artifact promotion | Open |
| Protected-environment approval matrix | Segregation of duties and emergency change | Operations/Security/Risk | Production pipeline | Open |
| Deployment strategies by service tier | Availability and rollback | SRE/Service owners | Production rehearsal | Open |
| API/event compatibility window | Rolling deployment and rollback safety | Architecture | Contract gates | Open |
| Production load and capacity model | Performance/cost approval | Product/SRE/Architecture | Performance tests | Open |
| Service tiers, SLIs, SLOs and availability targets | Alerts and go-live approval | Product/SRE/Risk | Readiness assessment | Open |
| RTO/RPO per institution/service | Recovery architecture and exercise | Business/Risk/SRE | Recovery sign-off | Open |
| Observability, SIEM and on-call tooling | Telemetry, routing and retention | SRE/Security | Operational rollout | Open |
| Log, trace, metric and release-evidence retention | Cost, privacy and compliance | Security/Compliance | Production configuration | Open |
| Alert severity and escalation policy | Response obligations | Operations/SRE | Alert activation | Open |
| Emergency access/change policy | Break-glass controls | Security/Risk/Operations | Production access | Open |
| Data migration and go-live cutover plan | Customer and financial continuity | Business/Architecture/Operations | Go-live rehearsal | Open |
| Production support model and provider escalation | Incident ownership and SLA | Operations/Vendor management | Go-live | Open |

## 26. Prohibited assumptions

- Do not choose a cloud, orchestrator, CI/CD product, observability stack, SIEM or secret manager without an approved decision.
- Do not invent load, concurrency, availability, retention, SLO, RTO, RPO, on-call or alert thresholds.
- Do not treat the SRS performance examples as a complete load model.
- Do not create a shared operational database or a tenth microservice for this epic.
- Do not give application replicas or staff users production schema-administration access.
- Do not run migrations automatically from every service startup.
- Do not rewrite released migrations or delete financial/audit history for rollback.
- Do not rebuild an artifact during environment promotion.
- Do not use floating image tags as release identity.
- Do not treat a health check, provider callback, log line or dashboard as proof of a final financial outcome.
- Do not allow a feature flag, emergency path or pipeline identity to bypass core invariants.
- Do not place credentials, raw tokens, PINs, OTPs, full provider payloads or unrestricted PII in telemetry or pipeline logs.
- Do not copy production data to non-production without formal approval and irreversible masking.
- Do not claim a release, recovery, performance or failover exercise passed unless it actually ran and evidence was retained.

## 27. Agent implementation instructions

### Required reading

- Repository and applicable child `AGENTS.md` files.
- `docs/architecture/BACKEND_ARCHITECTURE.md`.
- Phase 0 implementation documents and evidence.
- This Phase 1 overview and Epics 01 through 11.
- Approved ADRs for deployment, identity, events, observability, migration/recovery and supply chain.
- Service API/event contracts, database migrations, threat models and runbooks affected by the slice.

### Before implementation

1. Identify the requirement IDs, story IDs, services, environments and evidence owners.
2. Confirm all open decisions needed for the slice are approved; do not substitute defaults.
3. Inspect current build, deployment, migration and observability patterns.
4. Define the smallest end-to-end vertical slice and its failure/recovery evidence.
5. Confirm that proposed shared code is technical infrastructure only and does not couple domain models.

### Expected files

- Version-controlled CI/CD definitions and release-manifest schemas.
- Service deployment and migration packages.
- C# observability, health and staff-operations implementation in owning services.
- OpenAPI/event contracts and compatibility baselines.
- Integration, contract, security, performance and resilience tests.
- Dashboards, alert rules, service inventory and runbooks.
- ADRs, traceability and evidence references.

### Boundaries that must not change silently

- C#/.NET-only backend policy.
- Nine initial microservices and their data ownership.
- Core Banking and Ledger authority over balances.
- Identity and Access ownership of permission definitions.
- Operations and Workflow ownership of approval/incident cases.
- Audit and Reconciliation ownership of immutable operational evidence.

### Required verification

- Use the repository-pinned SDK and commands documented by Phase 0.
- Run formatting/analyzers, build and the affected unit, integration, architecture, contract and security tests.
- Validate affected migrations from empty and approved prior schemas.
- Validate API/event compatibility.
- Exercise applicable deployment, failure, retry, replay and recovery paths.
- Scan for secrets and prohibited data in artifacts and telemetry samples.
- Report commands, actual results, assumptions, skipped checks and remaining risks.

### Stop conditions

Stop and request a decision when work would require:

- an unapproved hosting, identity, deployment, observability, retention or recovery choice;
- an invented production target or workload;
- a cross-service database access or shared domain model;
- deletion or irreversible transformation of financial/audit data;
- bypass of permission, maker-checker, audit, reconciliation or tenant isolation;
- production data in non-production without approved masking;
- an unsafe rollback where authoritative financial state may be uncertain.

## 28. Traceability summary

| Requirement/story | Implementation focus | Primary evidence |
| --- | --- | --- |
| US-074 | Role-, institution- and branch-aware staff backend APIs | Authorisation, cross-tenant, approval-state and audit-reference tests |
| US-079 | Controlled CI/CD, versioned infrastructure/configuration and recoverable promotion | Pipeline, scan, approval, manifest, deployment and rollback/forward-fix evidence |
| US-080 | Logs, metrics, traces, queues, SLIs, business events, alerts and incidents | Dashboards, alert tests, trace links and incident recovery evidence |
| NFR-PERF-001/002 | Read and posting latency under agreed workloads | Approved load model and performance results |
| NFR-SCAL-001 | Horizontal scale without idempotency/order loss | Scale and replay tests |
| NFR-AVAIL-001 | Configurable service-tier availability | Approved SLI/SLO definitions and measurements |
| NFR-RES-001/002 | Isolated provider failure and replay-safe outcomes | Resilience, failover and duplicate-prevention tests |
| NFR-DR-001/002 | Backup/PITR, restore and approved RTO/RPO | Epic 11 recovery evidence and readiness gate |
| NFR-OBS-001/002 | End-to-end traceability with safe telemetry | Trace continuity and leakage tests |
| NFR-USA-001 | Responsive, role-aware staff workflows | Backend context/status contracts and frontend evidence |
| NFR-ACC-001 | WCAG 2.2 AA target | Frontend-owned accessibility evidence; backend semantic contracts |
| NFR-I18N-001 | Locale, currency, timezone and country-pack support | Explicit context and formatting contract tests |
| NFR-MNT-001 | Versioned contracts, tests, health and runbooks | CI gates and service readiness inventory |
| SEC-016 | Vulnerability, dependency, patch and certificate monitoring | Scan/expiry alerts and remediation evidence |
| SEC-017 | Security event incident/SIEM integration | Safe event contract and integration test |
| SEC-018 | No unapproved production data in non-production | Dataset provenance, masking and negative gate tests |

Epic 12 is complete only when the controls above operate across the enabled Phase 1 services and a full release rehearsal produces reviewable evidence. Prose alone is not evidence of production readiness.
