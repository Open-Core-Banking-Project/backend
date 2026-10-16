---
phase: "Phase 3 - Growth Channels"
epic: "Partner APIs and Sandbox"
owning_service: "Integration Hub"
modules: [API Catalogue, Partner Clients, Gateway Policy, Request Signing, Sandbox, Version Deprecation]
status: Draft
requirements: [API-001..API-008, IAM-011, SEC-001, SEC-007, SEC-011, SEC-012, SEC-016, SEC-018]
stories: [US-067]
dependencies: [Phase 1 Integration Hub Release Readiness, Enabled owner-service contracts]
adrs: [Partner authentication/signing, API lifecycle/deprecation, Sandbox isolation]
last_updated: 2026-10-16
---

# Epic 08: Partner APIs and Sandbox

## 1. Objective

Expose documented, versioned and rate-limited partner APIs through controlled gateways with approved authentication/scopes, optional request signing, correlation/idempotency, sandbox isolation and governed breaking-change deprecation.

## 2. Success measures

- Every endpoint maps to an approved owner-service contract and least-privilege scope.
- Authentication, signing, quotas, validation and audit are consistently enforced.
- Breaking changes create a new version with communicated deprecation/sunset evidence.
- Sandbox uses synthetic/masked data and non-production credentials with no production reachability.

## 3. Scope

Included: catalogue, OpenAPI, client onboarding/certification, scopes, auth/signing, gateway routing, quotas, errors, correlation/idempotency, sandbox, compatibility/deprecation and operational evidence. Excluded: exposing databases/internal models, guaranteeing undocumented behavior and partner business features not approved by owner services.

## 4. Actors, roles and permissions

Partner developer uses granted client scopes; integration admin `partner.client.manage`; API product owner `partner.api.publish`; security approver `partner.client.certify`; operations `partner.api.support`. Production activation and breaking-version publication use maker-checker/step-up.

## 5. Domain model

Concepts: `ApiProduct`, `ApiVersion`, `OperationScope`, `PartnerClient`, `Subscription`, `RatePlan`, `SigningPolicy`, `CertificationRun`, `DeprecationNotice`, `SandboxTenant`. States API `Draft -> Review -> Published -> Deprecated -> Retired`; client `Draft -> Sandbox -> Certification -> ProductionActive -> Suspended/Revoked`.

Invariants: published contract immutable; breaking change requires new version; client scope subset subscription; environment isolation; correlation always present; financial commands require idempotency; gateway never supplies missing business authority; sunset follows approved notice/support policy.

## 6. Use cases

Publish/version API; onboard client; grant scope/quota/network/signing; use sandbox; certify; activate production; rotate/revoke credentials; monitor usage; publish deprecation and retire after gates.

## 7. API contracts

Partner routes are generated/documented from approved OpenAPI and use RFC 7807 stable errors. Management routes: `/api-products`, `/api-versions`, `/partner-clients`, `/subscriptions`, `/certification-runs`, `/deprecations`. Enforce HTTPS, auth, schema validation, quotas, correlation and idempotency at gateway and owner service.

## 8. Event contracts

Produce `PartnerClientCertified/Activated/Suspended`, `ApiVersionPublished/Deprecated/Retired`, `PartnerQuotaExceeded`. Business events remain owned by domain services. Integration events include no credential/secret and use outbox/inbox where application state changes.

## 9. Database design

`integration_hub_db`, schemas `partner`, `integration`: API products/versions/operations/scopes, clients, subscriptions, rate/network/signing policy refs, certification runs, deprecation notices and sandbox refs. Contracts/artifacts stored by digest. Secret values remain in secret manager. Unique version/client/subscription and environment separation required.

## 10. Consistency, concurrency and idempotency

Management state/outbox local transactions. Gateway retries only safe/idempotent operations. Owner service remains final idempotency/authorization boundary. Quota coordination may be eventually consistent within approved bounds but cannot grant scope. Version changes use optimistic concurrency.

## 11. Security and privacy

OAuth/client credentials, mTLS or signing as approved; timestamp/nonce canonicalization; scope/resource checks, quotas, network restrictions, schema/mass-assignment defense and abuse monitoring. Sandbox cannot resolve production secrets/endpoints/data. Test auth downgrade, replay, scope confusion, BOLA/IDOR, injection and tenant crossover.

## 12. Audit and observability

Audit publication, scope/subscription, credential refs, certification, activation, suspension, deprecation and sensitive operations. Metrics: traffic/latency/errors by safe dimensions, auth/signature failures, quotas, version adoption and deprecation readiness. Never log tokens/secrets/full payloads.

## 13. Failure, retry and recovery

Gateway/owner timeout returns explicit status; financial retry requires same idempotency. Certification/activation fails closed on dependency/security evidence gaps. Restore preserves contract digests, clients, scopes, environment and deprecation state.

## 14. Configuration

Approve API catalogue, auth/signing, scopes, quotas, networks, versioning/deprecation/sunset, sandbox fixtures, certification tests, support/SLA and retention.

## 15. Implementation slices

1. API/auth/version ADRs. 2. Catalogue/OpenAPI/scopes. 3. Client/subscription/quota. 4. Sandbox isolation. 5. Certification/activation. 6. Deprecation/security/load/operations.

## 16. Test plan

| ID | Scenario | Expected |
| --- | --- | --- |
| PAS-01 | Valid scoped request | Owner service reauthorizes |
| PAS-02 | Missing/wrong scope/tenant | Denied |
| PAS-03 | Signed replay/tamper | Rejected |
| PAS-04 | Quota exceeded | Stable rate-limit response |
| PAS-05 | Financial retry | One outcome |
| PAS-06 | Breaking change same version | Contract gate fails |
| PAS-07 | Sandbox reaches production | Network/identity denial |
| PAS-08 | Deprecation/retirement | Notice/adoption/support gates enforced |

## 17. Acceptance criteria

- [ ] APIs are documented, versioned and rate-limited.
- [ ] Approved authentication, scopes and optional signing are enforced.
- [ ] Breaking changes use new versions and deprecation notice.
- [ ] Correlation/idempotency are required where applicable.
- [ ] Sandbox isolation and certification are verified.

## 18. Definition of Done

Repository DoD plus approved lifecycle/auth/sandbox ADRs, OpenAPI compatibility, secret-manager/gateway configuration, certification, security/load/idempotency tests, dashboards and support runbooks.

## 19. Open decisions

Catalogue, auth/signing, scopes, quotas, IP/network, version window, deprecation/sunset, sandbox topology/data, certification and partner SLA/support remain open.

## 20. Prohibited assumptions

Do not expose private tables/models, trust gateway authorization alone, reuse production credentials/data in sandbox, make silent breaking changes or retry ambiguous financial commands with new keys.

## 21. Agent implementation instructions

Read owner-service contracts and Integration/IAM/security ADRs. Keep APIs thin and domain authority in owner services. Run OpenAPI compatibility, auth/signing, tenant/scope, sandbox isolation, rate/load, idempotency and recovery tests. Stop for unresolved auth/version/sandbox policy.

