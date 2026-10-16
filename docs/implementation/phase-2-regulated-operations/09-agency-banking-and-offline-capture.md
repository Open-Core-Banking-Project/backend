---
phase: "Phase 2 - Regulated Operations"
epic: "Agency Banking and Offline Capture"
owning_service: "Channels and Engagement, coordinated with Identity, Core Banking, Payments and Risk"
modules: [Agent Network, Agent Devices, Float, Commissions, Offline Capture, Synchronisation]
status: Draft
requirements: [BRN-004, BRN-005, BRN-006, IAM-001..IAM-004, NFR-RES-001..NFR-RES-002]
stories: [US-057, US-058]
dependencies: [Epic 06 Risk, Epic 07 Reconciliation, Epic 08 Branch Teller Cash, Phase 1 Channels and Payments]
adrs: [Agency module/data ownership, Agent identity/device trust, Offline cryptography and conflict resolution]
last_updated: 2026-10-16
---

# Epic 09: Agency Banking and Offline Capture

## 1. Objective

Onboard and control agents, devices, locations, limits, float, commissions and settlement, and optionally capture a strictly bounded set of offline requests that synchronise without duplicate or unsafe financial outcomes.

## 2. Success measures

- Agent KYC, approval, status, assigned device/location and permissions are validated per request.
- Float/liquidity/commission/settlement views reconcile to ledger and provider evidence.
- Offline envelopes are device-bound, cryptographically protected, sequenced, expiring and limited.
- Synchronisation deduplicates requests; stale/high-risk/conflicting requests require online review.

## 3. Scope

Included: agent profile references, approval, outlets, devices, locations, services/limits, float view, commission rules, settlement, offline capture/sync and field support evidence. Offline capability is Could and disabled by default. Excluded: custom cryptography, unapproved offline cash posting and direct balance ownership.

## 4. Actors, roles and permissions

| Actor | Permission | Scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Agency officer | `agency.agent.manage` | Institution/region/agent | Activate/suspend | Maker-checker |
| Field agent | `agency.transaction.initiate` | Own agent/device/location/services/limits | Risk based | Per transaction policy |
| Agency supervisor | `agency.exception.resolve` | Region/assigned cases | Required | Controlled action |
| Sync identity | `agency.offline.sync` | Registered device/batch | Device proof | None |

## 5. Domain model

Concepts: `Agent`, `AgentOutlet`, `AgentDeviceBinding`, `LocationPolicy`, `AgentServiceEntitlement`, `FloatPositionReference`, `CommissionRuleVersion`, `SettlementCycle`, `OfflineEnvelope`, `OfflineBatch`, `SyncDecision`.

States: agent `Draft -> KycPending -> ApprovalPending -> Active -> Suspended/Closed`; device `Pending -> Bound -> Active -> Revoked`; offline request `Captured -> Submitted -> Validating -> Accepted/ReviewRequired/Expired/Rejected -> Posted/Failed/Uncertain`.

Invariants: agent/device/location/status checked online before posting; offline envelope cannot prove success; request identity is globally stable; limits cannot be exceeded by queued requests; Core Banking alone changes balances; commission/float are explicit ledger-backed components; revoked devices cannot sync new work.

## 6. Use cases

- Register, KYC, approve, activate/suspend agent and outlet.
- Bind/revoke device and configure service/location/limits.
- Display reconciled float/liquidity and settlement state.
- Initiate online agent transaction through owning domain service.
- Capture allowed offline request and synchronise authenticated batch.
- Review stale/high-risk/conflicting request and reconcile commission/settlement.

## 7. API contracts

Routes: `/api/v1/agents`, `/agents/{id}/decisions`, `/agents/{id}/devices`, `/agents/{id}/services`, `/agents/{id}/float`, `/agent-transactions`, `/agent-offline/batches`, `/agent-offline/requests/{id}`, `/agent-settlements`. Financial initiation returns explicit pending/uncertain status and audit/receipt reference; every offline request has device-generated stable ID, sequence, captured time and expiry.

## 8. Event contracts

Produced: `AgentApproved/Activated/Suspended`, `AgentDeviceBound/Revoked`, `OfflineRequestCaptured/Accepted/ReviewRequired/Expired`, `AgentTransactionCompleted`, `AgentCommissionAccrued`, `AgentSettlementCalculated`. Consume KYC/IAM/risk/payment/ledger/reconciliation outcomes. Partition by agent/device/request; signed replay protection and inbox/outbox required.

## 9. Database design

Agency ownership must be confirmed by ADR. Channels may own channel/outlet/device/offline state; Identity owns credentials; Core owns financial positions; Payments owns payment instructions; Risk owns decisions. Candidate channel tables: agents/outlets refs, device bindings refs, service entitlements, offline envelopes/batches/sync decisions. Store ciphertext/evidence under approved retention; never private keys. No cross-service foreign keys or copied authoritative balances.

## 10. Consistency, concurrency and idempotency

Online/offline requests converge through canonical owning-service commands with stable idempotency. Device sequence and nonce prevent replay but do not replace server idempotency. Atomically record sync decision/outbox locally. Reserve or bound offline exposure using an approved model; never infer available balance while disconnected. Reconciliation resolves uncertain state.

## 11. Security and privacy

Use approved device keys/attestation, secure storage, revocation, signed/encrypted envelopes, nonce/sequence/expiry and server verification. Enforce geolocation only under approved privacy/lawful basis. Threat-model cloned device, rollback, forged time/location, batch replay, agent collusion and lost device. Never implement custom cryptography.

## 12. Audit and observability

Audit KYC/approval, status, device binding/revocation, location/limit/commission changes, offline capture/sync decisions, reviews and settlement. Metrics: active agents/devices, float alerts, offline queue age/count, expired/replayed/conflicting requests, risk holds and settlement breaks. Do not log envelope plaintext or precise location unnecessarily.

## 13. Failure, retry and recovery

Lost/revoked device blocks new sync. Partial batch retry returns per-request prior outcome. Clock/sequence conflict routes to review. Owning-service timeout remains uncertain. Queue capacity/age invokes fail-closed limits. Recovery includes replay once, reconciliation and customer-status correction.

## 14. Configuration

Approve agent types, services, KYC, device trust, locations, limits, velocity, commission, float/liquidity, settlement, offline allow-list, envelope algorithm, key rotation, expiry, queue and conflict policy.

## 15. Implementation slices

1. Ownership/device/offline ADRs and permissions.
2. Agent/outlet onboarding and KYC approval.
3. Device/location/service/limit controls.
4. Online transaction, float/commission/settlement views.
5. Offline envelope and sync validation behind disabled flag.
6. Review/reconciliation, threat tests, dashboards and runbooks.

## 16. Test plan

| ID | Scenario | Expected |
| --- | --- | --- |
| AOF-01 | Agent KYC/approval | Active only after evidence/approval |
| AOF-02 | Wrong/revoked device/location | Denied/audited |
| AOF-03 | Online transaction retry | One financial outcome |
| AOF-04 | Offline envelope tampered/replayed | Rejected |
| AOF-05 | Expired/stale/high-risk offline request | Review/reject; no posting |
| AOF-06 | Partial batch retry | Stable per-request outcomes |
| AOF-07 | Float/commission settlement | Reconciles to ledger |
| AOF-08 | Cross-agent/tenant access | Denied |
| AOF-09 | Lost device | Revocation and bounded exposure |

## 17. Acceptance criteria

- [ ] Agent KYC, approval, device, status, location and limits are enforced.
- [ ] Float, liquidity, commission and settlement positions are visible and reconciled.
- [ ] Offline requests are cryptographically protected, bounded by approved offline limits, expiring and deduplicated.
- [ ] Stale/high-risk requests require online review before posting.

## 18. Definition of Done

Repository DoD plus approved agency/offline ADRs, cryptographic review, device/provider contracts, migration/replay/security tests, settlement reconciliation, dashboards and lost-device/offline-recovery runbooks.

## 19. Open decisions

Module/data ownership, agent KYC, device platform/attestation, lawful location use, allowed transactions, offline exposure/limits, cryptography/key lifecycle, time/sequence, commission/float/settlement and conflict policy remain open.

## 20. Prohibited assumptions

Do not enable offline by default, invent crypto, trust device time/location, treat phone identity as authentication, show cached float as authoritative, or post stale/duplicate requests automatically.

## 21. Agent implementation instructions

Read channel/IAM/payment/ledger/risk/reconciliation epics and security ADRs. Keep each authoritative datum in its owning service. Run crypto/device, replay, offline-limit, cross-tenant, contract, reconciliation, recovery and E2E tests. Stop before offline implementation without approved threat model/ADR.

