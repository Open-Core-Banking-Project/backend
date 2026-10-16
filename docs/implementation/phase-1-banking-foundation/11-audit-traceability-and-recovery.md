---
phase: "Phase 1 - Banking Foundation"
epic: "Audit, Traceability and Recovery"
owning_service: "Audit and Reconciliation"
modules:
  - Immutable Audit
  - Sensitive Access Evidence
  - Configuration Change Evidence
  - Financial Trace Graph
  - Evidence Export
  - Backup Catalogue
  - Restore Orchestration Evidence
  - Integrity Verification
status: Draft
requirements:
  - AUD-001
  - AUD-002
  - AUD-003
  - AUD-004
  - DAT-008
  - NFR-SCAL-001
  - NFR-RES-002
  - NFR-DR-001
  - NFR-DR-002
  - NFR-OBS-001
  - NFR-OBS-002
  - NFR-MNT-001
  - SEC-008
  - SEC-009
  - SEC-010
  - SEC-011
  - SEC-013
  - SEC-015
  - SEC-017
  - SEC-018
stories: [US-070, US-071, US-072]
dependencies:
  - Phase 0 architecture, local infrastructure and security foundations
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
adrs:
  - Enterprise audit envelope and source-commit policy
  - Append-only storage and tamper-evidence model
  - Financial trace identity and typed-link model
  - Audit retention, legal hold and evidence-export model
  - Backup catalogue and recovery-orchestration ownership
  - Cross-service PITR reconciliation and replay policy
last_updated: 2026-10-15
---

# Epic: Audit, Traceability and Recovery

## 1. Objective

Deliver the C#/.NET audit and recovery foundation that preserves immutable evidence of privileged actions and sensitive access, traces a financial journey end to end across services, and proves that encrypted backups can be restored without creating missing, duplicated or unauthorised outcomes.

Each source service remains responsible for recording audit evidence as part of its local business transaction and publishing it through an outbox. Audit and Reconciliation consumes that evidence idempotently into a protected append-only store and builds non-authoritative trace links. The central audit service cannot repair, approve, reverse or alter the source business record.

Recovery is platform-wide but evidence is coordinated here. Backup and restore execution may use approved infrastructure automation; this service records catalogues, plans, checkpoints, approvals, verification results and evidence. Cross-service recovery does not assume a distributed transaction or globally atomic snapshot. Services reopen only after their own invariants and cross-service references reconcile.

## 2. Success measures

- Every privileged action, approval, override, sensitive-data access, configuration publication and manual financial operation commits source-owned audit evidence with the corresponding local state change.
- Audit ingestion is at-least-once and idempotent: replay produces one accepted audit event identity without losing duplicate-attempt evidence where needed.
- Ordinary administrators and runtime identities cannot update or delete accepted audit events, trace links, integrity checkpoints or evidence-package manifests.
- Audit records capture the required actor, role/service context, institution, branch where applicable, action, time, source, reason, before/after change evidence and correlation without leaking prohibited data.
- A financial trace can connect channel/API request, authentication/step-up, approval, payment/provider attempts, Ledger transactions/journals, notifications and reconciliation/settlement references where those stages exist.
- Missing, delayed, conflicting and cyclic trace evidence is explicit; the trace engine never fabricates a completed journey.
- Search by protected customer reference, bank reference, provider reference, transaction reference or correlation ID is tenant-scoped, permission-scoped, masked and bounded.
- Evidence exports are authorised, reproducible, integrity-protected, access-controlled and linked to exact filters, event IDs, schema versions and integrity checkpoints.
- Backup catalogues prove encryption, scope, location class, source system, recovery point, verification and retention without exposing secret material.
- Scheduled isolated restore exercises measure actual recovery point/time against approved institution/service targets and retain step-by-step evidence.
- Post-restore gates verify financial invariants, identity revocation freshness, configuration versions, idempotency, sequences, checkpoints and outbox/inbox continuity before traffic/workers resume.
- Replay or job restart after restore does not create a duplicate posting, payment, provider submission, notification, approval, document or audit event.
- AUD-001 through AUD-004, NFR-DR-001/002, NFR-OBS-001/002 and US-070 through US-072 have traceable implementation and exercise evidence.

RTO, RPO, backup frequency, retention, geographic placement, integrity algorithms and evidence-export formats require approval. This epic records and tests approved targets; it does not invent them.

## 3. Scope and ownership boundaries

### Included

- Versioned enterprise audit envelope and source-service audit-emission rules.
- Append-only ingestion, deduplication, classification, retention assignment and integrity verification.
- Dedicated evidence for privileged actions, approvals, overrides, configuration changes, sensitive-data access and manual financial work.
- Typed financial trace nodes/links and completeness policies for approved Phase 1 journeys.
- Tenant-scoped audit/trace search, detail, timeline and safe evidence navigation.
- Controlled evidence export, manifest generation, watermark/tamper evidence and secure artifact handoff to Documents.
- Audit integrity checkpoints and verification jobs using approved cryptographic/key policies.
- Backup inventory/catalogue metadata for service databases, object stores, configuration and required messaging/checkpoint state.
- Recovery plan/version, restore exercise, step/checkpoint, invariant verification, approval and evidence records.
- Isolated restore-exercise workflow and production restore evidence gates.
- Cross-service recovery ordering, traffic/worker fencing, state reconciliation, projection rebuild and controlled replay evidence.
- Audit/trace/backup/restore dashboards, alerts and runbooks.
- Legal hold and approved retention/disposition for audit/evidence records.

### Source-service responsibilities

- Determine which business action/access requires evidence under approved policy.
- Capture exact actor/service, permission, resource, decision, reason and safe before/after change set.
- Commit the business state, local evidence reference and outbox record atomically.
- Generate stable event, correlation, causation, aggregate and business references.
- Exclude secrets and unnecessary PII before publication.
- Retain local authoritative state/history and expose controlled verification APIs.
- Make commands/jobs idempotent so replay/recovery is safe.

### Audit and Reconciliation responsibilities

- Validate producer identity, schema, institution and envelope integrity.
- Persist accepted evidence append-only with source identity and arrival metadata.
- Deduplicate and quarantine conflicts without overwriting originals.
- Link evidence through typed references and expose non-authoritative trace/search views.
- Detect missing/gapped/conflicting evidence and alert owning services.
- Produce controlled evidence packages and integrity verification.
- Record backup/restore plans, results, reconciliations and sign-off evidence.

### Excluded

- Editing, correcting, approving, reversing or replaying source business commands from the audit service.
- Direct Ledger writes, balance changes, payment status changes or reconciliation adjustments.
- Full Phase 2 reconciliation matching, settlement certification, suspense resolution and break management, except trace/recovery foundations.
- Security event detection/SIEM ownership beyond publishing approved security evidence to the central security platform.
- Infrastructure vendor selection, backup engine implementation details, key/HSM vendor or production topology without approved ADRs.
- A globally atomic backup across all microservices.
- Automatic production restore/cutover without human authority and service-specific reconciliation.
- Long-term data warehouse/reporting analytics or unrestricted log aggregation as a substitute for audit evidence.
- Phase 2 degraded-mode operation from US-073, except safe shutdown/recovery controls needed by Phase 1 restore.

### Hard boundaries

1. Business audit evidence is not an application log, trace span or analytics event.
2. Logs/traces help diagnosis; audit events provide controlled business/security evidence.
3. A trace link describes a relationship; it cannot alter or prove source state without source evidence.
4. The central audit service being temporarily unavailable does not lose evidence when the source transaction/outbox commits successfully.
5. A critical source action that cannot commit its required local evidence/outbox fails rather than completing unaudited.
6. A backup being present does not prove restorability; only verified restore exercises do.
7. Restore completion does not prove service readiness; invariants and cross-service reconciliation must pass.

## 4. Actors, roles and permissions

| Actor | Permission | Contextual scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Internal auditor | `audit.event.view` | Institution/event class/resource/date/purpose | Step-up for restricted evidence | None |
| Internal auditor | `audit.trace.view` | Institution/customer/business/correlation scope | Step-up for sensitive trace | None |
| Audit investigator | `audit.investigation.view` | Assigned case/institution/classification | Yes | Assignment required |
| Evidence exporter | `audit.evidence.export` | Institution/event class/date/case | Yes | Approval for restricted/bulk export |
| Evidence approver | `audit.evidence.export.approve` | Exact export request/filter/digest | Yes | Cannot approve own request |
| Audit integrity operator | `audit.integrity.verify` | Institution/partition/checkpoint | Yes | None; failures immutable |
| Audit retention administrator | `audit.retention.manage` | Institution/country/record class | Yes | Maker-checker |
| Legal-hold officer | `audit.legal_hold.apply` | Institution/case/record scope | Yes | Authority/reason required |
| Legal-hold approver | `audit.legal_hold.release` | Exact hold/authority | Yes | Cannot release own hold where required |
| Operations engineer | `recovery.catalogue.view` | Institution/environment/service/resource | Step-up for sensitive topology | None |
| Recovery planner | `recovery.plan.manage` | Institution/environment/service set | Yes | Maker-checker before execution |
| Recovery operator | `recovery.exercise.execute` | Approved plan/isolated environment | Yes | Cannot approve own result |
| Recovery approver | `recovery.exercise.approve` | Exact exercise/results/evidence | Yes | Independent approval |
| Production recovery commander | `recovery.production.execute` | Incident/approved plan/exact resources | Strong step-up | Multi-party/incident authority |
| Source service identity | `audit.evidence.publish` | Exact producer/audience/institution/event types | Service authentication | No human role |
| Backup automation identity | `recovery.catalogue.report` | Exact environment/resource/backup job | Workload identity | No human role |
| Verification worker identity | `audit.integrity.run` | Assigned partitions/checkpoints | Workload identity | No human role |

Identity and Access owns grants/entitlement versions. Audit and Reconciliation enforces tenant, classification, purpose, case assignment, date range, branch/customer/resource and evidence-export scope. Infrastructure restore permissions remain separate from application permissions and use approved privileged-access controls.

No permission allows mutation of accepted events or source business records. Break-glass access is time-bound, reasoned, independently reviewed and produces audit evidence outside the affected primary path where necessary.

## 5. Audit evidence model

### Standard audit envelope

Every accepted source audit contract contains:

- `AuditEventId`, event name and schema version.
- Producer service, bounded context, source environment and source record/evidence reference.
- Trusted `InstitutionId`; legal entity/branch/channel where applicable.
- Actor type, protected actor reference, role/grant/entitlement version and service identity/audience as applicable.
- Permission/capability checked plus relevant contextual scope summary.
- Action code, resource type and protected resource reference.
- Outcome: allowed/succeeded/rejected/failed/pending, without misrepresenting source finality.
- Reason code and approved safe reason/evidence references.
- Safe field-level before/after change set or digests/references when values are protected.
- Occurred-at time from source, received-at time from audit, business date when relevant and approved time-quality metadata.
- Correlation ID, causation ID, request/command ID and aggregate/business references.
- Data classification, retention class, source integrity/version metadata and sequence/order scope where required.

Whole entities, request bodies, provider payloads, credentials, PINs, OTPs, access tokens, private keys, complete identity numbers and unrestricted free text are prohibited. Before/after evidence uses an allow-listed schema per event type.

### Audit event categories

- Privileged/security action.
- Authentication/step-up and entitlement decision evidence.
- Approval/rejection/override and segregation-of-duty decision.
- Financial command/posting/reversal/manual adjustment evidence.
- Configuration/version/publication change.
- Sensitive-data/document/conversation access.
- Provider/integration administration and manual repair.
- Retention/legal-hold/disposition action.
- Backup/restore/recovery action.
- Audit/evidence search, view, export and integrity action.

### Source commit policy

The business aggregate state, source-local immutable audit record/reference and outbox message commit in the same local transaction where technically possible. If evidence is mandatory and that local commit fails, the protected action fails. Delivery to central Audit may be asynchronous; the outbox retries without repeating the business action.

Source events are facts. Central ingestion cannot create missing source evidence retroactively without a distinct `AuditEvidenceCorrectionRecorded` event linked to the original and authorised reason. It never edits the original.

## 6. Domain model

### Aggregates and entities

| Concept | Responsibility | Identity | Important relationships |
| --- | --- | --- | --- |
| AuditEvent | Immutable accepted source evidence | `AuditEventId` | Producer/source ref and integrity record |
| AuditIngestion | Receipt, validation, deduplication and conflict state | `AuditIngestionId` | Producer event and inbox identity |
| SensitiveAccessEvent | Enhanced evidence for protected data access | `SensitiveAccessEventId` | Actor/resource/purpose/case |
| ConfigurationChangeEvent | Versioned configuration change evidence | `ConfigurationChangeEventId` | Subject/version/approval/effective date |
| TraceNode | Protected reference to a source journey fact | `TraceNodeId` | Event/resource type, service and source ref |
| TraceLink | Typed directed relationship between nodes | `TraceLinkId` | Source/target node and relationship type |
| TraceCompletenessAssessment | Journey-policy result and missing/conflicting links | `AssessmentId` | Root trace and policy version |
| IntegrityCheckpoint | Immutable range/partition integrity evidence | `IntegrityCheckpointId` | Event sequence/range/digests/key version |
| EvidenceExportRequest | Authorised bounded export lifecycle | `EvidenceExportRequestId` | Filter digest, approval and package |
| EvidencePackage | Immutable export artifact manifest/ref | `EvidencePackageId` | Event IDs/checkpoints/document artifact ref |
| BackupRecord | Reported backup inventory and verification metadata | `BackupRecordId` | Resource/environment/recovery point/job |
| RecoveryPlan | Versioned ordered recovery and verification policy | `RecoveryPlanId` | Institution/environment/services/resources |
| RestoreExercise | One isolated or production recovery execution | `RestoreExerciseId` | Plan version, incident/exercise and results |
| RecoveryStep | Durable step/checkpoint/owner/result evidence | `RecoveryStepId` | Exercise, service/resource and dependencies |
| RecoveryVerification | Exact invariant/query/reconciliation result | `RecoveryVerificationId` | Exercise/service/check/version/evidence refs |
| LegalHold | Prevents audit/evidence disposition | `LegalHoldId` | Authority/case/record scope/status |

### Value objects

- `InstitutionId`, `ProducerService`, `BoundedContext`, `EnvironmentCode` and `SchemaVersion`.
- `ActorReference`, `ActorType`, `ServiceIdentityReference`, `PermissionCode`, `EntitlementVersion` and `AccessPurpose`.
- `ActionCode`, `ResourceType`, `ProtectedResourceReference`, `OutcomeCode` and `ReasonCode`.
- `CorrelationId`, `CausationId`, `RequestId`, `CommandId`, `AggregateReference` and `BusinessReference`.
- `EventClassification`, `DataClassification`, `RetentionClass`, `EvidenceReference` and `SafeChangeSet`.
- `TraceNodeType`, `TraceRelationshipType`, `TraceRootReference` and `CompletenessPolicyVersion`.
- `IntegrityDigest`, `SignatureReference`, `IntegrityKeyVersion`, `SequenceRange` and `PartitionKey`.
- `BackupResourceType`, `RecoveryPoint`, `BackupSetReference`, `EncryptionKeyReference` and `VerificationStatus`.
- `RecoveryTarget`, `RecoveryGate`, `CheckpointStatus`, `MeasuredRto`, `MeasuredRpo` and `TrafficFenceState`.
- `OccurredAt`, `ReceivedAt`, `DateTimeOffset` and `DateOnly` where business date applies.

### Invariants

1. Every tenant-owned audit, trace, evidence and recovery record carries a trusted institution ID.
2. Accepted audit events are append-only; no runtime/admin operation updates or deletes them.
3. Event identity is unique by producer + event ID; identical replay returns the original acceptance, while changed content under the same identity is quarantined as conflict.
4. Source occurred time and audit received time remain distinct.
5. Actor/resource/permission/action/classification schemas are allow-listed per event type/version.
6. Sensitive values are excluded, masked, tokenised or replaced with protected references/digests according to policy.
7. Audit corrections are new linked events and preserve the original evidence.
8. Trace links reference accepted source evidence or explicitly identified external evidence; they cannot invent source status.
9. Trace nodes/links are tenant-scoped and typed. Cross-tenant links are forbidden.
10. Trace completeness is evaluated against a versioned journey policy and may be `Complete`, `Incomplete`, `Conflicting`, `Pending` or `NotApplicable`.
11. Audit/trace search and export never mutates source records or audit evidence.
12. Integrity checkpoints bind an immutable event range/partition and key/policy version; failed verification remains visible.
13. Evidence package manifests bind exact filter, event IDs/ranges, schema versions, checkpoint refs, artifact digest and requester/approver.
14. Ordinary administrators cannot alter retention, legal hold, export approval or recovery evidence outside explicit permitted workflows.
15. Active legal hold blocks disposition of matching audit/evidence records.
16. Backup records cannot claim Verified solely because a backup job reported success; verification method/result is explicit.
17. Recovery plans are immutable by version once approved/executed.
18. A recovery step cannot complete until required checks/evidence are durably recorded.
19. Production traffic/outbound workers cannot resume merely because data restored; all required gates and approvals must pass.
20. Replay/rebuild uses original identities/checkpoints and cannot manufacture a new financial command.

Database grants, append-only tables, unique constraints, checks and concurrency tokens enforce these invariants. Infrastructure controls and periodic independent integrity verification provide defence in depth.

## 7. State transitions

Audit ingestion:

```text
Received -> Validating -> Accepted
    |           |          |
    +------> Rejected      +-> IntegrityCheckpointed
                |
                +------> QuarantinedConflict
```

Trace completeness:

```text
Pending -> Complete
       -> Incomplete -> Pending | Complete
       -> Conflicting -> Pending | Complete
       -> NotApplicable
```

Evidence export:

```text
Draft -> Submitted -> Approved -> Generating -> Ready -> Expired
   |         |           |            |
   +------> Cancelled     +--------> Failed
             +-------> Rejected
```

Restore exercise:

```text
Draft -> Submitted -> Approved -> Preparing -> Restoring -> Verifying -> Reconciled
   |         |           |            |            |             |
   +------> Cancelled     +--------> Failed <-------+-------------+
Reconciled -> AwaitingSignoff -> Passed | Failed
```

Production recovery adds explicit incident authority and traffic/worker fencing gates. No generic API skips directly to Passed or resumes traffic.

## 8. Financial trace model

### Trace nodes

Approved node types include:

- Channel/API request and authenticated actor/session evidence.
- Step-up/permission/risk decision.
- Workflow request/decision/override.
- Customer/account/product reference involved in the journey.
- Payment instruction, occurrence, provider attempt/status enquiry and provider reference.
- Ledger transaction, journal, journal lines/summary and reversal/correction links.
- Notification request/delivery status.
- Document/receipt/statement artifact reference.
- Reconciliation/settlement record or break when those modules are enabled.
- Manual repair/operational action.

### Trace relationships

Examples: `Caused`, `Authorised`, `SubmittedTo`, `AttemptedBy`, `VerifiedBy`, `PostedAs`, `ReversedBy`, `NotifiedBy`, `RenderedAs`, `ReconciledBy`, `CorrectedBy`, `RetriedAs` and `SupersededBy`.

Links carry source service/event, schema version, occurred/received times and confidence/source category. A correlation ID helps discovery but is not sufficient proof that two records belong together. Typed stable business/provider/Ledger references and source evidence establish the link.

### Completeness policies

Each approved journey has a versioned expected-stage policy. An internal transfer may require channel/API request, permission/risk evidence, payment instruction, Ledger posting and notification; an NIP transfer also requires provider attempt/status and reconciliation references. Optional/not-applicable stages are explicit.

The trace service reports missing or conflicting evidence. It never marks a journey financially successful from notification, provider callback, workflow approval or trace topology alone.

## 9. Recovery model and gates

### Backup catalogue

For each protected resource, record service owner, environment, resource type/ref, backup set/job ref, recovery point, start/end times, encryption/key reference metadata, retention class, location/residency class, size/count metadata, verification method/result and immutable infrastructure evidence ref. No database passwords, secret values or encryption keys are stored.

Resources include service PostgreSQL databases, document/evidence object storage, required broker/checkpoint state, approved configuration artifacts and other state explicitly named by the recovery plan. Redis/search/read models are rebuilt when non-authoritative unless a plan requires backup for recovery time.

### Recovery sequence

1. Declare exercise/incident scope and exact approved recovery-plan version.
2. Fence external ingress and outbound financial/provider/notification workers as required.
3. Capture incident/current checkpoints and protect evidence.
4. Select verified backup/recovery point under approved authority.
5. Restore into an isolated validation environment for exercises; production restoration requires incident authority.
6. Verify backup/object/database integrity and migration/schema compatibility.
7. Run service-specific invariants and reference/sequence/idempotency checks.
8. Reconcile cross-service recovery points, events/outboxes/inboxes and external-provider ambiguity.
9. Rebuild non-authoritative projections from controlled sources.
10. Determine broker replay ranges/checkpoints; replay idempotently without recreating commands.
11. Resolve or explicitly quarantine gaps/conflicts/uncertain operations.
12. Obtain service owner, Finance/Security/Operations approvals required by plan.
13. Resume read traffic, then controlled workers/write traffic in approved order.
14. Monitor, reconcile and retain exercise/incident evidence and measured RTO/RPO.

### Mandatory post-restore checks

- Ledger journals remain balanced, immutable and linked; balances rebuild/reconcile; sequences and periods/checkpoints are consistent.
- Payment/provider/limit/hold states do not permit a second debit/submission after replay.
- Identity revocations, sessions, devices and entitlement versions do not regress.
- Configuration, workflow, product, template and connector effective versions are consistent.
- Customer/KYC/documents remain decryptable under valid key references and classifications/holds persist.
- Object-storage inventory, versions/digests and relational metadata reconcile.
- Audit event identities, integrity checkpoints and outbox/inbox cursors remain continuous or gaps are explicitly quarantined.
- External uncertain operations are requeried/reconciled before any retry/failover.

## 10. Use cases

### UC-01 - Record immutable audit evidence

- Actor: Source service identity.
- Preconditions: Registered producer/schema/audience, trusted institution and source-local commit/outbox evidence.
- Permission: `audit.evidence.publish` constrained to producer/event types.
- Main flow: Authenticate producer; validate envelope/schema/classification; compute safe content identity; deduplicate; append event/ingestion result; update indexes/trace candidates; checkpoint asynchronously.
- Failure outcomes: Unknown schema/producer, missing required evidence, cross-tenant mismatch, prohibited field or event identity conflict.
- Idempotency: Producer + audit event ID with content digest conflict detection.
- Audit: Ingestion itself has protected operational evidence without recursive infinite emission.

### UC-02 - Search and view audit events

- Actor: Internal auditor/investigator.
- Preconditions: Current permission, tenant/classification/purpose/date/resource scope and step-up where required.
- Main flow: Apply bounded indexed filters; return masked summaries; reauthorise detail access; record sensitive search/detail access.
- Failure outcomes: Broad/unbounded query, cross-tenant ref, missing purpose, stale entitlement or restricted classification.
- Rule: Search indexes are projections; event detail comes from append-only authority and integrity status is shown.

### UC-03 - Trace a transaction end to end

- Actor: Operations engineer/auditor.
- Preconditions: Scoped query by protected customer/business/provider/correlation ref.
- Main flow: Resolve candidate roots; traverse typed links within institution/policy bounds; order by occurred/received time; display source-service statuses and completeness assessment; mask sensitive fields.
- Failure outcomes: Ambiguous root, incomplete/conflicting link, unauthorised node or stale projection.
- Rule: UI/API never collapses provider, payment, workflow and Ledger states into one invented status.

### UC-04 - Export an evidence package

- Actor: Evidence exporter and approver.
- Preconditions: Bounded filter/case/purpose, retention/classification policy and approved output destination.
- Main flow: Freeze filter digest/event range; submit approval; collect exact events/links/checkpoints; render manifest/package through Documents; integrity-protect; issue secure access; record every access/expiry.
- Failure outcomes: Self-approval, changed filter, integrity failure, excessive/unapproved scope or missing source evidence.
- Idempotency: Export request/version/filter digest.
- Rule: Export never modifies or marks source events resolved.

### UC-05 - Verify audit integrity

- Actor: Automated verifier/integrity operator.
- Preconditions: Approved partition/range/checkpoint/key policy.
- Main flow: Recalculate approved digests/signatures/checkpoint relationships; compare storage counts/sequences; append result; restrict/alert on mismatch.
- Failure outcomes: Missing event/range, key unavailable, digest/signature mismatch or unexpected mutation.
- Rule: A failed check is never overwritten by a later pass.

### UC-06 - Register and verify a backup record

- Actor: Backup automation identity/operations engineer.
- Preconditions: Registered environment/resource/job and approved backup policy.
- Main flow: Ingest signed/verified job metadata; record recovery point/encryption/retention/evidence refs; run non-destructive verification; mark explicit verification result.
- Failure outcomes: Unknown resource/job, stale evidence, unencrypted backup, residency/retention mismatch or verification failure.
- Rule: Job success and restore success are separate facts.

### UC-07 - Execute an isolated restore exercise

- Actors: Recovery operator, service owners and independent approver.
- Preconditions: Approved plan/version, verified backup set, isolated target, synthetic/masked access controls and scheduled authority.
- Main flow: Fence target; execute durable steps; restore; run automated/service-specific checks; reconcile; measure recovery; attach evidence; sign off exact results.
- Failure outcomes: Backup corrupt/missing, target isolation breach, key unavailable, invariant/reference mismatch, exceeded approved target or incomplete evidence.
- Idempotency: Plan version + exercise ID + step ID/attempt.
- Rule: Exercise never restores over production or contacts live providers/customers.

### UC-08 - Coordinate production recovery evidence

- Actor: Production recovery commander under incident authority.
- Preconditions: Declared incident, approved plan/scope, privileged approval and traffic fencing.
- Main flow: Record exact commands/automation refs and checkpoints; require service verification; reconcile ambiguous financial/provider state; collect sign-offs; record phased reopen and monitoring.
- Failure outcomes: Missing authority, failed invariant, unresolved duplicate risk, stale identity/configuration or incomplete reconciliation.
- Rule: This module records/orchestrates evidence; infrastructure automation performs actual restore under separate credentials.

### UC-09 - Apply legal hold to audit/evidence

- Actor: Legal-hold officer/approver.
- Preconditions: Authority, case, institution, bounded event/resource/date scope.
- Main flow: Resolve/freeze target criteria and checkpoint; activate hold; block disposition; record release through separate approved action.
- Failure outcomes: Ambiguous/unbounded scope, cross-tenant target, invalid authority or self-release conflict.
- Idempotency: Authority/case + scope digest + occurrence.

## 11. API contracts

### Audit and trace APIs

| Method | Route | Permission | Request | Response | Idempotent |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/v1/audit/events` | `audit.event.view` | Bounded filters/cursor/purpose | Masked event page | Query |
| GET | `/api/v1/audit/events/{eventId}` | `audit.event.view` | Purpose/context | Authorised event detail/integrity | Query |
| GET | `/api/v1/audit/traces` | `audit.trace.view` | One supported root reference | Candidate trace summaries | Query |
| GET | `/api/v1/audit/traces/{traceId}` | `audit.trace.view` | Depth/stage filters | Typed timeline/completeness | Query |
| POST | `/api/v1/audit/evidence-exports` | `audit.evidence.export` | Bounded filter/purpose/format/destination ref | Export request | Yes |
| POST | `/api/v1/audit/evidence-exports/{id}/submit` | `audit.evidence.export` | Exact version/filter digest | Workflow ref | Yes |
| GET | `/api/v1/audit/evidence-packages/{id}` | Scoped evidence view | Purpose | Redacted manifest/status/access ref | Query |
| POST | `/api/v1/audit/integrity-checks` | `audit.integrity.verify` | Partition/range/reason | Verification ref | Yes |
| POST | `/api/v1/audit/legal-holds` | `audit.legal_hold.apply` | Authority/case/scope/reason | Hold ref | Yes |

### Recovery APIs

| Method | Route | Permission | Request | Response | Idempotent |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/v1/recovery/backups` | `recovery.catalogue.view` | Environment/service/resource/date filters | Masked backup catalogue | Query |
| POST | `/api/v1/recovery/plans` | `recovery.plan.manage` | Scope/dependencies/gates/targets refs | Draft plan version | Yes |
| POST | `/api/v1/recovery/plans/{id}/submit` | `recovery.plan.manage` | Exact version/digest | Workflow ref | Yes |
| POST | `/api/v1/recovery/exercises` | `recovery.exercise.execute` | Approved plan/backup refs/isolated target | Exercise ref | Yes |
| POST | `/api/v1/recovery/exercises/{id}/steps/{stepId}/results` | Recovery operator/service identity | Attempt/result/evidence refs | Step state | Yes |
| POST | `/api/v1/recovery/exercises/{id}/verifications` | Scoped verifier | Check/version/result/evidence | Verification ref | Yes |
| POST | `/api/v1/recovery/exercises/{id}/submit-result` | `recovery.exercise.execute` | Result digest/measurements | Approval ref | Yes |
| GET | `/api/v1/recovery/exercises/{id}` | Recovery evidence view | None | Plan/steps/checks/sign-off summary | Query |

### Internal ingestion APIs

| Method | Route | Service permission | Request | Response | Idempotent |
| --- | --- | --- | --- | --- | --- |
| POST | `/internal/v1/audit-events` | `audit.evidence.publish` | Versioned audit envelope | Accepted/duplicate/quarantined result | Yes |
| POST | `/internal/v1/backup-records` | `recovery.catalogue.report` | Signed job/resource metadata | Catalogue result | Yes |
| POST | `/internal/v1/trace-evidence` | Registered source service | Typed source refs/relationships | Accepted/duplicate result | Yes |

The broker/outbox path is preferred for ordinary audit evidence; internal HTTP exists only for approved synchronous/system cases and must preserve source durability/idempotency. Audit search never exposes raw table query, unrestricted free-text payload search or cross-tenant wildcard scope.

Stable errors include `audit.schema_unsupported`, `audit.producer_denied`, `audit.event_identity_conflict`, `audit.prohibited_field`, `audit.scope_too_broad`, `audit.classification_denied`, `audit.integrity_failed`, `trace.root_ambiguous`, `recovery.plan_not_approved`, `recovery.gate_failed` and `recovery.production_authority_required`.

## 12. Event contracts

### Consumed events

| Event | Producer | Consumer behaviour | Deduplication key | Failure handling |
| --- | --- | --- | --- | --- |
| Versioned `AuditEvidenceRecorded` envelopes | Every service | Validate, append and index evidence | Producer + audit event ID | Reject/quarantine/DLQ; source outbox remains visible |
| Approved domain/financial facts | Owning services | Create typed trace nodes/links only | Producer event ID + trace mapping version | Inbox; unknown mapping quarantined |
| `WorkflowDecisionRecorded` | Workflow | Link approval/override to exact source action | Event ID + decision ID | Inbox; mismatch/conflict alert |
| Provider/payment/Ledger facts | Integration/Payments/Ledger | Link attempts/status/journals/reversals | Event ID + business ref | Inbox; preserve distinct statuses |
| Notification/document facts | Channels/Documents | Link delivery/artifact evidence | Event ID + source ref | Inbox |
| Backup job evidence | Platform automation | Register backup/verification metadata | Job/resource/backup ID | Conflict quarantine |

### Produced events

| Event | Trigger | Key fields | Ordering key | Consumers |
| --- | --- | --- | --- | --- |
| `AuditIngestionGapDetected` | Expected sequence/evidence gap | Institution/producer/scope/range/safe reason | Producer/scope | Source owner, Operations |
| `AuditIntegrityFailureDetected` | Checkpoint verification fails | Partition/range/checkpoint/evidence ref | Partition | Security, Operations |
| `FinancialTraceCompletenessChanged` | Assessment materially changes | Trace root/policy/old-new/missing stages | Trace root | Operations, Audit |
| `EvidencePackageReady` | Approved package verified | Export/package/artifact/checkpoint refs/expiry | Export request | Requester, Audit |
| `BackupVerificationFailed` | Backup verification fails | Resource/backup/job/recovery point/safe reason | Resource | Platform Operations |
| `RestoreExerciseCompleted` | Exercise/sign-off final | Exercise/plan/result/measured target/evidence refs | Exercise | Operations, Compliance |
| `RecoveryGateFailed` | Required post-restore check fails | Exercise/service/gate/check ref | Exercise/service | Recovery command, owner |

Audit events do not recursively emit an unbounded audit chain. Administrative actions within Audit use a dedicated append-only self-audit path plus infrastructure access evidence, with stable event identity and independent integrity checking.

Events exclude secrets, database connection details, backup encryption keys, unrestricted payloads and unnecessary PII. Schema/version changes require compatibility and producer rollout plans.

## 13. Database design

### Ownership

- Database: `audit_reconciliation_db`.
- Phase 1 schemas: `audit`, `evidence`, `integration`; `reconciliation` and `settlement` tables remain Phase 2 foundations unless needed for trace references.
- Migration assembly: `CoreBanking.AuditReconciliation.Infrastructure`.
- Runtime database principal: insert/select through restricted paths; no ordinary update/delete on accepted evidence tables.
- Migration/retention principals: separately controlled, approved and audited.

### Tables

| Table | Purpose | Key constraints | Tenant-owned | Retention |
| --- | --- | --- | --- | --- |
| `audit.audit_events` | Immutable accepted enterprise audit envelope | Unique producer + event ID; append-only | Yes | Audit/legal policy |
| `audit.audit_ingestions` | Receipt/validation/dedup/conflict evidence | Unique producer event receipt/content identity | Yes | Operational/evidence policy |
| `audit.audit_event_integrity` | Per-event/range integrity metadata | Unique event/checkpoint relation | Yes | Audit policy |
| `audit.sensitive_access_events` | Enhanced protected-data access evidence | Unique source access event | Yes | Privacy/audit policy |
| `audit.configuration_change_events` | Configuration/version/effective-date evidence | Unique source change event/version | Yes | Audit policy |
| `audit.trace_nodes` | Protected source fact references | Unique institution + source service/type/ref/version | Yes | Trace policy |
| `audit.financial_trace_links` | Typed immutable relationships | Unique source + target + type + evidence event | Yes | Financial evidence policy |
| `audit.trace_assessments` | Completeness/conflict policy results | Unique root + policy version + assessment occurrence | Yes | Trace policy |
| `audit.integrity_checkpoints` | Immutable partition/range checkpoint | Unique partition + sequence range + policy version | Yes | Permanent/approved policy |
| `evidence.export_requests` | Bounded export/approval lifecycle | Unique requester + command key/digest | Yes | Evidence policy |
| `evidence.evidence_packages` | Package manifest/artifact/integrity refs | Unique export version/package digest | Yes | Case/legal policy |
| `evidence.legal_holds` | Audit/evidence hold/release lineage | Unique authority/case + scope + occurrence | Yes | Legal policy |
| `evidence.backup_catalogue` | Backup resource/job/recovery-point metadata | Unique environment + resource + backup set | Yes | Recovery policy |
| `evidence.backup_verifications` | Non-destructive/restore verification evidence | Unique backup + check/version/occurrence | Yes | Recovery policy |
| `evidence.recovery_plans` | Immutable versioned ordered plan/gates | Unique institution + environment + plan code/version | Yes | Permanent evidence |
| `evidence.restore_exercises` | Exercise/incident lifecycle/measurements | Unique exercise ref | Yes | Recovery evidence policy |
| `evidence.recovery_steps` | Durable ordered/dependent step outcomes | Unique exercise + step + attempt | Yes | Recovery evidence policy |
| `evidence.recovery_verifications` | Service/invariant/reconciliation results | Unique exercise + check/version/target | Yes | Recovery evidence policy |
| `integration.outbox_messages` | Durable produced events | Unique event ID | Yes | Operational policy |
| `integration.inbox_messages` | Consumer deduplication | Unique producer + event ID + consumer | Yes | Replay-window policy |

### Constraints and access controls

- Tenant-owned indexes begin with `institution_id`; search references are protected tokens/opaque refs where feasible.
- `audit_events`, accepted integrity rows, trace links, completed exercise steps/verifications and evidence-package manifests are append-only.
- Runtime roles cannot update/delete immutable rows. Corrections append linked events/results.
- Event uniqueness and content digest detect same-ID changed-content conflicts.
- Sequence/order is scoped to the smallest required producer/aggregate/partition; no global broker order is assumed.
- Trace links enforce same institution and permitted node/link types.
- Export filters/date ranges/result sizes are bounded and hashed; package manifests bind exact event IDs/ranges.
- Active legal holds block approved retention/disposition jobs.
- Recovery steps use dependency and state constraints; required gates cannot be skipped.
- Recovery measurement fields include units/source timestamps and approved target version; missing target is `NotDefined`, not zero/passed.

### Tamper evidence

- Use approved framework cryptography and managed keys; do not invent algorithms.
- Bind canonical event bytes/fields, sequence scope, schema version and prior/checkpoint references under the approved design.
- Store key/version/signature/digest references, not private key material.
- Periodically verify ranges and export manifests; preserve every failed check.
- Protect database backups and infrastructure access logs independently so database administrators cannot silently rewrite all evidence layers.

### Migration and backfill

- Create append-only schemas/grants/constraints before enabling producer ingestion.
- Register producers/event schemas/classifications through approved configuration; do not accept unknown dynamic JSON as audit authority.
- Existing evidence import preserves original source/timestamp and records import provenance, validation and any unverifiable status.
- Backfills are append-only, bounded and idempotent; they cannot invent actor/reason/before-after data.
- Released migrations are append-only. Partition/key/integrity changes require dual verification, migration manifests and forward-fix plans.
- Verification checks immutable-table grants, counts/sequence gaps, duplicate IDs, trace tenant consistency, checkpoint integrity and legal-hold coverage.

## 14. Consistency, concurrency and idempotency

- Source state/local evidence/outbox commit atomically inside each source service.
- Audit ingestion transaction commits event, ingestion result, initial trace node candidates and local outbox atomically.
- Consumer/inbox deduplication occurs before append; identical duplicate returns the original result.
- Same event identity with changed canonical content is never last-write-wins; both receipt attempts are evidenced and the event is quarantined.
- Trace building is asynchronous/idempotent and may be eventually complete. Completeness state exposes lag/gaps.
- Concurrent link creation uses typed uniqueness constraints; conflicting relationships remain visible for assessment.
- Search indexes/caches are non-authoritative and rebuilt from accepted events/links.
- Export freezes a filter/event-set manifest before generation so concurrent new events do not silently alter the package.
- Restore-step claims/checkpoints use optimistic concurrency/leases and stable attempt IDs.
- Replay ranges use original event/command IDs and consumer checkpoints. New idempotency keys for recovered financial commands are prohibited.
- Cross-service recovery uses reconciliation and compensating/manual decisions, not a distributed transaction.

## 15. Security and privacy

Threats include audit bypass, forged producer events, event deletion/update, same-ID substitution, tenant/reference confusion, excessive search, insider browsing, inference through before/after values, export exfiltration, log/audit secret leakage, key misuse, backup theft, malicious restore evidence and production restore abuse.

Controls:

- Authenticate producers with short-lived service identity and exact audience/event-type scopes.
- Derive institution from trusted producer/security context and validate against the envelope/source mapping.
- Allow-list schemas/fields/classifications; reject prohibited values before persistence.
- Use protected/tokenised customer/resource search refs; reveal masked metadata only after current authorisation.
- Require purpose, bounded query and step-up for sensitive searches/details/exports.
- Rate-limit and detect bulk/enumerative access; audit every sensitive search/detail/export.
- Separate runtime, audit reader, integrity verifier, retention, export and migration/database administration identities.
- Encrypt evidence/backups at rest and in transit with managed keys; record key refs/versions only.
- Require maker-checker/multi-party authority for restricted exports, hold release, retention changes and production recovery.
- Keep backup locations/topology/connection data masked from ordinary APIs and logs.
- Isolate restore exercises from production networks/providers/customers and use synthetic/masked verification data where possible.
- Publish security events to approved SIEM/incident workflow without exposing audit bodies.
- Non-production does not receive production audit/backup data unless explicitly approved and irreversibly masked.

Negative tests cover forged producer/audience, cross-tenant refs/links, schema/prohibited fields, same-ID changed body, event update/delete, stale entitlement, broad query, export scope mutation, self-approval, link inference, backup metadata leakage, exercise-to-production connectivity, skipped recovery gates and unauthorised traffic resume.

## 16. Audit service self-audit and integrity

The service records its own administrative/search/export/retention/integrity/recovery actions using a dedicated append-only path that does not recursively generate infinite events. The event is committed with the administrative state change and included in independently checked integrity partitions.

Database migration/privileged storage actions additionally rely on infrastructure/cloud/database audit logs exported to an independently controlled security/evidence destination. Application audit alone cannot prove that its own database administrator did not bypass it.

Integrity alerts include missing ranges, count drift, canonical digest/signature mismatch, unexpected mutable grants, partition/checkpoint gaps, failed evidence export verification and backup catalogue inconsistencies.

## 17. Observability

Minimum metrics:

- Source audit events received/accepted/rejected/quarantined/duplicate by producer/schema/classification.
- Source outbox/audit ingestion lag and expected sequence gaps.
- Prohibited-field/schema/tenant/producer validation failures.
- Immutable-table mutation attempts and database privilege drift.
- Integrity checkpoints created/verified/failed and oldest unchecked range.
- Trace nodes/links/assessments by journey/completeness/missing stage/age.
- Audit/trace searches, denials, sensitive views and export volume/age/failures.
- Backup records by resource/age/encryption/verification/retention/compliance state.
- Restore exercises by plan/result, failed gate, measured RTO/RPO and overdue schedule.
- Recovery steps/checks age, cross-service mismatches and traffic-fence state.
- Inbox/outbox/dead-letter lag and restore/replay conflicts.

Traces cover source transaction/outbox, broker, audit ingestion, trace link, evidence export and recovery orchestration using correlation/causation IDs. They exclude event bodies, source before/after values, backup paths/credentials and protected refs not approved for telemetry.

Alerts/runbooks cover audit-source gap, ingestion backlog, conflict/quarantine, integrity failure, privilege drift, incomplete financial trace, sensitive access anomaly, export failure, stale/unverified backup, missed restore exercise, failed recovery gate and post-restore duplicate/gap risk.

## 18. Failure, retry and recovery

| Failure | Safe state | Recovery |
| --- | --- | --- |
| Source cannot commit required local evidence | Protected business action fails | Correct local persistence; retry command with same key |
| Central audit unavailable | Source state/outbox durable | Publisher retries; no business re-execution |
| Invalid/prohibited audit envelope | Rejected/quarantined; source gap visible | Fix producer/schema and publish authorised correction/new evidence |
| Same ID, changed content | Quarantined conflict | Investigate producer; never overwrite original |
| Trace mapper/consumer fails | Accepted evidence remains authoritative | Retry/rebuild links idempotently |
| Search index unavailable/stale | Search degraded/unavailable with freshness | Rebuild from audit store; no source mutation |
| Integrity check fails | Failed state/alert preserved | Freeze affected export/use per policy; investigate independent evidence |
| Evidence export generation fails | Request remains Failed/repairable | Retry exact frozen manifest or new approved request |
| Backup job reports failure | Backup Unverified/Failed | Platform remediation/new backup; do not claim coverage |
| Restore exercise step fails | Exercise Failed; target remains fenced | Repair plan/backup; resume exact step or new exercise under policy |
| Restored service invariant fails | Service/traffic remains fenced | Restore alternate point/repair through authorised process |
| Cross-service recovery points disagree | ReconciliationRequired | Controlled event replay/status enquiry/manual decision; no blind command replay |
| Audit database restore | Ingestion/search remain fenced | Verify event/checkpoint continuity, replay inbox/outbox and source gaps |

Manual audit repair appends explicit correction/link/assessment evidence; it never edits accepted events. Production recovery remains fenced until exact plan gates/approvals pass. A failed/partial recovery is retained as evidence and cannot be relabelled Passed.

## 19. Configuration

| Setting | Owner | Secret | Validation | Reload behaviour |
| --- | --- | --- | --- | --- |
| Audit event schema/field/classification | Audit/Security/Domain owner | No | Required/prohibited fields/version compatibility | Versioned deployment |
| Required audit policy by action/access | Compliance/Security/Domain owner | No | Source transaction coverage | Versioned/effective-dated |
| Trace node/link/journey policy | Audit/Domain owners | No | Types/required stages/source refs | Versioned/rebuildable |
| Search masking/tokenisation policy | Privacy/Security | Key refs external | Classification/permission/purpose | Versioned |
| Integrity/checkpoint/signing policy | Security/Audit | Keys external | Algorithm/key/range/rotation/verification | Versioned/controlled |
| Audit/evidence retention/legal hold | Legal/Compliance | No | Institution/country/class/action | Versioned/effective-dated |
| Export format/destination/access policy | Audit/Security | Destination/key refs external | Classification/approval/expiry | Versioned |
| Backup policy per resource/service | Platform/Compliance | Credentials/keys external | Frequency/encryption/retention/residency/verify | Versioned |
| Recovery plan/dependencies/gates | Service owners/Platform | No | Exact resources/order/checks/authority | Versioned/approved |
| RTO/RPO target | Institution/Product/Platform | No | Service tier/workload/measurement method | Versioned/release gate |
| Restore-exercise schedule/isolation | Platform/Security | No | Environment/resource/owner/evidence | Versioned |
| Traffic/worker fencing and replay policy | Service owner/Platform/Risk | No | Duplicate/uncertainty/dependency analysis | Versioned/approved |

Missing/stale critical audit schema, integrity, retention, backup or recovery policy fails safely. Feature flags cannot bypass required evidence, append-only controls, tenant authorisation, integrity checks or recovery gates.

## 20. Implementation slices

1. Approve enterprise audit envelope, source commit, trace identity/link, integrity, retention and recovery ADRs.
2. Create C# Audit/Reconciliation module boundaries, append-only database grants and architecture tests.
3. Implement producer/schema registry, ingestion validation, dedup/conflict quarantine, inbox and immutable persistence.
4. Add source-service audit SDK/contracts/building blocks and prove atomic local evidence/outbox in one non-financial and one financial flow.
5. Implement sensitive-access/configuration-change evidence, masking/tokenisation and permission-scoped search/detail APIs.
6. Implement integrity checkpoints, verification, independent admin evidence and failure alerts.
7. Implement typed trace nodes/links/completeness for internal transfer end to end.
8. Extend trace mapping to NIP/provider, workflow, Ledger, notification, document and reconciliation references.
9. Implement approved evidence export, frozen manifests, Documents artifact generation and secure access.
10. Implement backup catalogue/verification ingestion for every Phase 1 database/object-store resource.
11. Implement versioned recovery plans, steps, gates, exercise evidence and independent sign-off.
12. Run service-specific isolated restore exercises, beginning with non-financial services, then Ledger/Payments/Integration.
13. Implement cross-service PITR reconciliation, controlled replay, projection rebuild and traffic/worker fencing evidence.
14. Add dashboards, alerts, SIEM handoff and audit-gap/integrity/backup/restore runbooks.
15. Complete security, scale, retention/legal hold, chaos, restore and Phase 1 end-to-end evidence.

Each slice begins with failing tests and leaves unsupported producer schemas, trace mappings and recovery plans disabled. A dashboard or backup-job success report does not satisfy an exercise slice.

## 21. Test plan

| Test ID | Requirement/story | Level | Scenario | Expected evidence |
| --- | --- | --- | --- | --- |
| AUD-001 | AUD-001 / US-070 | Integration | Privileged action commits | Source state/local evidence/outbox atomic; central append |
| AUD-002 | AUD-001 | Failure | Required local audit commit fails | Protected action does not complete |
| AUD-003 | AUD-001 / NFR-RES-002 | Messaging | Replay same audit event | One accepted identity; duplicate receipt evidenced |
| AUD-004 | AUD-001 | Security | Same ID with changed body | Conflict quarantined; original unchanged |
| AUD-005 | AUD-002 / US-070 | Contract | Validate complete audit envelope | Actor/role/tenant/branch/action/time/source/reason/change/correlation |
| AUD-006 | AUD-002 / NFR-OBS-002 | Security | Secret/PIN/OTP/private key/full PII field | Rejected/redacted per schema; alert/evidence |
| AUD-007 | AUD-003 / US-070 | Database | Runtime/admin attempts update/delete | Database grant/constraint denies and records security event |
| AUD-008 | DAT-008 | Domain | Correct prior audit metadata | New linked correction; original preserved |
| AUD-009 | SEC-009 | Security | Cross-tenant audit event/detail/trace ref | Denied without existence disclosure |
| AUD-010 | SEC-010/013 | Security | Sensitive search/detail without step-up/purpose | Denied/masked and audited |
| AUD-011 | US-070 | Security | Bulk/unbounded/enumerative search | Bounded/rate-limited/denied |
| AUD-012 | US-070 | Workflow | Exporter approves own restricted export | Denied by separation |
| AUD-013 | US-070 | Integration | Generate evidence package | Frozen manifest/events/checkpoints/artifact integrity |
| AUD-014 | US-070 | Security | Change export filter after approval | Digest mismatch/reapproval required |
| AUD-015 | Integrity | Security | Modify/remove event in test partition | Integrity check fails and remains visible |
| AUD-016 | Integrity | Recovery | Rotate integrity key/checkpoint policy | Old/new ranges verify with recorded versions |
| AUD-017 | AUD-004 / US-071 | End-to-end | Trace internal transfer | Request/auth/payment/Ledger/notification links and source states |
| AUD-018 | AUD-004 / US-071 | End-to-end | Trace NIP timeout then verified status | Provider/Payment/Ledger states distinct; uncertainty visible |
| AUD-019 | AUD-004 | Domain | Correlation ID reused across unrelated records | No false link without typed source evidence |
| AUD-020 | AUD-004 | Domain | Required trace stage missing/delayed | Incomplete/Pending then converges after event |
| AUD-021 | AUD-004 | Domain | Conflicting final provider/Ledger evidence | Conflicting/investigation; no invented final status |
| AUD-022 | US-071 | Security | Search by customer/provider/bank ref | Tenant-scoped protected lookup; masked result |
| AUD-023 | US-071 | Architecture | Attempt source mutation through trace API | No write path/forbidden dependency |
| AUD-024 | NFR-OBS-001 | Contract | Propagate correlation/causation across services | Exact refs present without payload leakage |
| AUD-025 | NFR-OBS-002 | Security | Inspect logs/metrics/traces/DLQ/errors | No prohibited sensitive data |
| AUD-026 | NFR-SCAL-001 | Concurrency | Parallel ingestion/trace workers | Uniqueness/order scope preserved; no loss |
| AUD-027 | SEC-017 | Integration | Integrity/security event | Approved SIEM/incident event without audit body |
| AUD-028 | SEC-018 | Security | Production audit/backup data requested in test | Blocked unless approved irreversibly masked path |
| AUD-029 | NFR-DR-001 / US-072 | Integration | Register encrypted backup | Exact resource/recovery point/encryption/retention evidence |
| AUD-030 | NFR-DR-001 | Domain | Backup job success without verification | Status not marked restore-verified |
| AUD-031 | NFR-DR-001 / US-072 | Recovery | Isolated PostgreSQL restore | Integrity/schema/counts/invariants verified |
| AUD-032 | NFR-DR-001 / US-072 | Recovery | Object storage restore | Versions/digests/metadata/legal holds reconcile |
| AUD-033 | NFR-DR-002 / US-072 | Recovery | Measure approved exercise RTO/RPO | Actual method/times compared with target version |
| AUD-034 | NFR-DR-002 | Domain | RTO/RPO target absent | Result `NotDefined`; never passed/zero |
| AUD-035 | NFR-RES-002 | Recovery | Replay broker after restore | Original IDs/checkpoints; no duplicate financial outcome |
| AUD-036 | Recovery | Security | Resume traffic before required gates | Denied/fenced |
| AUD-037 | Recovery | Integration | Restore Identity before revocation reconciliation | Authentication/write traffic remains fenced |
| AUD-038 | Recovery / BR invariants | Integration | Restore Ledger and rebuild balances | Journals balanced/immutable; balances/sequences reconcile |
| AUD-039 | Recovery / BR-006 | Integration | Restored uncertain provider payment | Enquiry/reconciliation before retry/failover |
| AUD-040 | Recovery | Integration | Payments and Ledger recover to different points | ReconciliationRequired; no blind replay |
| AUD-041 | Recovery | Failure | Backup corrupt/key unavailable | Exercise fails with target fenced/evidence retained |
| AUD-042 | Recovery | Security | Restore exercise reaches live provider/customer | Network/policy isolation blocks it |
| AUD-043 | Legal hold | Concurrency | Retention job races active hold | Hold wins; evidence retained |
| AUD-044 | Audit self-evidence | Security | Auditor views/exports restricted records | Separate self-audit event/infrastructure evidence |
| AUD-045 | Messaging | Recovery | Central audit unavailable during source actions | Source outboxes retain evidence; catch-up preserves identities |
| AUD-046 | End-to-end | End-to-end | Restore all Phase 1 critical services | Gates/invariants/refs/replay pass before controlled reopen |
| AUD-047 | US-070..US-072 | Traceability | Inspect evidence map | Requirements/stories -> contracts/code/tests/exercises |

Fault injection stops processes before/after source commit, outbox publication, audit acceptance, trace link, checkpoint, backup report, restore step, event replay and traffic reopen. Recovery tests use isolated environments and synthetic data.

## 22. Acceptance criteria

- [ ] AUD-001 through AUD-004, NFR-DR-001/002, NFR-OBS-001/002 and US-070 through US-072 trace to decisions, contracts, code, tests and exercises.
- [ ] Every Phase 1 privileged action, approval, override, configuration publication, sensitive access and manual financial operation has source-owned atomic evidence/outbox coverage.
- [ ] Central audit storage and integrity/checkpoint evidence are append-only and protected from ordinary administrator modification/deletion.
- [ ] Required actor/role/service/institution/branch/action/time/source/reason/change/correlation fields are present and prohibited data is absent.
- [ ] Audit corrections append linked evidence and never modify originals.
- [ ] Internal and NIP/payment traces preserve distinct workflow, provider, payment, Ledger, notification and reconciliation states.
- [ ] Missing/conflicting trace stages remain visible and trace data cannot mutate financial/source records.
- [ ] Search/detail/export enforce tenant, classification, purpose, scope, step-up, masking and access audit.
- [ ] Evidence exports bind exact filters/events/checkpoints and are integrity-protected and securely delivered.
- [ ] Backup catalogue covers every enabled Phase 1 authoritative database/object store and records encryption/verification/retention/recovery point.
- [ ] Isolated restore exercises measure approved targets and retain plan/step/check/sign-off evidence.
- [ ] Post-restore gates prove financial, identity, configuration, idempotency, object and audit continuity before traffic/workers resume.
- [ ] Replay/restart/restore cannot duplicate financial/provider/notification/document/audit outcomes.
- [ ] Phase 2 reconciliation/settlement/degraded-mode scope remains disabled.

## 23. Definition of Done

- Build, analyzers, formatting, architecture tests and affected suites pass with the repository-pinned .NET SDK.
- EF Core migrations apply to empty/supported upgrade databases; append-only grants, event identity, tenant link, checkpoint, export, hold and recovery-step constraints pass PostgreSQL/Testcontainers tests.
- Producer/schema contracts pass compatibility, prohibited-field and source-commit coverage checks for every Phase 1 service.
- Database/runtime/migration/reader/export/integrity/recovery privileges pass least-privilege review.
- Internal-transfer and NIP trace journeys pass with intentionally missing/conflicting/replayed evidence cases.
- Integrity verification detects controlled tampering and key/policy rotation cases.
- Every authoritative Phase 1 database/object store has a current approved backup policy/catalogue/verification and restore runbook.
- At least one full isolated Phase 1 recovery exercise demonstrates service invariants, cross-service reconciliation, controlled replay and phased reopen; production scope follows approved plan.
- Actual measured RTO/RPO evidence is compared with approved targets or explicitly records targets as unresolved.
- Audit-gap, integrity-failure, sensitive-access, backup-age, failed-gate and restore-drift dashboards/alerts/runbooks are demonstrated.
- Traceability lists requirements/stories, producer schemas, permissions, APIs/events, migrations, integrity/key policy refs, tests, backup sets, exercise IDs, results, risks and approvals.

## 24. Open decisions

| Decision | Impact | Owner | Required by | Status |
| --- | --- | --- | --- | --- |
| Enterprise audit envelope/event taxonomy | Producer consistency | Audit/Architecture/Domain owners | Slice 1 | Open |
| Source action/access audit coverage matrix | Evidence completeness | Compliance/Security/Domain owners | Slice 1/4 | Open |
| Append-only database/storage/grant model | Immutability threat model | Security/DBA/Audit | Slice 2 | Open |
| Canonicalisation/digest/signature/checkpoint/key model | Tamper evidence | Security/Audit | Slice 1/6 | Open |
| Protected reference/token/search strategy | Privacy/searchability | Security/Privacy/Audit | Slice 5 | Open |
| Audit retention, archive, legal hold and disposition | Compliance/storage | Legal/Compliance | Before production |
| Trace node/link types and journey completeness policies | US-071 accuracy | Domain owners/Audit | Slice 7/8 | Open |
| Evidence export formats/destinations/watermark/signing | Legal/audit usability | Audit/Legal/Security | Slice 9 | Open |
| Audit self-audit and independent infrastructure evidence | Insider threat | Security/Platform/Audit | Slice 6 | Open |
| Backup platform/resource coverage/verification method | US-072 coverage | Platform/Service owners | Slice 10 | Open |
| Per-institution/service RTO and RPO targets | Release/recovery gates | Institution/Product/Platform | Slice 11 | Open |
| Recovery service dependency/order/fencing matrix | Safe restore | Platform/Service owners | Slice 11 | Open |
| Broker retention/checkpoint/replay strategy | Duplicate/gap risk | Platform/Domain owners | Slice 13 | Open |
| Cross-service recovery-point reconciliation rules | Financial consistency | Finance/Domain owners | Slice 13 | Open |
| Production recovery authority/sign-off matrix | Incident control | Executive/Operations/Security | Before production |
| Restore exercise frequency, isolation and evidence retention | Ongoing assurance | Compliance/Platform | Before release |
| Audit/trace ingestion/search capacity and performance | Scale/storage | Product/Platform | Before load testing | Open |

## 25. Prohibited assumptions

- Do not treat logs, traces, metrics, broker retention, analytics or SIEM records as substitutes for source-owned audit evidence.
- Do not emit audit evidence only after an action succeeds if failure to emit would make the action unaudited.
- Do not update/delete an accepted audit event to correct it; append linked correction evidence.
- Do not dump entire request/entity/provider payloads or secrets/PII into before/after fields.
- Do not trust client-supplied actor, role, institution, branch, permission, reason, approval or outcome.
- Do not link records solely because they share a correlation ID or nearby timestamp.
- Do not collapse provider, payment, workflow, Ledger, notification and reconciliation states into one trace status.
- Do not let trace/audit APIs post, approve, reverse, repair or mutate source records.
- Do not consider a backup job success message proof that the backup can be restored.
- Do not assume backups across services share an atomic recovery point.
- Do not restore over production during an exercise or permit exercise traffic to live providers/customers.
- Do not resume traffic/workers after restore before exact service/cross-service gates and approvals pass.
- Do not blindly replay commands, create new idempotency keys or retry uncertain provider operations after restore.
- Do not invent RTO/RPO, backup schedules, retention, crypto or regulatory-evidence rules.
- Do not enable Phase 2 reconciliation, settlement certification or degraded-mode business operation as incidental scope.

Until policies and targets are approved, use synthetic producers/backups, isolated exercises and explicit `NotDefined`/disabled states.

## 26. Agent implementation instructions

- Required reading: root `AGENTS.md`, architecture, Phase 0 evidence, Epics 01-10, this epic, AUD/DAT/NFR/SEC requirements, US-070..US-072 and every source service's audit/recovery contract.
- Expected C# boundaries: `CoreBanking.AuditReconciliation.Domain`, `.Application`, `.Infrastructure`, `.Api`, `.Contracts`, `.Workers` and tests with explicit Audit/Evidence/Recovery modules.
- Domain/Application must not reference ASP.NET Core, EF Core, broker/storage/cloud backup SDKs, SIEM clients or another service implementation/database.
- Define typed versioned audit envelopes/change sets and trace relationships; do not use an ungoverned property bag as the authoritative schema.
- Use strongly typed IDs/protected refs, append-only records, `DateTimeOffset`, `DateOnly`, `TimeProvider`, cancellation tokens and explicit sequence scopes.
- Add source-service audit emission in the same local transaction/outbox as the action; never make a central HTTP call inside a financial transaction.
- Use broker/inbox idempotency, content conflict detection and quarantine. Do not last-write-wins audit evidence.
- Build trace projections only from accepted typed evidence and expose completeness/freshness/source status.
- Keep backup execution credentials/commands in approved platform automation; the application records scoped evidence and gates.
- Add architecture tests forbidding source-service database access, audit-to-Ledger writes, mutable audit repositories and sensitive payload contracts.
- Add PostgreSQL/Testcontainers tests for append-only grants, event conflicts, cross-tenant links, checkpoints, exports, holds and recovery steps.
- Add fault-injection tests to every commit/outbox/ingestion/checkpoint/restore/replay/reopen boundary.
- Exercise recovery in isolated environments with synthetic/masked data and prove network fencing before invoking any restore automation.
- Stop when work requires an unapproved audit schema/coverage, crypto/key design, trace linkage, retention/hold, backup topology, replay rule, RTO/RPO or production authority decision.
- Completion report must list requirements/stories, producer/action coverage, permissions, APIs/events, migrations/grants, integrity policy/checkpoints, tests/commands, trace journeys, backup resources, exercise plans/results/measurements, security/privacy, gaps, assumptions, risks and rollout/rollback notes.

## 27. Traceability summary

| Source | Primary implementation evidence |
| --- | --- |
| AUD-001 / US-070 | Atomic source evidence/outbox, append-only ingestion and privileged/access/configuration coverage |
| AUD-002 / US-070 | Typed actor/role/institution/branch/action/time/source/reason/change/correlation fields |
| AUD-003 / US-070 | Restricted database identities, append-only corrections, checkpoints and independent admin evidence |
| AUD-004 / US-071 | Typed trace graph from request/approval/provider/payment/Ledger to notification/reconciliation |
| NFR-OBS-001/002 | Correlation/causation across services with sensitive-data exclusion |
| NFR-DR-001 / US-072 | Encrypted backup catalogue, verification, isolated restore and retained evidence |
| NFR-DR-002 / US-072 | Versioned targets, measured exercise RTO/RPO and independent sign-off |
| NFR-RES-002 | Original IDs/checkpoints and idempotent restart/replay with no duplicate financial outcome |
| DAT-008 / SEC-008/009/010/011/013/015/017/018 | Append-only corrections, encrypted/masked evidence, scoped access, SIEM and non-production controls |

US-073 degraded-mode business operation and full Phase 2 reconciliation/settlement remain later scope. Phase 1 recovery fencing and safe replay foundations must not be represented as delivering those capabilities.
