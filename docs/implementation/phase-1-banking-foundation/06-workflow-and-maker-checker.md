---
phase: "Phase 1 - Banking Foundation"
epic: "Workflow and Maker-Checker"
owning_service: "Operations and Workflow"
participating_services:
  - Identity and Access
  - Core Banking and Ledger
  - Customer and Onboarding
  - Payments and VTU
  - Channels and Engagement
  - Audit and Reconciliation
  - Risk and Reporting
modules:
  - Workflow Definitions
  - Workflow Instances
  - Approval Requests
  - Work Queues and Assignment
  - SLA and Escalation
  - Complaints and Service Cases
  - Failed-Payment Investigations
  - Overrides and Exception Actions
status: Draft
requirements: [WFL-001, WFL-002, WFL-003, WFL-004, WFL-005, WFL-006, WFL-007, WFL-008, IAM-005, IAM-006, IAM-007, IAM-010, DAT-001, DAT-005, DAT-006, DAT-008, RPT-002, SEC-008, SEC-009, SEC-010, SEC-011, SEC-013, SEC-017, SEC-018, AUD-001, AUD-002, AUD-003, AUD-004, NFR-OBS-001, NFR-OBS-002, NFR-RES-002, NFR-DR-001, NFR-MNT-001, BR-006, BR-007, BR-008, BR-010, BR-012]
stories: [US-048, US-049, US-050]
dependencies:
  - Phase 0 - Messaging, Outbox, Inbox and Idempotency
  - Phase 1 - Institution and Tenancy
  - Phase 1 - Identity, Roles and Permissions
  - Phase 1 - Core Ledger and Financial Posting
last_updated: 2026-10-15
---

# Epic: Workflow and Maker-Checker

## 1. Objective

Deliver a reusable, versioned human-workflow and case-management capability for maker-checker, four-eyes, multi-level approval, complaints, service requests, failed-payment investigation, escalation and controlled overrides.

Operations and Workflow is authoritative for workflow definitions, instances, tasks, decisions, cases, notes, evidence references, SLA clocks and escalation state. It is not authoritative for institution configuration, identity grants, customer records, accounts, ledger postings, payment outcomes or provider status.

An approval means an authorised human decision was completed against an exact subject version/hash. It does not mean the source business command succeeded. The source service must validate the decision, recheck its own invariants and record the final business outcome.

## 2. Success measures

- Workflow definitions are immutable after publication, versioned and effective-dated.
- Every running instance remains pinned to the exact definition version under which it began unless an explicitly approved migration occurs.
- Maker-checker, four-eyes, sequential, parallel and multi-level approval patterns are supported by typed definitions rather than source-service code changes.
- Routing can use institution, branch, required permission, product, amount/currency, risk category and SLA inputs without trusting role names from clients.
- Maker self-approval and prohibited conflict-of-duty combinations are denied using stable actor identity across roles, sessions and channels.
- Approver entitlement, scope and step-up evidence are revalidated when a decision is made.
- Rejection requires a reason; resubmission creates a linked revision and preserves complete history.
- Complaints and investigations retain category, priority, assignment, SLA, escalation, communications, notes and protected evidence references.
- Failed-payment actions never create duplicate debit/credit outcomes and never treat workflow state as payment or ledger truth.
- Manual overrides require explicit permission, reason, policy eligibility, approval where configured and immutable audit evidence.
- Timers, event replay, worker restart and duplicate decisions cannot create duplicate tasks, decisions or source-service effects.

The institution must approve workflow catalogue, routing/approval thresholds, SLAs, calendars, escalation paths, case taxonomy, override policy and retention. Examples in this document are not production policy.

## 3. Scope

### Included

- Typed workflow definitions, versions, validation, simulation, approval and publication.
- Workflow instances pinned to a definition version and exact subject version/hash.
- Maker-checker, four-eyes, one-level, sequential multi-level, parallel and quorum-capable approval primitives.
- Work-item creation, candidate eligibility, claiming, assignment, release and completion.
- Decision capture with reason, step-up and entitlement evidence.
- Institution, branch, permission, product, amount/currency, risk and SLA routing conditions.
- Business-calendar-aware SLA deadlines, reminders and escalation.
- Rejection, withdrawal, expiry, cancellation and resubmission history.
- Customer complaint and service-request cases.
- Failed, delayed and uncertain payment-investigation cases.
- Case assignment, priority, status, SLA, escalation, notes, communications and attachment/evidence references.
- Controlled requery, repair, reversal-request, freeze/unfreeze-request and escalation actions through owning-service contracts.
- Explicit override requests with approval and post-action evidence.
- Immutable workflow/case audit events and operational observability.

### Excluded

- Applying product, role, customer, account, ledger, payment, CMS or provider state changes. Owning services apply approved decisions.
- Computing identity permissions, scopes or conflicts; Identity and Access is authoritative.
- Posting/reversing ledger entries; Core Ledger is authoritative.
- Determining final provider/payment state; Payments and VTU/Integration Hub are authoritative.
- Complaint notification delivery and templates; Channels and Engagement owns delivery.
- Document binary storage, malware scanning and secure download; Workflow stores protected references only.
- Reconciliation matching/settlement and break authority, owned by Audit and Reconciliation.
- BPMN compatibility, arbitrary scripting, user-supplied executable expressions or a general-purpose orchestration engine unless separately approved.
- Invented approval thresholds, case categories, regulatory SLAs, escalation contacts or override permissions.

## 4. Ownership and trust boundaries

| Capability | Authoritative owner | Workflow responsibility |
| --- | --- | --- |
| Workflow/case definition and version | Operations and Workflow | Authoritative definition lifecycle |
| Instance, work item, SLA and decision | Operations and Workflow | Authoritative process state/evidence |
| Identity, grants, scopes and conflicts | Identity and Access | Request eligible candidates and revalidate decision actor |
| Subject business state/version | Source service | Store protected reference/hash; never replace source truth |
| Apply approved change | Source service | Deliver exact decision idempotently and track acknowledgement |
| Payment/provider/ledger state | Payments, Integration Hub, Ledger | Compose authorised investigation view; issue controlled commands |
| Document/evidence content | Approved document storage/owning service | Store classification and access-controlled reference |
| Customer communication delivery | Channels and Engagement | Request notification and retain delivery reference/status |
| Enterprise immutable audit | Audit and Reconciliation | Commit local evidence/outbox and publish minimal audit event |

Trust rules:

1. A client cannot choose approver eligibility, approval state, maker identity, threshold result or source business outcome.
2. Workflow stores the source subject type, ID, version, canonical hash and safe summary, not an editable duplicate of the business aggregate.
3. Identity supplies candidates/entitlements; Workflow also enforces separation and the source service rechecks decision authority where required.
4. Source services accept decisions only from the authenticated Workflow service and only for the exact pending subject version/hash.
5. Approval completion does not imply a payment, posting, account change or notification completed.
6. Workflow never writes another service database or asks operators to repair business state with direct SQL.

## 5. Actors, roles and permissions

Role names remain configurable. Workflow definitions reference stable permissions and contextual conditions.

| Actor | Permission | Contextual scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Workflow administrator | `workflow.definition.create` | Institution/process namespace | Required | Publication approval |
| Workflow administrator | `workflow.definition.update` | Institution/draft definition | Required | Publication approval |
| Workflow approver | `workflow.definition.approve` | Institution/process namespace | Required | Cannot approve own definition |
| Workflow publisher | `workflow.definition.publish` | Institution/definition/effective date | Required | Exact approved version |
| Maker/source actor | Source permission, not generic Workflow permission | Subject-specific scope | Source policy | Starts request, cannot approve it |
| Approver | Definition-required permission | Institution/branch/product/amount/risk/subject | Required by step | Decision only |
| Work-queue supervisor | `workflow.work_item.assign` | Institution/branch/queue | Required for reassignment | Reason for forced assignment |
| Workflow operator | `workflow.instance.view` | Institution/assigned/authorised subject | Sensitive fields masked | Read-only |
| Customer service officer | `case.complaint.create` | Institution/branch/customer | Policy-based | None |
| Case handler | `case.manage` | Institution/category/assigned case | Sensitive evidence step-up | None |
| Case supervisor | `case.assign` / `case.escalate` | Institution/queue/category | Required | Reason required |
| Payment operations officer | `case.payment_investigation.manage` | Institution/payment/case | Required | Action-specific approval |
| Override maker | `workflow.override.propose` | Institution/operation/resource | Required | Approval by policy |
| Override approver | `workflow.override.approve` | Same exact scope | Required | Cannot approve own override |
| Service identity | `service.workflow.request.create` | Audience/institution/subject type | Service authentication | No human role |
| Source service identity | `service.workflow.decision.consume` | Audience/subject namespace | Service authentication | Applies decision only |

Rules:

- A role name is never sufficient to route or authorise; use permission plus scope and definition conditions.
- Holding multiple roles cannot bypass maker separation or conflict rules.
- Delegation, reassignment and temporary eligibility preserve the original permission/scope requirements and are audited.
- A queue supervisor cannot decide an item solely because they can assign it.
- Case access does not automatically grant access to source customer, payment, provider or ledger detail.

## 6. Domain model

### Aggregates and entities

| Concept | Responsibility | Identity | Important relationships |
| --- | --- | --- | --- |
| `WorkflowDefinition` | Stable process identity/namespace | `WorkflowDefinitionId` | Owns immutable versions |
| `WorkflowVersion` | Typed stages, routes, SLAs and outcomes | `WorkflowVersionId` | Effective period and content hash |
| `WorkflowInstance` | One process execution pinned to version/subject | `WorkflowInstanceId` | Work items, variables and history |
| `WorkflowStepInstance` | Runtime state for one versioned step | `WorkflowStepInstanceId` | Dependencies, decisions and SLA |
| `WorkItem` | Claimable/assigned human task | `WorkItemId` | Candidate rule, assignee and step |
| `ApprovalRequest` | Exact subject/revision requiring decisions | `ApprovalRequestId` | Maker, subject ID/version/hash and decision plan |
| `ApprovalDecision` | Immutable approve/reject/abstain decision | `ApprovalDecisionId` | Actor, permission/scope and exact step |
| `Delegation` | Time-bound task authority metadata | `DelegationId` | Delegator/delegatee/scope/approval |
| `SlaClock` | Deadline, pause and escalation state | `SlaClockId` | Calendar/version and instance/item |
| `Case` | Complaint, service, payment investigation or exception | `CaseId` | Customer/payment/source refs and lifecycle |
| `CaseAssignment` | Effective handler/queue ownership | `CaseAssignmentId` | Case and actor/queue |
| `CaseNote` | Append-only internal/customer-visible note revision | `CaseNoteId` | Author, classification and correction link |
| `CaseEvidenceReference` | Protected attachment/evidence metadata | `EvidenceReferenceId` | Document/storage ref and classification |
| `CaseAction` | Controlled command request/result | `CaseActionId` | Requery/repair/reversal/freeze/override refs |
| `OverrideRequest` | Explicit exception to an approved policy path | `OverrideRequestId` | Target action, reason, policy and approval |

### Value objects

- `WorkflowKey`, `VersionNumber`, `SubjectType`, `SubjectReference`, `SubjectVersion` and `SubjectHash`.
- `StepType`, `DecisionType`, `ApprovalMode`, `Quorum`, `StageOrder` and `DependencyRule`.
- `PermissionRequirement`, `ScopeRequirement`, `AmountThreshold`, `Currency`, `ProductReference` and `RiskCategory`.
- `BusinessCalendarVersion`, `SlaDuration`, `Deadline`, `EscalationLevel` and `Priority`.
- `CaseCategory`, `CaseStatus`, `CommunicationReference`, `EvidenceClassification` and `ResolutionCode`.
- `ActionType`, `ActionStatus`, `ReasonCode`, `CorrelationId` and `IdempotencyKey`.

### Invariants

1. Published workflow versions are immutable and effective-dated.
2. Every instance is pinned to one workflow version and one institution.
3. The subject version/hash on an approval request cannot change after submission.
4. A maker cannot approve any restricted step for the same request, regardless of role/session/channel.
5. An approver must satisfy permission, institution, scope, conflict, step-up and delegation policy at decision time.
6. One actor cannot satisfy multiple distinct approval slots when four-eyes/unique-approver policy prohibits it.
7. A step completes only when its exact sequential/parallel/quorum rule is satisfied by valid final decisions.
8. Rejection requires a reason and is immutable; resubmission creates a new linked request/revision.
9. A duplicate decision/event cannot advance a step twice or invoke the source service twice.
10. A completed/cancelled/expired work item cannot be decided.
11. Running instances do not silently migrate when a new definition publishes.
12. SLA computation records exact calendar/timezone/version and pause periods.
13. Case notes/evidence history is preserved; corrections append a new revision/reference.
14. Sensitive evidence cannot be accessed by general case permission alone.
15. An override never bypasses non-overridable financial, security, tenant or legal invariants.
16. A source action result is recorded separately from the approval decision.
17. Workflow cannot mark a financial transaction Posted, Reversed or Successful.
18. Tenant-owned references cannot cross institutions.

### Definition lifecycle

```text
Draft -> Simulated -> PendingApproval -> Approved -> Scheduled -> Published -> Superseded
   |         |              |               |
   +-> Withdrawn            +-> Rejected    +-> RejectedBeforePublication
```

Editing after simulation invalidates the simulation and approval hash. Rollback publishes another approved version.

### Instance and approval lifecycle

```text
Created -> Running -> WaitingForDecision -> Running -> Completed
                   |                    -> Rejected
                   |                    -> Expired
                   |                    -> Cancelled
                   -> PausedForError -> Running

ApprovalRequest:
Draft -> Submitted -> InReview -> Approved -> Applying -> Applied
                   |          -> Rejected
                   |          -> Expired
                   +-> Withdrawn
Applying -> ApplyFailed -> Applying
```

`Approved` is not `Applied`. `Applied` requires a positive, correlated, idempotent result from the source service for the exact subject version/hash.

### Work item lifecycle

```text
Available -> Claimed -> Assigned -> Completed
          -> Assigned            -> Rejected
          -> Expired
Claimed/Assigned -> Released -> Available
                 -> Escalated -> Assigned
```

Decision revalidates current eligibility even when the actor was eligible at task creation/claim.

### Case lifecycle

```text
Open -> Triage -> Assigned -> Investigating -> PendingExternal -> Investigating
                                      |       -> PendingCustomer -> Investigating
                                      +-> Resolved -> Closed
                                      +-> Escalated -> Investigating
Closed -> Reopened (only by approved policy)
```

Failed-payment resolution remains linked to authoritative Payments, Provider and Ledger statuses. Case closure does not change those statuses.

## 7. Workflow definition model

A versioned definition contains only typed, allow-listed constructs:

- Subject type/version and initiating source service.
- Required maker permission/scope where applicable.
- Stage/step order, dependencies and terminal outcomes.
- Approval mode: one-level, sequential multi-level, parallel, quorum or four-eyes.
- Required permission and contextual-scope selectors for each decision slot.
- Unique-approver and maker-exclusion rules.
- Typed conditions for institution, branch, product, amount/currency, risk category and source-provided approved facts.
- SLA calendar/version, duration, pause rules, reminders and escalation route.
- Rejection, expiry, withdrawal, cancellation and resubmission behaviour.
- Source decision-delivery contract and expected acknowledgement.
- Safe summary/evidence fields allowed in work-item views.

Definitions cannot contain arbitrary C#, JavaScript, SQL, network URLs or unrestricted expressions. A bounded rule DSL/schema requires security and determinism review.

Simulation validates reachability, terminal paths, impossible quorum, contradictory conditions, missing permissions, cycle rules, timer behaviour, maker exclusion, escalation targets and sample routing. Simulation does not approve the business policy.

## 8. Core use cases

### WFL-UC-01 - Publish a workflow version

- Actor: Workflow administrator, separate approver and publisher.
- Flow: Create typed definition, simulate exact hash, submit, approve, schedule and publish.
- Guards: Valid schema/paths, no arbitrary execution, maker cannot approve, effective periods unambiguous.
- Event: `WorkflowVersionPublished.v1` after commit.

### WFL-UC-02 - Start an approval request

- Actor: Authenticated source service on behalf of a maker.
- Input: Institution, workflow key, subject type/ID/version/hash, maker identity, contextual facts/version refs, safe summary and idempotency key.
- Flow: Select exact effective workflow version, validate source and input schema, create instance/request/first steps/work items/SLA and publish facts atomically.
- Guard: Source owns business validation; Workflow refuses an unsupported/stale definition or mismatched tenant.

### WFL-UC-03 - Claim and decide a work item

- Actor: Eligible human approver.
- Flow: Resolve item, revalidate entitlement/scope/conflicts/step-up and maker separation, lock current step/item, store immutable decision, evaluate quorum/dependencies, create next items or terminal request state.
- Concurrency: Unique decision-slot/actor constraints and optimistic/row locking prevent double completion.
- Rejection: Reason required; source receives exact rejected decision.

### WFL-UC-04 - Apply an approved decision

- Actor: Workflow service delivering to source service.
- Flow: Publish/call exact decision ID, subject version/hash and approval evidence; source applies idempotently or rejects; Workflow records acknowledgement/result separately.
- Failure: Approval stays `ApplyFailed`/retryable; it is never reported as applied without source acknowledgement.
- Source conflict: Mark terminal conflict/manual review according to policy; do not alter subject hash.

### WFL-UC-05 - Resubmit after rejection

- Actor: Source service/maker.
- Flow: Source creates a new subject revision/hash and starts a new approval request linked to the rejected request.
- Guard: Old decisions remain immutable and cannot be copied as valid approvals for the new revision.

### WFL-UC-06 - Open and resolve a complaint

- Actor: Customer service officer or approved channel/service.
- Flow: Create case with category, customer/source refs and communication ref; triage/assign; add classified notes/evidence; track SLA/escalation; resolve with evidence and request customer notification; close after policy gates.
- Guard: Customer-visible and internal notes are separated; notification delivery status is tracked explicitly.

### WFL-UC-07 - Investigate a failed/uncertain payment

- Actor: Payment operations officer.
- Flow: Open/deduplicate case from payment event, compose current Payments/Provider/Ledger statuses with source/as-of metadata, select controlled action, obtain approval if required, send command to owning service and record authoritative result.
- Actions: Requery, repair metadata/state through approved Payments command, request reversal, escalate or close with no-action reason.
- Guard: Workflow never directly calls provider to create payment, posts Ledger, or treats timeout as failure.

### WFL-UC-08 - Execute a manual override

- Actor: Override maker and separate approver where configured.
- Flow: Validate override type is allow-listed, capture reason/evidence/expiry and source state/hash, approve exact request, send to owner, record applied/rejected result and schedule review if required.
- Guard: No generic “force success”, direct balance edit, tenant bypass or audit suppression override exists.

## 9. API contracts

| Method | Route | Permission | Request | Response | Idempotent |
| --- | --- | --- | --- | --- | --- |
| POST | `/api/v1/workflow-definitions` | `workflow.definition.create` | `CreateWorkflowDefinitionRequest` | `WorkflowDefinitionResponse` | By request ID |
| POST | `/api/v1/workflow-definitions/{id}/versions` | `workflow.definition.update` | `CreateWorkflowVersionRequest` | `WorkflowVersionResponse` | By request ID |
| POST | `/api/v1/workflow-versions/{id}/simulations` | `workflow.definition.update` | `WorkflowSimulationRequest` | `WorkflowSimulationResponse` | By input/hash |
| POST | `/api/v1/workflow-versions/{id}/submit` | `workflow.definition.update` | Version/hash/reason | `WorkflowVersionResponse` | Yes |
| POST | `/api/v1/workflow-versions/{id}/publish` | `workflow.definition.publish` | Approved hash/effective time | `WorkflowVersionResponse` | Yes |
| POST | `/internal/v1/approval-requests` | `service.workflow.request.create` | `CreateApprovalRequest` | `ApprovalRequestResponse` | Required |
| GET | `/api/v1/work-items` | Authenticated approver | Bounded eligible/assigned filters | `WorkItemPage` | Yes |
| POST | `/api/v1/work-items/{id}/claim` | Required step permission | Expected version | `WorkItemResponse` | Yes |
| POST | `/api/v1/work-items/{id}/decisions` | Required step permission | `CreateDecisionRequest` | `ApprovalDecisionResponse` | By decision ID |
| POST | `/api/v1/work-items/{id}/release` | Assignee/supervisor permission | Reason/version | `WorkItemResponse` | Yes |
| POST | `/api/v1/work-items/{id}/assignments` | `workflow.work_item.assign` | Actor/queue/reason/version | `WorkItemResponse` | By request ID |
| GET | `/api/v1/approval-requests/{id}` | Maker/approver/source-scoped view | Safe fields | `ApprovalRequestResponse` | Yes |
| POST | `/api/v1/cases` | Case-type create permission | `CreateCaseRequest` | `CaseResponse` | By request ID |
| GET | `/api/v1/cases` | Case view permission | Bounded queue/category/status filters | `CasePage` | Yes |
| POST | `/api/v1/cases/{id}/assignments` | `case.assign` | Assignee/queue/reason/version | `CaseResponse` | By request ID |
| POST | `/api/v1/cases/{id}/notes` | `case.manage` | Classified note contract | `CaseNoteResponse` | By request ID |
| POST | `/api/v1/cases/{id}/evidence-references` | `case.manage` plus evidence scope | Protected document ref/classification | `CaseEvidenceResponse` | By request ID |
| POST | `/api/v1/cases/{id}/actions` | Case/action-specific permission | Typed action/reason/version | `CaseActionResponse` | By request ID |
| POST | `/api/v1/cases/{id}/resolution-proposals` | `case.resolve` | Resolution/evidence/version | `CaseResponse` | By request ID |
| POST | `/internal/v1/approval-requests/{id}/application-results` | Source service permission | Decision/subject/result refs | `ApprovalRequestResponse` | By source result ID |

Contract rules:

- Source/maker identity, institution, subject version/hash and amount/currency facts are authenticated and canonicalised; clients cannot assert approval truth.
- Work-item list results are candidates, not durable authorisation. Decision always revalidates.
- Definitions/actions use discriminated typed schemas, not arbitrary JSON commands.
- Notes, summaries and reasons have length, content, classification and redaction controls; no secrets/full payment payloads.
- RFC 7807 errors use stable codes without revealing cross-tenant cases/tasks or candidate lists.
- Collection endpoints use cursor pagination and allow-listed filters. Bulk export is separately authorised.

## 10. Event contracts

### Produced events

| Event | Trigger | Key fields | Ordering key | Consumers |
| --- | --- | --- | --- | --- |
| `WorkflowVersionPublished.v1` | Version becomes effective | Definition/version IDs, institution, key, effective period, hash | Definition ID | Source services, Audit |
| `ApprovalRequested.v1` | Request/instance commits | Request/instance IDs, subject ref/version/hash, maker, workflow version | Approval request ID | Source, Channels, Audit |
| `WorkItemAssigned.v1` | Assignment commits | Work item/request, assignee/queue ref, deadline, safe summary ref | Work item ID | Channels, Audit |
| `ApprovalDecided.v1` | Valid decision changes request state | Request/decision IDs, subject version/hash, outcome, actor, step, workflow version | Approval request ID | Exact source service, Audit |
| `ApprovalApplicationFailed.v1` | Source rejects/fails application | Request/decision/source result, safe reason | Approval request ID | Operations, Audit |
| `WorkflowEscalated.v1` | SLA/rule escalation commits | Instance/item, level, deadline, target queue/ref | Instance ID | Operations, Channels, Audit |
| `CaseCreated.v1` | Case commits | Case ID/type/category/priority, institution, subject/customer refs | Case ID | Channels, Audit, source service |
| `CaseStatusChanged.v1` | Case lifecycle transition | Case ID, old/new status, reason/resolution category | Case ID | Channels, source, Audit |
| `CaseActionRequested.v1` | Controlled action commits | Case/action IDs, action type, target subject/version, approval ref | Case ID | Owning source service |
| `CaseActionResolved.v1` | Source result recorded | Case/action/source result IDs, outcome/safe reason | Case ID | Operations, Audit |
| `OverrideApproved.v1` | Exact override approved | Override ID/type/target version/hash/expiry | Override ID | Owning source only, Audit |

### Consumed events

| Event | Producer | Consumer behaviour | Deduplication key | Failure handling |
| --- | --- | --- | --- | --- |
| `EntitlementVersionAdvanced.v1` | Identity and Access | Invalidate candidate/decision entitlement cache | Event ID/version | Decision fails closed until fresh |
| `InstitutionConfigurationPublished.v1` | Core institution module | Refresh business calendar and approved workflow refs | Event ID/version | Timer/creation pauses on gaps |
| `PaymentStatusChanged.v1` | Payments and VTU | Create/update deduplicated investigation where policy requires | Event ID/payment version | Inbox retry; version gap resync |
| `ProviderAttemptChanged.v1` | Payments/Integration | Refresh investigation provider-status projection | Event ID/attempt version | Mark status stale/unavailable |
| `FinancialTransactionPosted.v1` | Ledger | Update investigation ledger-status projection/reference | Event ID | Never infer provider success |
| `FinancialTransactionReversed.v1` | Ledger | Resolve linked reversal action result/status | Event ID | Reconcile action until linked |
| `NotificationDeliveryChanged.v1` | Channels | Record complaint/customer communication delivery state | Event ID | Case retains pending/failed status |
| `DocumentStatusChanged.v1` | Document capability | Mark evidence usable/quarantined/revoked | Event ID/version | Sensitive action blocks if required evidence unavailable |

Every event uses the standard envelope and minimal safe data. Events exclude unrestricted case notes, document contents, full account numbers, provider payloads, customer PII and secrets.

Outbox/inbox is mandatory. Replay cannot create duplicate work items, decisions, escalations, cases, source commands or notifications.

## 11. Database design

### Ownership

- Database: `operations_workflow_db`.
- Schemas: `workflow`, `approval`, `case_management`, `integration`.
- Migration assembly: Operations and Workflow Infrastructure project.
- Principals: Separate runtime/migration identities; no other service has direct table access.
- Attachments: Encrypted document/object storage via protected references; no binary content in workflow tables.

### Workflow and approval tables

| Table | Purpose | Key constraints | Tenant-owned | Retention |
| --- | --- | --- | --- | --- |
| `workflow.workflow_definitions` | Stable process key/namespace | Institution/key unique | Yes | Permanent history |
| `workflow.workflow_versions` | Immutable typed definition/effective period/hash | Definition/version unique | Yes | Permanent |
| `workflow.definition_status_history` | Publication lifecycle/evidence | Version/sequence unique | Yes | Permanent |
| `workflow.workflow_instances` | Runtime pinned version/subject | Institution/source/idempotency unique | Yes | Policy/legal hold |
| `workflow.workflow_steps` | Runtime step/dependency state | Instance/definition-step unique | Yes | With instance |
| `workflow.work_items` | Task, candidate rule, assignment/deadline | Step/slot unique; concurrency | Yes | Policy/audit |
| `workflow.work_item_status_history` | Append-only task transitions | Item/sequence unique | Yes | Policy/audit |
| `workflow.sla_clocks` | Deadline/calendar/pause/escalation | Instance/item/purpose unique | Yes | Policy/audit |
| `workflow.escalation_events` | Append-only escalation outcomes | Clock/level unique | Yes | Policy/audit |
| `approval.approval_requests` | Exact maker/subject/version/hash and state | Institution/source request key unique | Yes | Permanent evidence |
| `approval.approval_steps` | Runtime decision requirements/quorum | Request/step key unique | Yes | Permanent evidence |
| `approval.approval_decisions` | Immutable human decisions | Decision ID and step/actor/slot constraints | Yes | Permanent evidence |
| `approval.delegations` | Time-bound approved task delegation | Actor/scope/period constraints | Yes | Permanent evidence |
| `approval.application_results` | Source-service acknowledgement/outcome | Source result ID unique; request/decision unique as policy | Yes | Permanent evidence |
| `approval.override_requests` | Typed exception request/version/hash | Institution/request key unique | Yes | Permanent evidence |

### Case tables

| Table | Purpose | Key constraints | Tenant-owned | Retention |
| --- | --- | --- | --- | --- |
| `case_management.cases` | Complaint/service/investigation identity/state | Institution/case number unique; concurrency | Yes | Policy/legal hold |
| `case_management.case_status_history` | Append-only transitions/resolutions | Case/sequence unique | Yes | Policy/legal hold |
| `case_management.case_assignments` | Effective queue/actor ownership | Case/assignment version | Yes | Historical |
| `case_management.case_notes` | Classified append-only note revisions | Note/revision unique | Yes | Classification policy |
| `case_management.case_evidence_references` | Protected document/evidence refs | Case/reference/version unique | Yes | Evidence policy/legal hold |
| `case_management.case_communications` | Customer communication request/delivery refs | Case/communication ID unique | Yes | Consent/retention policy |
| `case_management.case_actions` | Typed requery/repair/reversal/escalation command | Case/request ID unique; target version/hash | Yes | Permanent evidence |
| `case_management.sla_events` | Case deadline/pause/escalation history | Case/clock/sequence unique | Yes | Policy/audit |
| `case_management.payment_status_snapshots` | Non-authoritative source/as-of summaries | Case/payment/source/version unique | Yes | Operational; labelled projection |
| `integration.idempotency_records` | Stable mutation outcomes | Institution/operation/key unique; request hash | Yes | Approved horizon |
| `integration.outbox_messages` | Durable facts/commands | Event ID unique | Yes | Replay horizon |
| `integration.inbox_messages` | Consumer deduplication | Consumer/event ID unique | Yes | Replay horizon |

### Constraints and indexes

- Every tenant-owned unique index includes `institution_id` unless deliberately platform-wide.
- Published definition versions and approval decisions are append-only under runtime database privileges.
- Effective-period and version constraints prevent ambiguous published workflow selection.
- Unique constraints prevent one actor filling prohibited multiple decision slots and prevent duplicate terminal decisions.
- Work item, instance, case and action aggregates use concurrency versions.
- SLA/queue indexes cover institution/status/priority/deadline/assignment with bounded queries.
- Source IDs are protected references without cross-database foreign keys.
- Database constraints enforce required rejection/override reasons, expiry ordering and terminal-state rules where practical.

### Migration and backfill

- Seed only platform-owned workflow/action type schemas, never institution thresholds, approvers, SLAs or permissions.
- Existing process migration pins each open instance to a validated imported version and retains legacy reference/evidence quality.
- Do not migrate an informal historical approval as cryptographically/currently valid authority without explicit cutover approval.
- Released migrations are append-only. Decision/case/evidence history is never truncated by ordinary migration.

## 12. Routing, assignment and decision evaluation

Routing is a two-stage process:

1. Definition evaluation produces required permission/scope/condition slots and target queues.
2. Identity and Access returns or validates candidate identities/entitlements for those slots.

Candidate lists may be cached briefly for task discovery, but decision authorisation is never cached beyond the approved entitlement freshness window. Decision evaluation verifies:

- Identity/session/step-up and current entitlement version.
- Institution, branch and resource scope.
- Required permission and conflict-of-duty rules.
- Maker exclusion and prior decision actors.
- Amount/currency, product and risk facts bound to the subject version/hash.
- Work item/step/request state and concurrency version.
- Delegation validity, scope, period and approval.

Assignment does not grant permission. Reassignment cannot weaken decision requirements. If no eligible approver exists, the request enters an explicit blocked/escalated state; Workflow never self-approves or chooses a broader role.

## 13. Failed-payment investigation safety

The case view displays separately:

| Status | Source of truth | Required metadata |
| --- | --- | --- |
| Payment instruction | Payments and VTU | Status/version/observed time |
| Provider attempt | Payments/Integration Hub | Attempt/status/reference/observed time |
| Ledger transaction | Core Ledger | Posted/rejected/reversed reference/observed time |
| Reconciliation | Audit and Reconciliation | Break/status/observed time |

Rules:

- Missing/unavailable source data is shown as unavailable or stale, never “failed” or “not posted”.
- `Requery` asks the owning Payments/Integration service to perform approved status enquiry.
- `Repair` is a typed owning-service command for a known repairable inconsistency, not arbitrary state editing.
- `Reverse` is a request to Payments/Ledger with original references and approval; Ledger decides eligibility and posting.
- Provider timeout remains Uncertain until authoritative resolution. Do not fail over, reverse or resubmit blindly.
- Every action stores actor, permission, reason, source versions, command ID, correlation ID and final authoritative result.

## 14. Consistency, concurrency and idempotency

- Definition simulation/approval/publication uses exact canonical content hash and optimistic concurrency.
- Instance creation is idempotent by institution/source service/subject/request key.
- Decision creation locks or compares request, step, work-item and decision-slot state in one local transaction.
- Decision, state transition, new work items, audit, idempotency result and outbox event commit atomically.
- Timer/escalation workers claim due clocks through database-backed leasing/skip-locked or approved equivalent; unique clock/level constraints prevent duplicates.
- Source decision delivery is at-least-once with stable decision ID/subject hash. Source application is idempotent and acknowledged.
- Case creation from events is unique by institution/case type/source subject/policy version as defined.
- Case action uses institution/operation/action request key and source target version/hash.
- No distributed transaction is used. Apply status and reconciliation expose cross-service convergence explicitly.

## 15. Security, privacy and evidence

- Tenant/resource authorisation is enforced in Workflow and rechecked by each source service.
- Explicit contract mapping prevents mass assignment of maker, approver, status, priority, assignment, outcome, SLA or source result.
- Definitions are data, never executable user code; expression complexity and evaluation time are bounded.
- Work-item safe summaries reveal the minimum needed to decide. Full source data requires source-service permission.
- Case notes declare Internal, CustomerVisible or RestrictedEvidence classification. Do not silently expose internal notes in customer responses.
- Attachments use approved upload/scan/quarantine/document controls before they can satisfy evidence requirements.
- Sensitive payment/provider/ledger data is referenced and masked, not copied into notes/logs/events.
- Notes/reasons are untrusted text: encode on output, validate length/content and exclude them from metric labels.
- Search/export are bounded and separately permissioned to prevent case/customer harvesting.
- Production case data cannot be used in non-production without approved irreversible masking.

Negative tests cover cross-tenant IDs, task enumeration, maker as approver through another role/session, stale entitlement, duplicate/quorum races, assignment-as-permission, note/evidence leakage, malicious definition expressions, mass assignment and forged source application results.

## 16. Audit and observability

Audit definition lifecycle; instance creation/migration/cancellation; task creation/claim/assignment/release/escalation/expiry; every decision; source-application result; case creation/access/assignment/note/evidence/action/resolution/reopen; override lifecycle; SLA pause/resume and administrative repair.

Evidence includes actor/service, permission/role context, institution/branch, workflow/version/hash, request/subject/version/hash, maker/approver identities, decision/reason, entitlement/step-up evidence refs, case/action refs, source result, before/after state, deadline/calendar version, correlation/causation and timestamp. Do not copy restricted source payloads.

Minimum metrics:

- Requests/instances by workflow/version/state/age.
- Work items by queue/state/priority/deadline and claim/decision latency.
- Approval/rejection/expiry/cancellation by safe reason and stage.
- Maker-self-approval/conflict/stale-entitlement denials.
- SLA due/overdue/escalated/paused and timer lag.
- Source-application pending/failure/retry/convergence age.
- Cases by type/category/priority/state/age/assignment.
- Failed-payment cases by source status combination and unresolved age.
- Case action success/failure/retry and reversal/requery outcome.
- Override volume/age/outcome and post-review status.
- Outbox/inbox lag, duplicates, dead letters and audit failure.

Traces span source request, Workflow decisions, source application, payment/provider/ledger actions, notification and audit using correlation/causation IDs. Logs exclude subject payloads, notes, evidence, full account/customer data and tokens.

## 17. Failure, retry and recovery

| Failure | Safe state | Recovery |
| --- | --- | --- |
| Definition missing/ambiguous/stale | Request not started | Publish/fix approved version; retry same request key |
| Identity/entitlement unavailable | Claim/decision denied or paused | Refresh and retry; never assume eligibility |
| Concurrent decisions | One legal slot/state transition wins | Return current result/conflict; no duplicate source command |
| Timer worker crash | Clock remains due/leased until expiry | Another worker claims idempotently |
| Source service unavailable after approval | Request remains ApplyFailed/Applying | Retry same decision/subject hash |
| Source rejects stale subject | Explicit application conflict | New source revision/resubmission or investigation |
| Notification unavailable | Decision/case remains valid; delivery pending | Channels retries independently |
| Evidence quarantined/unavailable | Dependent decision/action blocked | Resume after approved usable status |
| Payment/provider/ledger source unavailable | Investigation section marked stale/unavailable | Retry/resync; no inferred outcome |
| Restore | New decisions/actions paused until reconciliation | Verify instances, slots, clocks, outbox and source acknowledgements |

Manual repair is a permissioned command that preserves original state/history and publishes evidence. Operators do not edit decision, task or case rows directly.

Backup/restore exercises verify definition/version hashes, running-instance pinning, decision uniqueness, maker separation, SLA deadlines/timezones, case history/evidence refs, source-application acknowledgements and outbox/inbox replay safety.

## 18. Configuration

| Setting | Owner | Secret | Validation | Reload behaviour |
| --- | --- | --- | --- | --- |
| Workflow definitions | Institution process owner | No | Typed schema, simulation, approval | Versioned/effective-dated |
| Approval thresholds/quorum | Business/Risk/Compliance | No | Currency/product/risk/unique actor rules | Workflow version |
| Permission/scope routing | Security/process owner | No | Published permission catalogue | Workflow version |
| Business calendar/timezone | Institution operations | No | Published calendar version | Instance/run snapshot |
| SLA/pause/escalation policy | Operations/Compliance | No | Calendar, duration, targets, terminal path | Workflow/case version |
| Case categories/priorities | Operations/Product | No | Ownership, required fields and SLA | Versioned |
| Override catalogue | Risk/Security/domain owner | No | Allow-list, non-overridable invariants, approvals | Versioned |
| Evidence classification/retention | Compliance/Privacy | No | Document policy/legal hold | Versioned |
| Source command timeout/retry | Workflow/source owner | No | Idempotency and status enquiry semantics | Controlled |
| Notification template refs | Channels/process owner | No | Published template/channel | Versioned reference |

Missing configuration blocks the affected process/action. Feature flags cannot bypass maker-checker, tenant, source validation, financial invariants or audit.

## 19. Implementation slices

1. Approve definition DSL/schema, approval patterns, routing ownership, SLA/calendar, case taxonomy, override and retention decisions.
2. Define versioned definition, approval, case, action and event contracts plus architecture/security tests.
3. Implement workflow definition/version simulation and publication lifecycle.
4. Implement instance/request creation pinned to subject/version/hash and source idempotency.
5. Implement sequential maker-checker with work items, Identity decision revalidation and self-approval denial.
6. Add parallel/quorum/multi-level patterns with concurrency/property tests.
7. Implement SLA clocks, business-calendar deadlines, reminders and idempotent escalation workers.
8. Implement source decision delivery, application acknowledgement, retry and explicit ApplyFailed state.
9. Implement complaint/service case lifecycle, assignment, notes, evidence refs and notification handoff.
10. Implement failed-payment investigation projections and requery/escalate actions with synthetic owning-service adapters.
11. Implement approved repair/reversal/freeze/override requests without direct business-state mutation.
12. Add audit, dashboards, alerts, reconciliation jobs and runbooks.
13. Complete security, resilience, performance, backup/restore and end-to-end evidence.

Each slice starts with traceable failing tests and leaves incomplete workflows/actions unavailable rather than permissive.

## 20. Test plan

| Test ID | Requirement/story | Level | Scenario | Expected evidence |
| --- | --- | --- | --- | --- |
| WFL-001 | WFL-001 / US-048 | Integration | Publish versioned effective workflow | Immutable version/hash and effective selection |
| WFL-002 | WFL-001 / US-048 | Integration | New version while instance runs | Existing instance remains pinned |
| WFL-003 | WFL-001 | Security | Definition contains arbitrary expression/code | Rejected by typed schema |
| WFL-004 | WFL-002 / US-048 | Domain | One-level maker-checker | Separate valid checker completes step |
| WFL-005 | WFL-002 / US-048 | Domain | Sequential multi-level approval | Stage order enforced |
| WFL-006 | WFL-002 / US-048 | Concurrency | Parallel/quorum decisions race | Exact quorum once; unique actor rules preserved |
| WFL-007 | IAM-006 / BR-008 | Security | Maker approves through another role/session | Denied by stable actor ID |
| WFL-008 | IAM-005 | Security | Conflicted role combination decides | Denied using current Identity rules |
| WFL-009 | WFL-003 | Integration | Route by branch/permission/amount/product/risk | Only eligible queue/candidates; exact facts/version |
| WFL-010 | WFL-003 | Security | Assignment without decision permission | Cannot decide despite assignment |
| WFL-011 | WFL-003 | Security | Entitlement revoked after assignment | Decision fails closed on revalidation |
| WFL-012 | WFL-004 | Domain | Reject without reason | Denied |
| WFL-013 | WFL-004 | Integration | Resubmit corrected subject | New request/hash linked; old history preserved |
| WFL-014 | WFL-003 | Time | SLA across holiday/timezone | Deadline uses exact calendar version |
| WFL-015 | WFL-003 | Concurrency | Two timer workers escalate same item | One escalation outcome/event |
| WFL-016 | WFL-005, WFL-006 / US-049 | Integration | Create, assign, investigate and resolve complaint | Complete status/SLA/evidence/communication history |
| WFL-017 | WFL-006 / US-049 | Security | Internal note requested by customer channel | Not disclosed |
| WFL-018 | WFL-008 / US-049 | Security | Quarantined/unauthorised attachment | Cannot view/use as evidence |
| WFL-019 | WFL-005 / US-050 | Integration | Failed payment event opens case | One deduplicated investigation with source refs |
| WFL-020 | US-050 | Resilience | Provider/Ledger status unavailable | Explicit stale/unavailable, no inferred outcome |
| WFL-021 | US-050 / BR-006 | Security | Requery uncertain payment | Owning-service status enquiry; no duplicate submission |
| WFL-022 | US-050 | Integration | Authorised reversal request | Ledger/Payments decides result; Workflow records it |
| WFL-023 | US-050 | Security | Operator edits payment/ledger state through case | No such path; rejected by architecture/contract |
| WFL-024 | WFL-007 | Security | Override without explicit permission/reason | Denied and audited |
| WFL-025 | WFL-007 | Security | Override attempts non-overridable financial invariant | Denied regardless of approval |
| WFL-026 | WFL-007 | Integration | Approved allow-listed override | Source revalidates/applies exact hash and acknowledges |
| WFL-027 | WFL-002 | Idempotency | Replay decision request/event | One decision/step transition/source command |
| WFL-028 | WFL-002 | Concurrency | Approve and reject same slot simultaneously | One legal terminal decision wins |
| WFL-029 | WFL-002 | Resilience | Source unavailable after approval | ApplyFailed/retry; never falsely Applied |
| WFL-030 | SEC-009 | Security | Cross-tenant work item/case/subject ID | Denied without existence disclosure |
| WFL-031 | SEC-011 | Security | Mass-assign status/assignee/maker/outcome | Rejected/ignored; no elevation |
| WFL-032 | NFR-RES-002 | Messaging | Replay source/status events | No duplicate case/task/action/notification |
| WFL-033 | AUD-001..AUD-004 | Integration | Review full decision/action trail | Immutable actor/reason/version/correlation evidence |
| WFL-034 | NFR-DR-001 | Recovery | Restore with running approvals/cases | Slots, clocks, decisions and source acknowledgements reconcile |
| WFL-035 | WFL-001..WFL-008 | End-to-end | Product/account/payment approval plus complaint | Reusable workflows preserve source authority and traceability |

Use `TimeProvider`/controlled clocks for deterministic SLA tests and real PostgreSQL connections for decision/timer concurrency. Load profiles require approved queue/case volumes.

## 21. Acceptance criteria

- [ ] WFL-001 through WFL-008 and US-048 through US-050 trace to decisions, contracts, code and tests.
- [ ] Definitions are typed, versioned, effective-dated, simulated, approved and immutable after publication.
- [ ] Running instances remain pinned to their starting version.
- [ ] Maker-checker, four-eyes, sequential, parallel, multi-level and quorum controls behave deterministically.
- [ ] Decision time revalidates identity, permission, scope, conflicts, maker separation and step-up.
- [ ] Rejection reasons and resubmission history are complete and immutable.
- [ ] Approval and source application are distinct states with idempotent acknowledgement/retry.
- [ ] Complaints retain assignment, SLA, notes, evidence, resolution and communication history.
- [ ] Failed-payment cases display separately sourced statuses and cannot cause duplicate financial outcomes.
- [ ] Overrides are typed, allow-listed, permissioned, reasoned, approved and unable to bypass hard invariants.
- [ ] Tenant isolation, evidence access, audit, replay, concurrency and recovery tests pass.

## 22. Definition of Done

- Build, analyzers, formatting, architecture tests and affected suites pass.
- EF Core migrations apply to empty/supported upgrade databases; immutability, uniqueness and decision constraints are verified.
- OpenAPI/internal/event contracts pass compatibility, security and data-minimisation review.
- Identity integration proves revoked/stale entitlement and maker-self-approval denial.
- At least one product/account approval and one financial action approval apply to the exact source version and return authoritative source results.
- Complaint and failed-payment journeys pass end to end with synthetic data/provider states.
- Timer restart, source outage, event replay, backup/restore and reconciliation runbooks are demonstrated.
- Traceability links requirements, stories, definitions, permissions, APIs/events, migrations, tests and operational evidence.

## 23. Open decisions

| Decision | Impact | Owner | Required by | Status |
| --- | --- | --- | --- | --- |
| Definition DSL/schema and supported approval patterns | Engine scope/security | Architecture/Operations | Slice 1 | Open |
| Initial workflow catalogue and source namespaces | Phase 1 integration scope | Product/Domain owners | Slice 1 | Open |
| Thresholds, quorum and unique-approver policies | Maker-checker outcomes | Risk/Compliance/Domain owners | Slice 5 | Open |
| Candidate resolution/cache/freshness policy | Security and availability | Identity/Security | Slice 5 | Open |
| Delegation and reassignment policy | Approver continuity/SoD | Security/Operations | Slice 5 | Open |
| Business calendars, SLA pause and escalation rules | Deadlines/compliance | Operations/Compliance | Slice 7 | Open |
| Definition/instance migration policy | Running process behaviour | Architecture/Operations | Before migration feature | Open |
| Complaint/service taxonomy and customer communications | US-049 scope | Customer Service/Product | Slice 9 | Open |
| Case note/evidence classification and retention | Privacy/access | Compliance/Privacy | Slice 9 | Open |
| Failed-payment case creation thresholds and status views | US-050 workload/safety | Payments/Operations | Slice 10 | Open |
| Allowed requery/repair/reversal/freeze action contracts | Financial/operational safety | Domain owners/Risk | Slice 10 | Open |
| Override catalogue and non-overridable invariants | Exceptional authority | Security/Risk/Finance | Slice 11 | Open |
| Capacity, latency, availability, RTO and RPO | Release gates | Product/Platform | Before release testing | Open |

## 24. Prohibited assumptions

- Do not invent workflow thresholds, approver roles, SLAs, escalation targets or case categories.
- Do not embed arbitrary executable code, SQL, URLs or unbounded expressions in workflow definitions.
- Do not hard-code business behaviour to role names.
- Do not treat assignment, queue membership or cached candidate status as decision authority.
- Do not let a maker approve through another role/session or satisfy multiple prohibited approval slots.
- Do not mutate a running instance to a newly published definition silently.
- Do not treat Approved as Applied or as proof of payment/ledger/provider success.
- Do not let Workflow modify another service database or state directly.
- Do not copy full sensitive source aggregates, provider payloads, ledger details or attachments into workflow records/events/logs.
- Do not infer Failed from an unavailable/uncertain payment/provider state.
- Do not requery, repair, resubmit, fail over or reverse through a generic action.
- Do not create a generic “force success”, balance override or audit bypass.
- Do not delete rejected submissions, decisions, notes, actions or source results.

Until policy and contracts are approved, keep the workflow/action unavailable and use synthetic definitions/adapters only.

## 25. Agent implementation instructions

- Required reading: root `AGENTS.md`, architecture, Phase 0 evidence, prior Phase 1 epics, this epic, WFL/IAM/DAT/SEC/AUD requirements, US-048..US-050 and approved process/security decisions.
- Expected C# boundaries: `CoreBanking.OperationsWorkflow.Domain`, `.Application`, `.Infrastructure`, `.Api`, `.Contracts` and `.Tests`.
- Domain code must not reference ASP.NET, EF Core, Identity SDKs, brokers, document storage or source-service clients.
- Keep definitions typed and deterministic; source-specific adapters/contracts belong in Application/Infrastructure, not Domain branching by service name.
- Use strongly typed IDs, explicit `Money`/currency for routing thresholds, `TimeProvider`, `DateTimeOffset`, cancellation tokens and optimistic concurrency.
- Use EF Core migrations owned by Operations and Workflow and real PostgreSQL/Testcontainers for decision/quorum/timer concurrency.
- Start each slice with traceable failing tests, especially self-approval, stale entitlement, duplicate decision, cross-tenant, source-application and evidence-leakage negatives.
- Use outbox/inbox for every cross-service decision/action and stable idempotency keys for source delivery/application.
- Stop when work requires an unapproved workflow language, threshold, role/permission, SLA, escalation, case taxonomy, repair/reversal action, override or retention decision.
- Completion report must list requirement/story coverage, definitions/versions, permissions, APIs/events, migrations, tests/commands, concurrency/replay/recovery evidence, assumptions, risks and rollout notes.

