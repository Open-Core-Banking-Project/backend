---
phase: "Phase 2 - Regulated Operations"
epic: "Degraded Operations and Phase Readiness"
owning_service: "Platform-wide, coordinated by Operations and Workflow"
modules: [Degraded Capability Policy, Deferred Commands, Recovery Replay, Status Correction, Phase Readiness]
status: Draft
requirements: [ARC-008, NFR-RES-001, NFR-RES-002, NFR-DR-001, NFR-DR-002, NFR-OBS-001, NFR-OBS-002, NFR-MNT-001]
stories: [US-073]
dependencies: [All enabled Phase 2 epics, Phase 1 Operations Release Readiness, Phase 1 Audit Recovery]
adrs: [Degraded capability matrix, Deferred command and replay, Customer status correction]
last_updated: 2026-10-16
---

# Epic 11: Degraded Operations and Phase Readiness

## 1. Objective

Permit only selected safe functions that are explicitly configured and approved during provider/service outage, preserve idempotency and expiry for deferred requests, and recover through controlled replay, reconciliation and customer-status correction while proving Phase 2 release readiness.

## 2. Success measures

- Every degraded capability has approved dependency trigger, allow-list, limits, status wording, expiry, recovery and stop conditions.
- Unapproved or unsafe operations fail closed; no degraded path bypasses identity, risk, ledger, audit or reconciliation.
- Deferred commands replay at most once into the ordinary owning-service contract and remain reconcilable.
- Phase 2 release rehearsal proves outage, queue, replay, reconciliation, restore and rollback/forward-fix evidence.

## 3. Scope

Included: degraded policy/version, dependency state, safe capability matrix, deferred-command envelope, admission/expiry, queue health, recovery batches, replay, reconciliation, customer-status correction, runbooks and phase gates. Excluded: universal offline banking, invented business continuity policy, direct database repair and active-active design without ADR.

## 4. Actors, roles and permissions

| Actor | Permission | Scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Policy manager | `operations.degraded_policy.manage` | Institution/capability | Publish | Maker-checker |
| Incident commander | `operations.degraded_mode.activate` | Incident/environment/scope | Required | Independent approval where required |
| Recovery operator | `operations.deferred.replay` | Approved recovery batch | Required | Runbook/maker-checker |
| Status-correction approver | `operations.status_correct` | Affected journey/customer set | Required | Maker-checker |
| Service identity | `operations.deferred.consume` | Owning service/capability | Workload | Approved policy |

## 5. Domain model

Concepts: `DegradedCapabilityPolicy/Version`, `DependencyCondition`, `Activation`, `DeferredCommand`, `DeferredBatch`, `RecoveryPlan`, `ReplayAttempt`, `ReconciliationResult`, `StatusCorrection`, `ReadinessAssessment`.

States: activation `Proposed -> Approved -> Active -> RecoveryPending -> Closed`; command `Accepted -> Queued -> Expired/ReviewRequired -> Replaying -> Completed/Failed/Uncertain -> Reconciled`.

Invariants: deny by default; policy version frozen per command; original caller/tenant/permission context is retained and revalidated; expiry/limits enforced at admission and replay; replay calls normal contract with original idempotency; queued does not mean financially successful; no command is deleted before terminal/reconciled retention state.

## 6. Use cases

- Detect dependency failure and propose/approve bounded activation.
- Admit or reject an allow-listed command with explicit pending response.
- Monitor capacity, age, expiry and risk while degraded.
- Disable admission and execute checkpointed recovery/replay.
- Reconcile each command to authoritative outcome.
- Correct customer-visible status through approved evidence.
- Assess all Phase 2 epics for production readiness.

## 7. API contracts

Routes: `/api/v1/degraded-policies`, `/degraded-activations`, `/deferred-commands/{id}`, `/recovery-batches`, `/recovery-batches/{id}/execute`, `/status-corrections`, `/readiness-assessments`. Channel-facing ordinary APIs use the same business contract and return stable `pending`, `expired`, `review_required`, `uncertain` or `unavailable` outcomes; clients do not select degraded mode.

## 8. Event contracts

Produced: `DegradedModeActivated/Ended`, `CommandDeferred/Expired/ReviewRequired`, `RecoveryBatchStarted/Completed`, `DeferredCommandReplayed/Reconciled`, `CustomerStatusCorrected`, `PhaseReadinessAssessed`. Consume dependency/service health and authoritative outcome events. Partition by institution/capability/command; inbox/outbox and bounded retry mandatory.

## 9. Database design

No shared operational database. Operations and Workflow owns activation/recovery approval cases; each owning service owns its deferred-command state if approved; Audit owns evidence. Common technical schema may define contract, not shared tables. Required fields: tenant, capability/policy version, original identity/context, safe payload reference/digest, idempotency, captured/expiry time, state, attempt/checkpoint, authoritative outcome and evidence refs. Encrypt sensitive payload storage; unique owner/capability/idempotency.

## 10. Consistency, concurrency and idempotency

Admission and local deferred record commit atomically. Do not acknowledge durable queueing before persistence. Replay leases/checkpoints prevent concurrent execution; ordinary service idempotency is final defense. Expiry and current permission/risk/account state are revalidated. Reconciliation, not distributed transactions, closes cross-service uncertainty.

## 11. Security and privacy

Signed/authenticated context, encrypted deferred payload, least privilege, limited operators and immutable activation/replay evidence. Threat-model forged outage, policy escalation, queue flooding, stale/revoked identity, tampering, replay and data leakage. Rate/capacity limit per tenant/channel/capability; emergency access remains time-bound.

## 12. Audit and observability

Audit policy/version, activation approvals, incident, admission decisions, expiry, replay, exceptions, authoritative outcome, reconciliation and status correction. Metrics: dependency state, active scope, queue depth/oldest age/capacity, expired/review/uncertain count, replay rate/failure and reconciliation backlog. Alerts link runbooks and never include payloads.

## 13. Failure, retry and recovery

If durable storage/audit is unavailable, commands requiring evidence fail closed. Queue saturation stops admission before loss. Recovery pauses on invariant or financial exception. Poison commands go to controlled review, not deletion. Restore retains idempotency, order scope and expiry; replay cannot duplicate financial outcomes.

## 14. Configuration

Version/approve dependency triggers, capabilities, scope/limits, maximum duration/capacity, expiry, priority/order, status text, replay concurrency, stop conditions, escalation and customer correction. Feature flags cannot bypass invariants.

## 15. Implementation slices

1. Approve capability matrix and command/replay ADR.
2. Policy/activation workflow, permissions and evidence.
3. One non-financial degraded walking skeleton.
4. One explicitly approved financial journey with idempotency/reconciliation proof.
5. Queue/expiry/replay/review/status-correction controls.
6. Cross-epic readiness rehearsal, dashboards and runbooks.

## 16. Test plan

| ID | Scenario | Expected |
| --- | --- | --- |
| DOR-01 | Unapproved capability/dependency | Fail closed |
| DOR-02 | Allowed command admitted | Durable pending state/evidence |
| DOR-03 | Duplicate deferred/replay | One authoritative outcome |
| DOR-04 | Expired/stale permission/risk state | Review/reject; no posting |
| DOR-05 | Queue capacity reached | Admission stopped/alerted |
| DOR-06 | Recovery worker restarts | Checkpointed safe continuation |
| DOR-07 | Financial outcome uncertain | Enquiry/reconciliation, no blind retry |
| DOR-08 | Customer status correction | Approved/audited, source state unchanged |
| DOR-09 | Cross-tenant/operator misuse | Denied/audited |
| DOR-10 | Full Phase 2 outage rehearsal | Complete reviewable evidence |

## 17. Acceptance criteria

- [ ] Degraded capabilities are explicit, approved, scoped, limited and disabled by default.
- [ ] Deferred requests preserve idempotency, expiry, order scope and truthful status.
- [ ] Recovery includes safe replay, reconciliation and customer-status correction.
- [ ] Every enabled Phase 2 capability passes release, security, recovery and operational gates.

## 18. Definition of Done

Repository DoD plus approved capability matrix, threat model, load/capacity evidence, replay/reconciliation tests, incident and recovery runbooks, full rehearsal and business/security/operations/compliance sign-off.

## 19. Open decisions

Capability matrix, dependency triggers, allowed transactions, limits/duration/capacity, payload retention, replay ordering/concurrency, stale-state handling, status wording/correction, authority and production targets remain open.

## 20. Prohibited assumptions

Do not enable all reads/writes, let clients select degraded mode, replay expired/high-risk work automatically, claim queued means successful, bypass ordinary APIs/controls, or delete unresolved commands.

## 21. Agent implementation instructions

Read all enabled epics and Phase 1 operations/audit/recovery instructions. Begin non-financially; add financial paths only with approved matrix and reconciliation proof. Run outage, saturation, expiry, concurrency, replay, security, restore and E2E tests. Stop on unresolved scope, status or recovery policy.

