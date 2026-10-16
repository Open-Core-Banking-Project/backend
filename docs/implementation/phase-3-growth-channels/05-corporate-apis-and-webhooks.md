---
phase: "Phase 3 - Growth Channels"
epic: "Corporate APIs and Webhooks"
owning_service: "Integration Hub with Identity and Access"
modules: [Corporate API Clients, Credentials, Scopes, Certification, Webhook Delivery]
status: Draft
requirements: [CHN-006, API-001..API-008, SEC-001, SEC-007, SEC-012, SEC-016]
stories: [US-061]
dependencies: [Epic 01 Corporate Entitlements, Phase 1 Integration Hub and Providers]
adrs: [Corporate client authentication/signing, Webhook delivery, Certification]
last_updated: 2026-10-16
---

# Epic 05: Corporate APIs and Webhooks

## 1. Objective

Allow corporate technical administrators to manage scoped, rate-limited and rotatable API credentials and signed webhook endpoints, with delivery logs and approved certification before production activation.

## 2. Success measures

- Client scopes cannot exceed bank-approved corporate entitlements.
- Credentials rotate/revoke without secret disclosure; production and sandbox remain isolated.
- Webhook deliveries are signed, replay-safe, retried and observable with redacted logs.
- Production activation is impossible before required certification and approval pass.

## 3. Scope

Included: client registration, auth/signing method, credential reference, scopes, quotas, network restrictions, webhook subscriptions/secrets, delivery/attempt/dead-letter/replay, certification and activation. Excluded: partner-wide catalogue (Epic 8), business-domain ownership and plaintext secret retrieval.

## 4. Actors, roles and permissions

Corporate technical admin `corporate.integration.manage` within corporate ceiling; bank integration admin `integration.corporate.approve`; security approver `integration.client.certify`; client identity uses granted service scopes only. Credential reveal is one-time if permitted; production activation uses maker-checker/step-up.

## 5. Domain model

Concepts: `CorporateApiClient`, `CredentialReference/Version`, `ClientScopeGrant`, `RatePlan`, `NetworkPolicy`, `WebhookSubscription`, `WebhookSecretVersion`, `Delivery/Attempt`, `CertificationRun`, `Activation`. States client `Draft -> Certification -> Approved -> Active -> Suspended/Revoked`; delivery `Pending -> Attempting -> Delivered/Retrying/DeadLettered`.

Invariants: scopes subset bank/corporate grant; secrets never stored/logged in plaintext here; rotation overlaps only approved transition; production credentials cannot target sandbox or vice versa; signatures cover approved canonical data/timestamp/event ID; replay has no duplicate business effect.

## 6. Use cases

Register client; grant scopes/quotas/network rules; issue/rotate/revoke credential; register/verify endpoint; run certification; activate production; deliver/retry/dead-letter/replay webhook; inspect safe logs.

## 7. API contracts

Routes: `/corporates/{id}/api-clients`, `/api-clients/{id}/credentials`, `/api-clients/{id}/scopes`, `/webhook-subscriptions`, `/certification-runs`, `/activations`, `/webhook-deliveries`. Stable errors cover scope ceiling, uncertified client, invalid endpoint/network, rate limit and revoked credential.

## 8. Event contracts

Produce `CorporateApiClientActivated/Suspended`, `CredentialRotated/Revoked`, `WebhookSubscriptionActivated`, `WebhookDelivered/DeadLettered`, `CertificationCompleted`. Consume approved domain facts for outbound delivery. Use stable event/delivery IDs, signed payload version and minimum data.

## 9. Database design

`integration_hub_db`: `partner.api_clients`, scopes, network restrictions, certification runs, webhook subscriptions, outbound deliveries/attempts/dead letters; secret manager stores credentials/secrets. Unique tenant/corporate/client names, endpoint/event subscription and delivery/event IDs; append-only attempts/certification evidence.

## 10. Consistency, concurrency and idempotency

Client/subscription state and outbox commit locally. Credential operations use version concurrency. Delivery key is subscription/event/schema version; retry creates attempt, not business event. Replay reuses original event identity with explicit replay evidence.

## 11. Security and privacy

Approved OAuth client credentials/mTLS/request signing, SSRF-safe endpoint validation, egress allow-list, TLS, secret rotation, quotas and abuse detection. Test credential theft/reuse, scope escalation, signature canonicalization, replay, DNS rebinding, cross-corporate access and payload leakage.

## 12. Audit and observability

Audit client/scope/network/credential/subscription/certification/activation/replay changes. Metrics: auth failures, quota use, delivery latency/success/retry/dead-letter and credential/certificate expiry. Logs expose no secret or unrestricted payload.

## 13. Failure, retry and recovery

Endpoint timeout retries with bounded policy; delivered status only on approved acknowledgement. Poison delivery dead-letters. Secret-manager failure blocks issue/activation. Restore preserves client versions, delivery identity and certification evidence.

## 14. Configuration

Approve auth/signing, scopes, quotas, IP/network, secret/certificate lifetime, payload/event catalogue, retry/backoff, endpoint policy, certification cases and activation authority.

## 15. Implementation slices

1. Auth/scope ADR. 2. Client/scopes/credentials. 3. Subscriptions/signing. 4. Delivery/retry/dead letter. 5. Certification/activation. 6. Security/load/runbooks.

## 16. Test plan

| ID | Scenario | Expected |
| --- | --- | --- |
| CAW-01 | Scope beyond ceiling | Denied/audited |
| CAW-02 | Credential rotate/revoke | Old credential expires per policy |
| CAW-03 | Rate/network restriction | Enforced |
| CAW-04 | Signed delivery/replay | Valid signature; one consumer effect |
| CAW-05 | Endpoint timeout | Bounded retry/dead letter |
| CAW-06 | Uncertified production activation | Blocked |
| CAW-07 | SSRF/cross-corporate attack | Rejected |

## 17. Acceptance criteria

- [ ] Credentials are scoped, rate-limited, rotatable and environment isolated.
- [ ] Webhook signing secrets and safe delivery logs are provided.
- [ ] Delivery retry, dead-letter and replay are controlled.
- [ ] Production activation requires certification and approval.

## 18. Definition of Done

Repository DoD plus auth/webhook ADRs, secret-manager integration, migrations, certification evidence, security/load/replay tests, dashboard and runbook.

## 19. Open decisions

Auth/signing, scopes, quotas, IP policy, secret reveal/rotation, webhook catalogue/ack, retry, certification and production approval remain open.

## 20. Prohibited assumptions

Do not return stored secrets, grant broad wildcard scopes, trust corporate endpoint blindly, activate without certification, or include sensitive payloads in delivery logs.

## 21. Agent implementation instructions

Read IAM/Integration/Corporate mandate epics. Keep secrets in approved manager and business authority in owner services. Run auth, signing, SSRF, migration, rate, replay, contract, load and recovery tests. Stop for unresolved auth/certification policy.

