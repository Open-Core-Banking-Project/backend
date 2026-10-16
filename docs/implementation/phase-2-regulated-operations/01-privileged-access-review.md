---
phase: "Phase 2 - Regulated Operations"
epic: "Privileged Access Review"
owning_service: "Identity and Access"
modules: [Access Review, Temporary Access, Entitlement Revocation]
status: Draft
requirements: [IAM-004, IAM-005, IAM-009, IAM-010, SEC-009, SEC-010, AUD-001, AUD-002]
stories: [US-010]
dependencies: [Phase 1 Identity Roles and Permissions, Phase 1 Audit Traceability and Recovery]
adrs: [Identity provider and token model, Entitlement invalidation, Privileged access governance]
last_updated: 2026-10-16
---

# Epic 01: Privileged Access Review

## 1. Objective

Schedule and complete evidence-based reviews of privileged accounts, temporary permissions and access history so excess access is revoked and overdue decisions are escalated.

## 2. Success measures

- Campaigns produce immutable entitlement snapshots and assigned review items.
- Owners retain, revoke or request evidence with reason and step-up authentication.
- Revocation invalidates active entitlement versions and applicable sessions promptly.
- Overdue items escalate under a versioned policy; completion reports reconcile every in-scope item.

## 3. Scope

Included: campaign definitions/runs, scope selection, snapshots, assignment, decisions, evidence requests, revocation, expiry, escalation and reports. Excluded: PAM password vaulting, employee HR lifecycle, and institution-specific review cadence not yet approved.

## 4. Actors, roles and permissions

| Actor | Permission | Scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Campaign administrator | `identity.access_review.manage` | Institution/campaign | Required to launch | Maker-checker |
| Reviewer | `identity.access_review.decide` | Assigned items | Required | None |
| Evidence provider | `identity.access_review.evidence.submit` | Assigned item | Policy based | None |
| Security auditor | `identity.access_review.audit` | Institution/period | Required for export | None |

Reviewers cannot decide their own privileged access or override conflicts of duty. Identity and Access owns permissions and enforces current scope on every command.

## 5. Domain model

| Concept | Responsibility | Key rules |
| --- | --- | --- |
| `ReviewCampaignDefinition` | Versioned scope, cadence and escalation | Approved before use |
| `ReviewCampaign` | One execution and reconciliation totals | Immutable scope snapshot after launch |
| `ReviewItem` | One identity/grant/reviewer decision | Unique per campaign and grant |
| `EntitlementSnapshot` | Evidence of access at launch | Append-only and digest protected |
| `ReviewDecision` | Retain, revoke or request evidence | Actor, reason, time and policy version required |

State: `Draft -> PendingApproval -> Scheduled -> Running -> Completed`; items: `Pending -> EvidenceRequested -> Retained/Revoked`, with `Overdue` as an escalated non-terminal state.

Invariants: campaign completion accounts for every item; snapshot changes never rewrite history; retain does not bypass permission expiry; revoke rechecks the current grant and is idempotent; current authorisation always wins over stale campaign data.

## 6. Use cases

- **UC-01 Create/launch campaign:** select approved scope, preview counts, obtain approval, snapshot entitlements and assign reviewers atomically per batch.
- **UC-02 Decide item:** validate reviewer/current scope, collect reason/evidence, record decision and issue revocation if chosen.
- **UC-03 Escalate overdue work:** scheduled job applies the campaign policy exactly once per escalation level.
- **UC-04 Close campaign:** reconcile totals and block closure while unresolved items remain unless an approved exception exists.

## 7. API contracts

| Method | Route | Permission | Idempotency |
| --- | --- | --- | --- |
| POST | `/api/v1/access-review/campaigns` | `identity.access_review.manage` | Request key |
| POST | `/api/v1/access-review/campaigns/{id}/launch` | `identity.access_review.manage` | Campaign/version |
| GET | `/api/v1/access-review/items` | `identity.access_review.decide` | Query |
| POST | `/api/v1/access-review/items/{id}/decisions` | `identity.access_review.decide` | Item/decision key |
| POST | `/api/v1/access-review/items/{id}/evidence` | `identity.access_review.evidence.submit` | Evidence digest |
| GET | `/api/v1/access-review/campaigns/{id}/report` | `identity.access_review.audit` | Query |

Return stable errors for stale snapshot, self-review, conflict, expired assignment, missing evidence and already-decided item. All lists are paginated.

## 8. Event contracts

Produced: `AccessReviewCampaignLaunched`, `AccessReviewEvidenceRequested`, `AccessReviewDecisionRecorded`, `AccessGrantRevoked`, `AccessReviewItemOverdue`, `AccessReviewCampaignCompleted`. Partition by institution and campaign/item. Use outbox; consumers deduplicate by event ID. Revocation events carry grant references, not credentials or sensitive evidence.

## 9. Database design

Database `identity_access_db`; schemas `access` and `integration`; migration assembly owned by Identity and Access.

Tables: `access.review_campaign_definitions`, `review_campaign_versions`, `review_campaigns`, `review_items`, `entitlement_snapshots`, `review_decisions`, `review_evidence_references`, `review_escalations`. Tenant IDs are mandatory; campaign/version and campaign/grant are unique; decisions and snapshots are append-only; concurrency tokens protect item transitions.

Migrations are additive. Existing grants are snapshotted only by an approved campaign job; rollback never deletes decisions or revocation evidence.

## 10. Consistency, concurrency and idempotency

Campaign/item updates and outbox messages share local transactions. Optimistic concurrency prevents double decisions. Revocation uses `(campaignId, itemId, grantId)` and returns the prior outcome on retry. No distributed transaction is required: unresolved revocation delivery remains visible and retried until current IAM state confirms the result.

## 11. Security and privacy

Use step-up, least privilege, anti-enumeration, bounded exports and sensitive-access audit. Evidence is stored by reference under document classification controls. Test cross-tenant access, self-review, stale reviewer assignment, privilege escalation, mass assignment and evidence leakage.

## 12. Audit and observability

Audit campaign policy/version, scope, launch approval, reviewer assignment, decisions, reasons, evidence digests, revocations, exceptions and exports. Metrics: pending/overdue age, decisions by outcome, revocation latency and invalidation failures. Alert on overdue critical access and failed revocation. Never log token/grant secrets or evidence content.

## 13. Failure, retry and recovery

Snapshot failure leaves campaign unlaunched. Partial batched creation resumes by campaign/scope key. Revocation failure leaves item `RevocationPending`, blocks campaign closure and alerts. Restore/replay must not recreate grants or duplicate decisions.

## 14. Configuration

Version and approve scope rules, cadence, due dates, escalation, privileged classifications, reviewer assignment and evidence requirements. Do not hard-code them in role names.

## 15. Implementation slices

1. Contracts, permissions and failing tests.
2. Campaign/snapshot persistence and preview.
3. Launch, assignment and item decisions.
4. Revocation/session invalidation and outbox.
5. Escalation, reporting, audit, dashboards and runbooks.
6. End-to-end review/revoke evidence.

## 16. Test plan

| ID | Scenario | Expected |
| --- | --- | --- |
| PAR-01 | Launch approved campaign | Complete unique snapshot/items |
| PAR-02 | Reviewer decides own grant | Denied and audited |
| PAR-03 | Concurrent decisions | One terminal decision |
| PAR-04 | Revoke retry | One revocation outcome |
| PAR-05 | Temporary access expires during review | Current denial preserved |
| PAR-06 | Overdue job replays | One escalation per level |
| PAR-07 | Cross-tenant item lookup | Non-disclosing denial |
| PAR-08 | Revocation delivery fails | Pending state, retry and alert |

## 17. Acceptance criteria

- [ ] Campaigns are scheduled and scoped through approved definitions.
- [ ] Owners can retain, revoke or request evidence.
- [ ] Overdue reviews escalate and report accurately.
- [ ] Self-review, stale scope and cross-tenant access are denied.
- [ ] Decisions, revocations and exports have immutable evidence.

## 18. Definition of Done

Repository DoD plus reviewed permissions, migrations, contracts, campaign reconciliation, entitlement invalidation, dashboards and runbook. Document completion does not mean implementation is complete.

## 19. Open decisions

Campaign cadence/scope, privileged classification, reviewer source, revocation SLA, session invalidation, exception authority, retention and HR/PAM integration remain owner-approved decisions.

## 20. Prohibited assumptions

Do not infer reviewer from manager fields, auto-retain missing decisions, restore expired access, include credentials in snapshots, or let campaign state override current authorisation.

## 21. Agent implementation instructions

Read Phase 2 overview, Phase 1 IAM/audit epics and approved IAM ADRs. Change only Identity and Access contracts/domain/persistence/API/tests plus documented consumers. Run build, unit/integration/architecture/contract/security tests and report actual results. Stop on an unresolved invalidation, reviewer or retention decision.

