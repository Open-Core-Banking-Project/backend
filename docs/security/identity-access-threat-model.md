---
title: "Identity and Access threat model"
status: Baseline
owners: [Architecture, Security, Identity and Access]
created: 2026-10-26
last_reviewed: 2026-10-26
requirements: [IAM-001, IAM-002, IAM-003, IAM-004, IAM-005, IAM-006, IAM-007, IAM-008, IAM-009, IAM-010, IAM-011, SEC-002, SEC-003, SEC-004, SEC-005, SEC-007, SEC-008, SEC-009, SEC-010, SEC-011, SEC-013, SEC-016, SEC-017, AUD-001, AUD-002, AUD-003]
stories: [US-005, US-006, US-007, US-008, US-009]
adrs: [ADR-0008, ADR-0009, ADR-0010, ADR-0011, ADR-0012]
---

# Identity and Access threat model

## 1. Purpose and status

This document is the Slice 1 security baseline for Phase 1, Epic 02. It identifies the assets, actors, trust boundaries, threats, mandatory controls and verification evidence for authentication, sessions, service identities, roles, contextual permissions and entitlement invalidation.

The model is implementation-directing but is not production approval. A channel, provider realm or service may be enabled only after its open production gates in section 12 are approved and its controls are demonstrated. Unsupported or unconfigured flows fail closed.

## 2. Scope

Included:

- Customer, staff, agent, corporate-user, administrator and service authentication boundaries.
- Keycloak as the selected OpenID Connect provider behind standards-based ports.
- Access and refresh token handling, sessions, step-up evidence and device binding.
- Permission catalogue, role versions, grants, contextual scopes and conflict-of-duty decisions.
- Entitlement snapshot delivery, local non-authoritative caching, invalidation and recovery from gaps.
- Browser, native-mobile, USSD, WhatsApp/chatbot and service-to-service protocol boundaries.
- Authentication, authorisation and privileged-action audit evidence.

Deferred to later slices but constrained here:

- Factor enrolment and recovery mechanics.
- Trusted-device key protocol and privacy fields.
- Selected-field payload encryption and KMS/HSM operations.
- Final realm topology, production hosting, availability and operational ownership.
- Partner and corporate delegated identity topology.

## 3. Security objectives

1. Prove the actor, institution, actor type, session and intended audience before processing a protected operation.
2. Prevent one institution, channel, device, human or service identity from inheriting another's access.
3. Deny when permission, contextual scope, authentication strength or entitlement freshness cannot be proven.
4. Make session, device and entitlement revocation effective within the accepted window, with fresh resolution for privileged and financial operations.
5. Prevent a maker from approving their own restricted action across roles, sessions and channels.
6. Keep passwords, OTPs, refresh tokens, bearer tokens, private keys and recoverable secrets out of application storage, logs, traces, events and client responses.
7. Produce immutable, correlated and privacy-minimised evidence for sensitive decisions and changes.

## 4. Assets and impact

| Asset | Security need | Failure impact |
| --- | --- | --- |
| Credential and factor material | Confidentiality, non-replay | Account takeover and fraud |
| Access/refresh tokens and session families | Confidentiality, integrity, revocability | Impersonation and persistent access |
| Provider subject and identity links | Integrity, tenant binding | Identity substitution or cross-tenant access |
| Service credentials/certificates | Confidentiality, audience restriction, rotation | Lateral service compromise |
| Roles, grants, scopes and conflicts | Integrity, versioning, approval | Privilege escalation and segregation failure |
| Entitlement versions and snapshots | Integrity, freshness, availability | Stale allow or platform denial |
| Device and step-up evidence | Integrity, purpose/session binding | Strong-authentication bypass |
| Audit and security evidence | Integrity, availability, minimisation | Lost accountability or data leakage |
| Public/private key metadata | Authenticity, purpose/version binding | Decryption abuse or secret disclosure |

## 5. Actors and attacker capabilities

- Legitimate customer, staff, agent, corporate user, administrator or service acting within assigned authority.
- External attacker with stolen credentials, a token, a device, a channel account or control of an untrusted network.
- Malicious or compromised tenant user attempting horizontal or vertical privilege escalation.
- Privileged insider attempting unauthorised grants, self-approval, evidence suppression or cross-tenant access.
- Compromised channel/provider adapter attempting replay, identity spoofing or over-broad API use.
- Compromised workload attempting audience confusion or use of human permissions.
- Operator error, delayed events, restore, cache failure or provider outage producing stale or ambiguous security state.

The model does not assume the browser, mobile device, gateway, message broker, cache, network, client-supplied claims or channel provider is trusted to authorise a banking operation.

## 6. Trust boundaries and data flows

| Boundary | Data crossing | Required validation |
| --- | --- | --- |
| Interactive client to identity provider | Credentials, challenge state, authorisation response | Approved OIDC flow, PKCE, exact redirect URI, state/nonce, TLS, anti-enumeration |
| Client or BFF to service API | Access token/session cookie, correlation and request data | Issuer, audience, signature, time, session, actor type, institution, CSRF where applicable |
| API to Identity and Access snapshot endpoint | Actor/institution/version query and entitlement snapshot | Mutual workload authentication, exact audience, tenant match, bounded response, freshness |
| Identity and Access to broker | Versioned invalidation/security event | Transactional outbox, schema version, institution, monotonic version, no secrets |
| Consumer from broker/cache | Invalidation event or cached snapshot | Inbox deduplication, sequence/gap detection, no version regression, TTL |
| Channel provider to channel adapter | Provider identity, session/event reference, user input | Provider authentication/signature, replay defence, verified binding, allow-listed intent |
| Service to service | Audience-scoped service token and request | Service actor type, issuer/audience/environment, explicit service permission |
| Service to audit/SIEM | Decision and security evidence | Redaction, correlation, append-only source evidence, durable delivery |

Authoritative decisions remain inside the owning service. A gateway may reject early but cannot create a trusted tenant, permission, scope or final authorisation decision.

## 7. Channel constraints

| Channel | Approved protocol boundary | Token/storage constraint | Fail-closed condition |
| --- | --- | --- | --- |
| Customer/staff browser | OIDC authorisation code with PKCE | Channel review selects protected server session/BFF or another approved browser design; tokens are not placed in URLs or ordinary persistent web storage | Missing state/nonce, CSRF control, issuer/audience validation or approved storage design |
| Native mobile | System-browser OIDC authorisation code with PKCE | Refresh material only in OS-protected storage; no embedded credential collection | Compromised/unsupported device policy, redirect mismatch or unavailable secure storage |
| USSD | Provider-authenticated adapter plus verified customer binding | Provider receives no platform bearer/refresh token; short-lived server-side session is non-authoritative | Unverified binding, replay, unsupported sensitive intent or missing step-up |
| WhatsApp/chatbot | Signed/authenticated provider event plus verified binding | Channel identity is an attribute, not a credential; tools are allow-listed and service-authenticated | Signature/replay/binding failure or generated/free-form instruction attempting privileged access |
| Service workload | OAuth client credentials | Credential or certificate reference is protected and rotated; token is audience/environment scoped | Human role/permission, wrong audience/environment, expired credential or unknown client |

## 8. STRIDE analysis and controls

| ID | Category | Threat or abuse case | Mandatory controls | Verification evidence |
| --- | --- | --- | --- | --- |
| IAM-T01 | Spoofing | Credential stuffing or user enumeration | Provider rate limits, configurable lock/challenge policy, generic errors, safe security telemetry | Invalid-credential and enumeration tests; no identity disclosure |
| IAM-T02 | Spoofing | Forged, wrong-issuer, wrong-audience or cross-environment token | Framework signature/time/issuer/audience validation; environment-specific clients | Token-negative contract tests |
| IAM-T03 | Spoofing | Client-supplied institution or actor claim substitutes trusted membership | Resolve actor and membership from validated identity link; compare tenant at API and persistence boundaries | Cross-tenant negative tests with identical resource references |
| IAM-T04 | Spoofing | Service token used as human or human token used for service action | Explicit actor type and separate permission namespaces; deny and audit mismatch | Human/service interchange tests |
| IAM-T05 | Tampering | Role, approval state, institution or grant scope mass-assigned | Explicit contract mapping, immutable published versions, maker-checker transition methods | IAM-029 and contract-shape tests |
| IAM-T06 | Tampering | Entitlement event reordered, duplicated or version rolled back | Transactional outbox/inbox, monotonic version, idempotent apply, reject regression | Duplicate/out-of-order/gap tests |
| IAM-T07 | Tampering | Cache entry altered or snapshot returned for another tenant | Authenticated source call, actor/institution key, response validation, non-authoritative cache | Tenant/key mismatch and corrupt snapshot tests |
| IAM-T08 | Repudiation | Privileged actor denies changing a role, grant, session or device | Append-only source evidence with actor, institution, action, reason, correlation, before/after/version | Immutability and required-field tests |
| IAM-T09 | Repudiation | Maker approves own action from another role/session | Compare stable actor identifier, not role/session; Workflow repeats the check | IAM-012 across roles, sessions and channels |
| IAM-T10 | Information disclosure | Password, OTP, token, secret, decrypted field or sensitive scope leaks | Allow-listed logging, telemetry redaction, bounded contracts, no unrestricted request bodies | Repository/log/event/error leakage scans |
| IAM-T11 | Information disclosure | Token leaks through URL, browser storage, referrer or crash report | Authorisation code flow, no token in URL, reviewed per-channel storage, redacted diagnostics | Browser/mobile security review and dynamic test |
| IAM-T12 | Denial of service | Provider, snapshot source or cache unavailable | Circuit breaking, bounded retry where safe, readiness signal, privileged/financial deny, no permissive fallback | Outage and stale-cache tests |
| IAM-T13 | Denial of service | Invalidation flood or event gap prevents safe access | Partition/key by affected identity, deduplication, gap detection, signed/current snapshot resync, backlog metrics | Replay, gap and recovery test |
| IAM-T14 | Elevation of privilege | Missing permission or unresolved scope defaults to allow | Every protected operation declares capability; unknown/missing scope denies; architecture checks | Missing permission and wrong-scope tests |
| IAM-T15 | Elevation of privilege | Multiple roles combine into prohibited authority | Evaluate effective union plus configured conflicts before activation under concurrency | IAM-010/IAM-011 concurrency tests |
| IAM-T16 | Elevation of privilege | Stale token/snapshot retains revoked access | Entitlement version compare, bounded TTL, invalidation event, fresh source for privileged/financial actions | IAM-019/IAM-020 and revocation-latency evidence |
| IAM-T17 | Elevation of privilege | Step-up evidence replayed for another action, session or device | Short lifetime; bind actor, session, purpose and device; single-use where required | Wrong-purpose/session/device and replay tests |
| IAM-T18 | Elevation of privilege | Channel identity or generated chatbot output invokes privileged API | Verified binding, allow-listed intents/tools, service permissions, confirmation and step-up | Spoofed channel and unrestricted-tool tests |
| IAM-T19 | Elevation of privilege | Refresh-token replay preserves attacker access | Rotation with reuse detection; revoke family/session and emit security event | Concurrent refresh/reuse test |
| IAM-T20 | Elevation of privilege | Restore reintroduces revoked grants or sessions | Keep traffic closed; reconcile provider/local state; rebuild versions without regression | IAM-028 recovery exercise |

## 9. Entitlement failure state machine

An authorisation request is eligible for evaluation only when all of these are true:

1. The validated token actor, actor type, institution, audience and session are internally consistent.
2. The snapshot source and consumer agree on actor, institution and monotonic entitlement version.
3. The snapshot is complete and inside the allowed freshness window.
4. The required stable permission exists and every applicable scope is resolvable.
5. Authentication strength, device and step-up evidence meet the operation policy.
6. Conflict-of-duty and maker identity checks pass.

Missing, stale, mismatched, incomplete or gapped state transitions to `Denied`. A consumer may resynchronise from the versioned source contract, but it must not reuse an earlier allow while resynchronisation is pending. Duplicate invalidation is a no-op; lower versions are rejected; a higher unexpected version triggers a full refresh.

## 10. Security evidence and privacy

Sensitive authentication and authorisation evidence records the safe category, stable actor reference when known, actor type, institution, session/device reference where applicable, permission, evaluated scope summary, entitlement version, decision/reason, authentication strength, channel, correlation and timestamp.

Evidence excludes passwords, PINs, OTPs, recovery codes, bearer/refresh tokens, private keys, raw assertions, unrestricted device signals, full request bodies and unnecessary personal data. Unknown-user failures use a non-reversible safe reference only where approved. Ordinary administrators cannot edit or delete source evidence.

## 11. Verification plan

Slice 1 establishes the following executable targets for later implementation slices:

- Token validation: forged, expired, not-yet-valid, wrong issuer/audience/environment and cross-institution denial.
- Actor separation: every human type remains explicit; service and human grants cannot be exchanged.
- Entitlement safety: missing, stale, future, regressed, duplicated, gapped and cross-tenant snapshot/event cases deny or resynchronise safely.
- Scope safety: branch, channel, resource, amount, currency, device and authentication-strength failure never invokes the business action.
- Segregation: self-approval across roles/sessions/channels and concurrent conflicting grants are denied and audited.
- Session security: concurrent refresh reuse, revoked/expired session and provider-revocation outage retain local denial.
- Channel security: redirect/state/nonce/CSRF failures, token leakage checks, channel replay/spoofing and unrestricted chatbot tool invocation.
- Evidence safety: no credential, token, private key or decrypted field in API, database, log, trace, event or error captures.
- Recovery: restored grants, sessions and versions remain unavailable until reconciliation proves non-regression.

Tests map to IAM-001 through IAM-030 in the epic test plan. Performance thresholds and attack rates remain configuration evidence and must not be invented in test code.

## 12. Production approval gates and residual risk

| Gate | Owner | Safe state until approved |
| --- | --- | --- |
| Keycloak hosting topology, support and operational ownership | Architecture, Security, Platform | Non-production adapter only; production authentication disabled |
| Realm/client configuration and exact redirect/audience inventory | Security, Channel owners | Unregistered channel/client denied |
| Approved factors, recovery methods and assurance mapping | Security, Product | Flow requiring the factor remains unavailable |
| Browser/mobile token transport and storage implementation review | Security, Channel owner | Channel cannot retain refresh capability |
| Per-service entitlement cache lifetime and revocation alert threshold | Security, Risk, Service owner | Privileged/financial action uses fresh source; other unresolved paths deny |
| Initial role bundles and conflict rules | Security, Domain owners, Operations | No production grant publication |
| Partner/corporate delegated identity topology | Architecture, Security, Product | Partner/corporate authentication disabled |
| Authentication/authorisation SLOs, abuse profile and retention periods | Product, Platform, Compliance | No production-readiness claim |

Accepted residual risk is limited to the bounded ordinary-operation cache interval configured under ADR-0009. No residual stale-allow risk is accepted for privileged or financial operations, tenant mismatch, unresolved scope, entitlement gaps or restored security state.

## 13. Review triggers

Security and Identity and Access must review this model when any of the following changes: identity provider, token format or transport, browser/mobile architecture, factor or recovery method, session storage, service authentication, entitlement delivery/cache window, role/conflict semantics, device-key protocol, payload encryption, channel provider, new actor type, production topology, material incident or penetration-test finding.

## 14. References

- [ADR-0008](../adr/ADR-0008-authentication-provider-token-model-and-service-identity.md)
- [ADR-0009](../adr/ADR-0009-roles-permissions-contextual-scopes-and-invalidation.md)
- [ADR-0010](../adr/ADR-0010-observability-audit-evidence-redaction-and-retention.md)
- [ADR-0011](../adr/ADR-0011-secret-key-and-certificate-management.md)
- [ADR-0012](../adr/ADR-0012-channel-security-boundaries.md)
- [Phase 0 identity and permission foundation](../implementation/phase-0-engineering-foundation/03-identity-and-permission-foundation.md)
- [Phase 1 Identity, Roles and Permissions epic](../implementation/phase-1-banking-foundation/02-identity-roles-and-permissions.md)
