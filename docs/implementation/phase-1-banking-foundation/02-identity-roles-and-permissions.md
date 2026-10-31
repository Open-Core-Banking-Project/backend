---
phase: "Phase 1 - Banking Foundation"
epic: "Identity, Roles and Permissions"
owning_service: "Identity and Access"
participating_services:
  - Core Banking and Ledger
  - Operations and Workflow
  - Risk and Reporting
  - Integration Hub
  - Audit and Reconciliation
modules:
  - Identity Registry
  - Authentication and Sessions
  - Multi-Factor and Step-Up Authentication
  - Roles, Permissions and Contextual Scopes
  - Entitlement Distribution and Invalidation
  - Trusted Devices
  - Service Identities
  - Payload Encryption Contracts
status: In Progress
requirements: [IAM-001, IAM-002, IAM-003, IAM-004, IAM-005, IAM-006, IAM-007, IAM-008, IAM-009, IAM-010, IAM-011, SEC-002, SEC-003, SEC-004, SEC-005, SEC-007, SEC-008, SEC-009, SEC-010, SEC-011, SEC-013, SEC-016, SEC-017, AUD-001, AUD-002, AUD-003, NFR-OBS-001, NFR-OBS-002, BR-007, BR-008, BR-009]
stories: [US-005, US-006, US-007, US-008, US-009]
dependencies:
  - Phase 0 - Identity and Permission Foundation
  - Phase 0 - Messaging, Outbox, Inbox and Idempotency
  - Phase 0 - Observability and CI
  - Phase 1 - Institution and Tenancy
adrs: [ADR-0008, ADR-0009]
last_updated: 2026-10-26
---

# Epic: Identity, Roles and Permissions

## 1. Objective

Deliver the production-ready identity and authorisation capabilities required for customer, staff, agent, corporate, administrator and service access without coupling business behaviour to role names or trusting a frontend, gateway or token alone.

When this epic is complete, the platform can authenticate supported actor types, manage sessions and trusted devices, enforce MFA and step-up policy, administer versioned roles and contextual permissions, prevent prohibited role combinations and maker self-approval, invalidate changed entitlements within an approved window, and provide every business service with a deny-by-default authorisation contract.

This epic evolves the Phase 0 reference policy into production behaviour. Implementation must not begin on credential or token internals until the identity-provider, token and service-authentication ADRs are approved.

## 2. Success measures

- Every protected operation declares a stable capability permission and is denied when authentication, tenant, entitlement freshness or contextual scope cannot be proven.
- Customer, staff, agent, corporate, administrator and service identities remain explicitly typed and cannot exchange grant models.
- Password, MFA, lockout, token/session and step-up policies are configurable by institution and channel within approved security minima.
- Permission removal and device/session revocation become effective within the approved revocation objective and are measurable.
- A user may hold multiple roles only when conflict-of-duty rules allow the combined effective entitlements.
- Role, grant, privileged-access and high-risk authentication changes are approved where configured and produce immutable evidence.
- New trusted-device registration requires successful step-up authentication; revocation terminates all sessions bound to that device.
- Private keys, password material, OTPs, recovery secrets and bearer tokens never appear in frontend responses, application logs, traces, events or ordinary database columns.
- Automated evidence covers cross-tenant denial, missing and stale permission state, wrong scope, conflict-of-duty, maker self-approval, session revocation, device revocation and cryptographic key transition.

The launch team must approve measurable targets for sign-in latency, token validation latency, revocation propagation, authentication availability and security-event delivery before production readiness. Examples in this document are not approved service-level objectives.

## 3. Scope

### Included

- Platform identity records and links to an approved identity provider.
- Authentication for customer, staff, agent, corporate-user and administrator channels using approved provider capabilities.
- Separate short-lived service identities and service permissions.
- Configurable password, failed-attempt, lockout, MFA, session and channel policies subject to platform security floors.
- MFA enrolment, authentication challenges, step-up evidence and recovery controls supported by the approved provider.
- Session issue/reference, refresh, expiry, revocation and device binding.
- Trusted-device registration, listing, naming and revocation.
- Stable permission catalogue, configurable roles, role versions, role-permission assignments and identity-role grants.
- Institution, legal-entity, region, branch, channel, customer, account, product, mandate, amount, currency, device, network, risk and time scopes.
- Conflict-of-duty rules, temporary grants, grant expiry and access-review-ready records.
- Entitlement versioning, distribution, cache rules and invalidation events.
- Maker-checker eligibility and self-approval prevention shared with Operations and Workflow.
- Public-key discovery and encrypted-envelope contracts for selected frontend fields, plus one authentication-flow reference implementation.
- Immutable authentication, authorisation and privileged-access evidence.
- Security events suitable for central monitoring and incident response.

### Excluded

- Customer KYC and legal identity verification, owned by Customer and Onboarding.
- Approval request orchestration, queues, escalation and business workflow history, owned by Operations and Workflow.
- Transaction-risk scoring and fraud models, owned by Risk and Reporting.
- Institution key creation, custody, HSM/secret-manager administration and provider credentials, owned by institution configuration and Integration Hub/platform operations.
- Corporate mandates and signatory rules, delivered in the corporate-banking phase; this epic only supports mandate scope references.
- Full privileged-access review campaigns from US-010, scheduled for Phase 2. Phase 1 preserves the data and expiry controls needed by that capability.
- Transaction PIN lifecycle unless separately approved. A transaction PIN is not a password and must use a separate protection and verification policy.
- Frontend implementation. Frontends may reflect effective permissions for usability but never become an enforcement boundary.
- Selecting a commercial or open-source identity provider in this document.

## 4. Ownership and trust boundaries

| Capability | Authoritative owner | Required collaboration |
| --- | --- | --- |
| Identity type, institution membership and provider link | Identity and Access | Institution lifecycle from Core Banking and Ledger |
| Credential verification and protocol-specific token operations | Approved identity provider behind Identity and Access ports | Platform security and operations |
| Roles, permissions, scopes, conflicts, grants and entitlement versions | Identity and Access | Permission definitions reviewed with owning business service |
| Resource existence, ownership and business-specific scope evaluation | Owning business service | Uses trusted actor and entitlement context |
| Approval workflow state and approver selection | Operations and Workflow | Identity supplies eligible approvers and separation evidence |
| Maker self-approval and conflict-of-duty decision | Both Identity and Workflow at their boundaries | Both must deny independently where applicable |
| Device, login and transaction risk score | Risk and Reporting | Identity supplies minimal device/login signals |
| Trusted-device registry and session-device binding | Identity and Access | Risk consumes versioned events |
| Key custody, rotation and private-key operation | Approved KMS/HSM integration | Identity implements authentication-envelope decryption port only |
| Public-key metadata and discovery contract | Identity and Access for auth endpoints; owning service for other endpoints | Institution configuration publishes active key metadata |
| Audit evidence | Source service plus Audit and Reconciliation | Source commits evidence/outbox atomically |

Trust rules:

1. Authentication proves an actor and session; it does not prove permission for a business resource.
2. Identity and Access is authoritative for grants, but each business service authorises access to its own resources.
3. The gateway may authenticate and pre-screen, but every protected service repeats required validation.
4. Tenant identity is resolved from trusted authentication/service context and mapped membership, never accepted from the request body alone.
5. A token is signed input, not permanent truth. Entitlement freshness and revocation policy still apply.
6. Device trust is a risk signal and policy condition, never a substitute for identity or permission.

## 5. Actors, roles and permissions

Role names below are illustrative configurable bundles. Code checks the permission column, never the role name.

| Actor | Permission | Contextual scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Platform security administrator | `identity.permission.catalogue.manage` | Platform/approved namespaces | Required | Platform change approval |
| Institution security administrator | `identity.role.create` | Institution | Required | Maker-checker before publication |
| Institution security administrator | `identity.role.update` | Institution/role | Required | Maker-checker before publication |
| Security approver | `identity.role.approve` | Institution/role | Required | Cannot approve own change |
| Grant administrator | `identity.grant.assign` | Institution/identity/role/scope | Required for privileged grants | Approval by policy |
| Grant approver | `identity.grant.approve` | Institution/identity/role/scope | Required | Cannot approve own change |
| Session administrator | `identity.session.revoke` | Institution/identity/session | Required for another user | Reason required |
| Customer | `identity.session.self.manage` | Own identity/sessions | According to risk | None |
| Customer | `identity.device.self.manage` | Own identity/devices | Registration and broad revocation | None |
| Support officer | `identity.device.view` | Institution/assigned customer | Required for sensitive detail | Sensitive view audited |
| Security operator | `identity.device.revoke` | Institution/identity/device | Required | Reason; approval by policy |
| Identity administrator | `identity.account.unlock` | Institution/identity | Required | Reason; approval by policy |
| Security auditor | `identity.security_event.view` | Institution/classification | Required for sensitive detail | Read-only |
| Access reviewer | `identity.access.review` | Institution/assigned campaign | Required | Phase 2 orchestration |
| Service operator | `identity.service_client.manage` | Institution/environment/service | Required | Maker-checker and credential rotation |
| Business service identity | Explicit `service.<domain>.<action>` | Audience/institution/environment | Mutual/service authentication | Never receives a human role |

Permission catalogue rules:

- Use `<domain>.<resource>.<action>` names with a documented owning service and data classification.
- Published permission names are compatibility commitments; rename through deprecation and migration, not in-place mutation.
- Wildcard permissions are prohibited by default. Any platform break-glass capability requires an ADR, expiry, independent approval and enhanced monitoring.
- A role cannot contain permissions from another institution or unpublished permission versions.
- Effective access is the union of valid grants reduced by explicit conditions, conflicts, expiry, suspension and resource policy. A grant never overrides a deny required by tenant, security or segregation policy.
- Temporary grants require start time, expiry, reason, grantor, approval and review evidence.
- Permission and role changes are versioned and become active only after required approval and publication.

## 6. Domain model

### Aggregates and entities

| Concept | Responsibility | Identity | Important relationships |
| --- | --- | --- | --- |
| `Identity` | Actor type, lifecycle, provider links and institution memberships | `IdentityId` | Has sessions, methods, devices and grants |
| `InstitutionMembership` | Valid actor membership and status within one tenant | `MembershipId` | Identity plus institution; no cross-service FK |
| `AuthenticationPolicyVersion` | Effective password, MFA, lockout, session and channel rules | `AuthenticationPolicyVersionId` | Institution/channel/effective interval |
| `Session` | Local security metadata, device binding and revocation evidence | `SessionId` | References provider session/token family by protected opaque identifier |
| `AuthenticationChallenge` | Time-bound MFA or step-up attempt | `ChallengeId` | Actor, session, purpose, method and result |
| `MfaEnrolment` | Approved factor metadata without recoverable secret exposure | `MfaEnrolmentId` | Identity, method, provider reference and lifecycle |
| `TrustedDevice` | User-visible device registration and trust lifecycle | `TrustedDeviceId` | Identity, institution, protected device-key/fingerprint reference |
| `PermissionDefinition` | Stable platform capability metadata | `PermissionId` | Namespace, owner, version and classification |
| `Role` | Institution-owned named permission bundle | `RoleId` | Has immutable versions |
| `RoleVersion` | Draft/approved/published permission bundle | `RoleVersionId` | Permission assignments and conditions |
| `IdentityRoleGrant` | Role assignment with scope and effective interval | `GrantId` | Identity membership, role version and scope set |
| `ResourceScope` | Normalised contextual restriction | `ScopeId` | Institution, branch, channel, resource, amount or condition |
| `ConflictRule` | Prohibited role/permission combinations | `ConflictRuleId` | Institution and effective interval |
| `EntitlementVersion` | Monotonic change marker for one actor/tenant | `EntitlementVersionId` | Incremented on access-affecting changes |
| `ServiceClient` | Non-human workload identity and approved audiences | `ServiceClientId` | Environment, credentials/cert refs and service grants |
| `SecurityKeyMetadata` | Public discovery and private-operation reference metadata | `KeyMetadataId` | Institution, purpose, version, validity and KMS reference |

### Value objects

- `ActorType`: Customer, Staff, Agent, CorporateUser, Administrator or Service.
- `IdentityStatus`: Pending, Active, Locked, Suspended, Disabled or Closed.
- `Channel`: Web, Mobile, StaffWeb, USSD, WhatsApp, Agent, PartnerApi or Service.
- `PermissionName`, `RoleCode`, `ProviderSubject`, `TokenFamilyReference` and `Audience`.
- `ScopeType`, `ScopeValue`, `AmountLimit`, `Currency`, `EffectivePeriod` and `NetworkRestriction`.
- `AuthenticationContext`: methods, authentication time, assurance level, device ID, network/risk references and step-up evidence.
- `DeviceKeyThumbprint`, `DeviceDisplayName`, `DevicePlatform` and protected `DeviceSignals`.
- `KeyPurpose`, `KeyVersion`, `AlgorithmIdentifier` and `EncryptedEnvelope`.
- `EntitlementVersionNumber` as a per-identity/per-institution monotonic value.

### Invariants

1. Each human identity has one immutable platform ID; provider subject links are unique within provider/realm.
2. A membership belongs to exactly one institution and cannot convey access to another institution.
3. A service identity cannot receive a human role; a human identity cannot receive a service-client grant.
4. Roles are configurable bundles; role names never select application behaviour.
5. Only published, currently effective role versions may grant permissions.
6. A permission is effective only when every applicable institution, branch, channel, resource, amount, currency, device, time and step-up condition passes.
7. Conflict rules evaluate the combined access of all active and proposed grants before publication or assignment.
8. A maker cannot approve the same restricted change, even when the actor has another role that includes approval permission.
9. Access-affecting changes atomically increment the applicable entitlement version and create outbox/audit evidence.
10. Revoked, expired, suspended or locked sessions cannot be refreshed.
11. Registering a trusted device requires a fresh approved step-up challenge bound to the actor, session and device key.
12. Revoking a device revokes every active session bound to it in the same local transaction where possible and requests provider revocation idempotently.
13. MFA challenges are single-purpose, time-limited, attempt-limited and replay protected.
14. Passwords use an approved adaptive verifier when the platform stores them; raw passwords are never persisted or logged.
15. Only public keys and approved metadata may be returned to clients. Private-key operations occur through KMS/HSM-backed ports.
16. Payload encryption is additional to approved TLS and never weakens transport, authentication, authorisation or input validation.
17. Decryption accepts only approved algorithms, key versions, envelope purpose, audience, expiry and replay policy.
18. Privileged access requires the configured assurance level and enhanced evidence.

### Identity lifecycle

```text
Pending -> Active -> Locked -> Active
                   -> Suspended -> Active
                   -> Disabled -> Closed
```

- `Locked` is normally policy-driven by failed or suspicious authentication and may be time-bound or manually resolved.
- `Suspended` prevents new authentication and refresh, but preserves evidence and permits authorised investigation.
- `Disabled` is an administrative termination of access. Re-enablement requires explicit policy and evidence.
- `Closed` is terminal for authentication; records remain according to retention and legal-hold policy.

### Session lifecycle

```text
Issued -> Active -> Refreshed -> Active
                 -> StepUpRequired -> Active
                 -> Expired
                 -> Revoked
                 -> Compromised
```

Token-provider operations and local state must converge through idempotent commands and reconciliation. Local revocation must fail closed even if provider revocation is temporarily unavailable.

### Role version and grant lifecycle

```text
Draft -> PendingApproval -> Approved -> Scheduled -> Published -> Superseded
   |            |              |
   +-> Withdrawn+-> Rejected   +-> RejectedBeforePublication

ProposedGrant -> PendingApproval -> Active -> Expired
                     |              -> Revoked
                     +-> Rejected
```

A role version or grant is immutable after publication/activation except for lifecycle metadata. Corrections create a new version or explicit revocation.

### Device lifecycle

```text
PendingStepUp -> Trusted -> Suspended -> Trusted
                         -> Revoked
                         -> Expired
```

A revoked device cannot silently regain trust. Registration creates a new device identity after fresh step-up and policy evaluation.

## 7. Core use cases

### IAM-UC-01 - Authenticate a human actor

- Actor: Customer, staff, agent, corporate user or administrator.
- Preconditions: Active identity and institution membership; active authentication policy; supported channel.
- Command: `AuthenticateActor` through the approved provider adapter.
- Main flow: Resolve tenant/channel policy, validate credentials without logging them, evaluate lock/challenge rules, complete configured MFA, create or correlate the session, record security evidence and return approved tokens/session metadata.
- Failure outcomes: `identity_invalid_credentials`, `identity_locked`, `identity_suspended`, `authentication_mfa_required`, `authentication_challenge_failed`, `authentication_policy_unavailable`.
- Idempotency: Challenge completion is keyed by challenge ID plus provider attempt/event identity; sign-in itself must not reuse successful secrets.
- Audit: Success/failure category, actor reference when known, institution, channel, device reference, masked network metadata, assurance level and correlation ID.

### IAM-UC-02 - Refresh or revoke a session

- Actor: Session owner, authorised administrator or security automation.
- Preconditions: Valid session/token family reference and policy.
- Permission: Self-service or `identity.session.revoke` for another actor.
- Main flow: Verify session state, token family, entitlement version and device state; refresh through provider or revoke locally and at provider; publish state change.
- Concurrency: Compare-and-set lifecycle transition; repeated revocation returns the original outcome.
- Failure outcomes: `session_expired`, `session_revoked`, `session_compromised`, `entitlement_stale`.

### IAM-UC-03 - Create and publish a role version

- Actor: Institution security administrator and separate security approver.
- Permission: `identity.role.create`/`identity.role.update`, then `identity.role.approve`.
- Main flow: Create draft, add published permissions and conditions, simulate effective access/conflicts, submit, approve, schedule and publish.
- Guards: Same-actor approval denied; wildcard denied unless specially approved; conflict analysis must pass; all permissions belong to the approved catalogue.
- Transaction boundary: Role version, assignments, lifecycle record, entitlement-impact work item, audit and outbox commit together at publication.
- Events: `RoleVersionPublished.v1`.

### IAM-UC-04 - Assign a role with contextual scope

- Actor: Grant administrator and configured approver.
- Permission: `identity.grant.assign` and `identity.grant.approve`.
- Main flow: Resolve identity membership and published role, validate scopes against institution references, evaluate current/proposed conflicts, approve if required, activate, increment entitlement version and publish invalidation.
- Failure outcomes: `grant_cross_tenant`, `grant_scope_invalid`, `grant_conflict`, `grant_self_approval`, `role_version_not_active`.
- Idempotency: Institution plus target identity plus client request ID; payload hash mismatch is a conflict.

### IAM-UC-05 - Evaluate an authorisation requirement

- Actor: Any authenticated human/service identity through a business service.
- Input: Trusted actor context, permission, target institution, resource attributes, requested action and current authentication context.
- Main flow: Validate identity/session and entitlement freshness, load/evaluate effective grants, apply scopes/conflicts/conditions, return allow/deny plus stable reason and policy versions.
- Availability: Privileged and financial operations fail closed when authoritative or acceptably fresh entitlement state is unavailable.
- Evidence: Sensitive decisions record permission, scopes considered, outcome and versions without copying sensitive resource data.

### IAM-UC-06 - Register or revoke a trusted device

- Actor: Customer or authorised security operator.
- Permission: `identity.device.self.manage` or `identity.device.revoke`.
- Main flow: Start device registration, bind challenge to device public key/thumbprint, require fresh step-up, activate device, or revoke device and all bound sessions.
- Guards: Device display metadata is untrusted; fingerprint/signals alone do not authenticate; duplicate key registration follows explicit policy.
- Events: `TrustedDeviceRegistered.v1`, `TrustedDeviceRevoked.v1`, `SessionsRevokedForDevice.v1`.

### IAM-UC-07 - Decrypt a selected authentication payload field

- Actor: Authentication endpoint, never the frontend itself.
- Preconditions: TLS, approved endpoint/field allow-list, active key metadata and valid envelope.
- Main flow: Validate envelope version/purpose/audience/timestamp/replay data, request private-key operation through KMS/HSM adapter, limit plaintext lifetime, validate the resulting field and clear/dispose buffers where supported.
- Failure outcomes: Stable `payload_encryption_invalid`, `payload_key_expired`, `payload_key_revoked` or `payload_decryption_unavailable`; never disclose cryptographic internals.
- Audit: Key version, outcome and correlation only; never ciphertext/plaintext or private material.

### IAM-UC-08 - Authenticate a service client

- Actor: Approved workload.
- Preconditions: Active client, environment, audience, network/certificate conditions and rotated credential reference.
- Main flow: Validate short-lived assertion or mutual certificate through the approved mechanism, issue trusted service context, enforce explicit service permission and audience in the target service.
- Guards: No human roles, interactive refresh tokens or institution-wide wildcard by default.
- Events: Credential or client lifecycle events only; ordinary successful calls use telemetry rather than one event per call.

## 8. API contracts

Routes are illustrative versioned contracts and must be reconciled with the approved identity provider. Credential-bearing endpoints require stricter rate limiting, body logging suppression and security headers.

| Method | Route | Permission | Request | Response | Idempotent |
| --- | --- | --- | --- | --- | --- |
| POST | `/api/v1/auth/sessions` | Anonymous plus channel policy | `CreateSessionRequest` | `SessionChallengeOrTokenResponse` | No |
| POST | `/api/v1/auth/challenges/{id}/complete` | Challenge possession | `CompleteChallengeRequest` | `SessionTokenResponse` | By challenge attempt |
| POST | `/api/v1/auth/sessions/refresh` | Valid refresh context | `RefreshSessionRequest` | `SessionTokenResponse` | Provider policy |
| DELETE | `/api/v1/auth/sessions/{sessionId}` | Self or `identity.session.revoke` | Reason when administrative | `SessionResponse` | Yes |
| GET | `/api/v1/auth/sessions` | `identity.session.self.manage` | Pagination/filter | `SessionPage` | Yes |
| GET | `/api/v1/identity/devices` | `identity.device.self.manage` | Pagination | `TrustedDevicePage` | Yes |
| POST | `/api/v1/identity/devices/registrations` | `identity.device.self.manage` | `StartDeviceRegistrationRequest` | `StepUpChallengeResponse` | By request ID |
| POST | `/api/v1/identity/devices/registrations/{id}/complete` | Challenge possession | `CompleteDeviceRegistrationRequest` | `TrustedDeviceResponse` | Yes |
| DELETE | `/api/v1/identity/devices/{deviceId}` | Self or `identity.device.revoke` | Reason when administrative | `TrustedDeviceResponse` | Yes |
| GET | `/api/v1/security/public-keys/{purpose}` | Public metadata | Institution/channel/key purpose | `PublicKeySetResponse` | Yes |
| GET | `/api/v1/access/permissions` | `identity.permission.catalogue.view` | Bounded filters | `PermissionPage` | Yes |
| POST | `/api/v1/access/roles` | `identity.role.create` | `CreateRoleRequest` | `RoleResponse` | By request ID |
| POST | `/api/v1/access/roles/{roleId}/versions` | `identity.role.update` | `CreateRoleVersionRequest` | `RoleVersionResponse` | By request ID |
| POST | `/api/v1/access/role-versions/{id}/submit` | `identity.role.update` | Reason | `RoleVersionResponse` | Yes |
| POST | `/api/v1/access/grants` | `identity.grant.assign` | `CreateGrantRequest` | `GrantResponse` | By request ID |
| DELETE | `/api/v1/access/grants/{id}` | `identity.grant.revoke` | Required reason | `GrantResponse` | Yes |
| POST | `/internal/v1/authorisation/evaluate` | Approved service permission | `AuthorisationEvaluationRequest` | `AuthorisationDecisionResponse` | Yes |
| GET | `/internal/v1/entitlements/{identityId}/version` | Approved service permission | Institution context | `EntitlementVersionResponse` | Yes |
| POST | `/internal/v1/access/role-versions/{id}/approval-decisions` | Workflow service permission | Exact workflow decision/version/hash | `RoleVersionResponse` | By decision ID |
| POST | `/internal/v1/access/grants/{id}/approval-decisions` | Workflow service permission | Exact workflow decision/version/hash | `GrantResponse` | By decision ID |

Contract rules:

- Never return password verifier, factor secret, OTP, recovery code, refresh token record, private-key reference details or raw device signals.
- Browser/mobile token delivery and storage follow the approved channel threat model. Do not default every channel to the same token transport.
- Use RFC 7807 problem details with stable codes and generic authentication failures that resist identity enumeration.
- `401` means authentication is absent, invalid or expired. `403` means an authenticated actor is not authorised. Hidden cross-tenant resources may return the approved non-disclosing outcome.
- Collection routes require bounded pagination, institution scope and allow-listed filters.
- Administrative mutations require correlation ID, client request ID, expected version and reason where applicable.
- Human approvers act through Operations and Workflow endpoints using `identity.role.approve` or `identity.grant.approve`. Identity and Access exposes only service-authenticated decision-application endpoints and rejects a decision that does not match the pending version/hash.
- OpenAPI examples use synthetic identities, IP addresses, device metadata and tokens.

## 9. Event contracts

### Produced events

| Event | Trigger | Key fields | Ordering key | Consumers |
| --- | --- | --- | --- | --- |
| `IdentityActivated.v1` | Identity becomes active | Identity ID, actor type, institution membership refs | Identity ID | Audit, Risk, channels |
| `IdentityAccessSuspended.v1` | Lock/suspension blocks access | Identity ID, institution ID, reason category | Identity ID | Business services, Audit, Risk |
| `SessionRevoked.v1` | Session is revoked/compromised | Session ID, identity ID, institution ID, reason category | Identity ID | Gateway/read models, Audit, Risk |
| `TrustedDeviceRegistered.v1` | Device trust becomes active | Device ID, identity ID, institution ID, platform category | Identity ID | Risk, Audit |
| `TrustedDeviceRevoked.v1` | Device is revoked | Device ID, identity ID, institution ID, reason category | Identity ID | Risk, channels, Audit |
| `RoleVersionPublished.v1` | Approved role version becomes effective | Role/version IDs, institution ID, permission-name hashes/list per contract policy | Institution ID | Business authorisation read models, Audit |
| `IdentityGrantChanged.v1` | Grant activates, expires or is revoked | Identity ID, institution ID, grant ID, status | Identity ID | Business services, Audit |
| `EntitlementVersionAdvanced.v1` | Effective access may have changed | Identity ID, institution ID, new version, reason category | Identity ID | Every protected business service |
| `AuthenticationSecurityEventRaised.v1` | Policy-defined suspicious/high-risk event | Event ID, institution ID, actor ref when known, category, severity | Institution ID | Risk, SIEM adapter, Audit |
| `ServiceClientChanged.v1` | Service client/credential lifecycle changes | Client ID, institution/environment, status/version | Client ID | Target services, Audit |
| `PublicKeyMetadataChanged.v1` | Key activates, rotates or revokes | Institution ID, purpose, public key ID/version/status | Institution ID | Frontend config, owning services, Audit |

### Consumed events

| Event | Producer | Consumer behaviour | Deduplication key | Failure handling |
| --- | --- | --- | --- | --- |
| `InstitutionActivated.v1` | Core Banking and Ledger | Create institution identity/access partition and baseline policy draft | Event ID | Inbox retry; institution access remains unavailable |
| `InstitutionSuspended.v1` | Core Banking and Ledger | Deny institution sessions/refresh and advance affected entitlements | Event ID | High-priority retry and alert |
| `BranchCreated.v1`/`BranchStatusChanged.v1` | Core Banking and Ledger | Maintain branch scope reference read model | Event ID | Fail branch-scoped grants closed until current |
| `AuthenticationRiskAssessed.v1` | Risk and Reporting | Challenge, limit or block according to approved policy | Risk assessment ID | Timeout follows channel fail-safe policy |
| `ApprovalDecided.v1` | Operations and Workflow | Apply exact pending role/grant/security change once | Approval decision ID | Inbox retry; change remains pending |
| `SecurityKeyVersionPublished.v1` | Institution/Integration configuration | Update approved public/private-operation metadata | Event ID | Old allowed version follows transition policy; alert |

Every event carries event ID, schema version, occurred time, institution ID where applicable, correlation ID, causation ID and aggregate reference. Events exclude secrets, credentials, tokens, raw authentication assertions, full IP addresses unless explicitly approved, biometric data and unnecessary personal data.

Outbox publication follows the local commit. Consumers use inbox deduplication. Replay must not reactivate revoked access, duplicate audit evidence, reset expiry or decrement an entitlement version.

## 10. Database design

### Ownership

- Database: `identity_access_db`.
- Schemas: `identity`, `access`, `security`, `integration`.
- Migration assembly: Identity and Access Infrastructure project only.
- Principal: Separate runtime and migration principals; no other service receives direct table access.
- Credential authority: The approved provider may own credential/token protocol storage. Local tables store only required platform metadata and protected opaque references.

### Identity and authentication tables

| Table | Purpose | Key constraints | Tenant-owned | Retention |
| --- | --- | --- | --- | --- |
| `identity.identities` | Typed platform identity lifecycle | Actor type/status checks; concurrency version | Platform identity; partitioned access | Policy/legal hold |
| `identity.institution_memberships` | Identity-to-institution relationship | Unique identity/institution; status | Yes | Policy/legal hold |
| `identity.identity_links` | Provider/realm subject mapping | Unique provider/realm/subject | Yes where applicable | Link lifecycle plus evidence |
| `identity.authentication_methods` | Non-secret method/provider metadata | Unique identity/provider/method ref | Yes | Security policy |
| `identity.mfa_enrolments` | Factor type, protected provider ref and status | No recoverable secret; versioned status | Yes | Security policy |
| `identity.sessions` | Session state, actor, channel and device binding | Unique opaque provider family ref hash; concurrency | Yes | Active plus security retention |
| `identity.session_status_history` | Append-only session transitions | Sequence unique per session | Yes | Security/audit policy |
| `identity.refresh_token_references` | Protected hash/reference only if locally required | Unique token-family hash; never plaintext | Yes | Until expiry plus security window |
| `identity.trusted_devices` | User-visible device trust state | Unique identity/institution/device-key thumbprint per policy | Yes | Lifecycle plus security retention |
| `identity.device_status_history` | Append-only trust transitions | Sequence unique per device | Yes | Security/audit policy |
| `identity.service_clients` | Workload identity metadata and audiences | Unique environment/client code | Yes/platform | Lifecycle plus audit |
| `identity.service_credentials` | KMS/certificate reference and lifecycle metadata | Unique client/version; no private credential | Yes/platform | Lifecycle plus audit |

### Access-control tables

| Table | Purpose | Key constraints | Tenant-owned | Retention |
| --- | --- | --- | --- | --- |
| `access.permissions` | Stable capability catalogue | Unique permission name/version; owner required | Platform-defined | Permanent/deprecated |
| `access.roles` | Institution role identity | Unique institution/role code | Yes | Permanent history |
| `access.role_versions` | Immutable role publication units | Unique role/version; status/effective checks | Yes | Permanent history |
| `access.role_version_permissions` | Permission and condition assignments | Unique version/permission/condition | Yes | With role version |
| `access.identity_role_grants` | Proposed/effective/revoked role grants | Grant lifecycle and effective interval | Yes | Permanent evidence |
| `access.grant_status_history` | Append-only grant decisions/transitions | Unique grant/sequence | Yes | Permanent evidence |
| `access.resource_scopes` | Typed normalised scope records | Scope-type/value validation | Yes | With grant/version |
| `access.grant_scopes` | Grant-to-scope association | Unique grant/scope | Yes | With grant |
| `access.permission_conditions` | Versioned conditional policy expressions | Approved expression type/schema only | Yes/platform | Permanent history |
| `access.conflict_rules` | Prohibited role/permission combinations | Versioned/effective-dated | Yes | Permanent history |
| `access.temporary_grants` | Expiring elevated grants and reason | Expiry required; approval ref | Yes | Permanent evidence |
| `access.entitlement_versions` | Current monotonic version by actor/tenant | Unique identity/institution; version never decreases | Yes | Current plus history |
| `access.entitlement_version_history` | Reason and origin of each advance | Unique identity/institution/version | Yes | Security/audit policy |
| `access.access_reviews` | Phase 1 review-ready record shell | No campaign behaviour yet | Yes | Policy/legal hold |

### Security and integration tables

| Table | Purpose | Key constraints | Tenant-owned | Retention |
| --- | --- | --- | --- | --- |
| `security.authentication_policies` | Versioned institution/channel policy | Unique institution/channel/version | Yes | Permanent history |
| `security.authentication_challenges` | Challenge purpose, expiry, attempts and result | Single-use completion; protected provider ref | Yes | Short active plus evidence |
| `security.login_events` | Append-only authentication evidence | Event ID unique; partition by time/institution | Yes | Security/retention policy |
| `security.lockouts` | Active and historical lock/challenge actions | One active applicable lock per scope | Yes | Security policy |
| `security.privileged_access_events` | Enhanced privileged-action evidence | Append-only | Yes | Audit/legal hold |
| `security.key_metadata` | Key ID/version/public metadata/KMS operation ref | Unique institution/purpose/version | Yes | Rotation history |
| `security.envelope_replay_records` | Replay identifiers where envelope policy requires | Unique institution/purpose/replay ID; expiry | Yes | Short bounded window |
| `integration.outbox_messages` | Durable event publication | Event ID unique | Yes where applicable | Operational policy |
| `integration.inbox_messages` | Consumer deduplication | Consumer/event ID unique | Yes where applicable | Replay horizon |
| `integration.idempotency_records` | Administrative request replay protection | Institution/operation/key unique; request hash | Yes | Approved replay horizon |

### Constraints and indexes

- Every tenant-owned unique index begins with `institution_id` unless uniqueness is deliberately platform-wide.
- Provider subject and opaque token-family references are normalised then protected/hashed according to threat model before indexing.
- PostgreSQL check constraints enforce legal status values, effective date ordering, non-negative attempt limits, required expiry for temporary grants and actor/grant compatibility where possible.
- Partial unique indexes prevent multiple active versions where only one is legal.
- Foreign keys remain within `identity_access_db`; branch, customer, account, mandate and external workflow identifiers are validated references without cross-database FKs.
- Concurrency tokens protect identities, sessions, devices, role drafts, grants, policies and entitlement rows.
- Authentication-event indexes support bounded queries by institution/time, actor/time, category/severity and correlation ID without indexing secret or high-risk raw values.
- Append-only history tables reject update/delete through application permissions.

### Migration and backfill

- Create schemas and reference permission catalogue before enabling protected Phase 1 endpoints.
- Seed only platform-owned permission definitions; never seed broad institution roles, real users, credentials or production grants.
- Provision an institution baseline through the institution event and an idempotent application command, not ad hoc SQL.
- Any legacy identity import requires mapping, duplicate detection, credential/provider migration, forced-reset policy, reconciliation totals and security approval. The applied procedure for a Phase 0 local database is `docs/operations/identity-access-legacy-identity-kind-mapping.md`.
- Released migrations are append-only. Corrections use forward migrations with verification evidence.
- Rollback of an access migration must never silently restore revoked access. Prefer forward-fix and fail closed.
- Migration drift is verified per context by `scripts/migrations-verify.ps1`, which fails the build, `scripts/verify.ps1` and CI when a model change is not recorded in a migration. It never connects to a database and never generates or applies a migration.
- The session lifecycle migration `20261027150150_AddSessionLifecycleAndSecurityLocks` adds its columns nullable, backfills deterministically, validates the backfill and only then applies `NOT NULL`, the tenant-unique provider fingerprint index and the lifecycle check constraints. Legacy rows carry a `legacy:` marker that cannot match a real provider fingerprint, so an unbound legacy session fails closed on refresh. `security.locks` records policy-driven lock evidence and is written, read and released through `SessionSecurityService`.
- Application principals receive least-privilege grants from each service's `AddApplicationPrincipalPrivileges` migration; effective privileges, including role membership and ownership, are asserted by `tests/CoreBanking.SecurityTests`. Procedure: `docs/operations/database-privileges.md`.

## 11. Consistency, concurrency and entitlement invalidation

- Role draft changes use optimistic concurrency. Publishing locks or atomically compares the exact approved version/hash.
- Grant activation/revocation, entitlement-version advance, audit record and outbox event commit in one local transaction.
- Simultaneous grant proposals evaluate conflicts again inside the activation transaction; pre-approval simulation alone is insufficient.
- Session/device revocation uses idempotent compare-and-set transitions. Provider revocation is an outbox-backed side effect with retries and reconciliation.
- An entitlement version is monotonic per identity/institution. It never decreases during restoration, replay or role rollback.
- Business services may maintain local entitlement read models or bounded caches. Every decision records the version used.
- For privileged and financial actions, a missing, expired, invalidated or unverifiable entitlement state denies access. Lower-risk read behaviour requires an approved availability/security policy.
- Events are ordered by identity ID for identity/grant changes. Consumers must still tolerate duplicates and detect version gaps.
- A version gap triggers targeted resynchronisation from an authenticated internal API; consumers do not guess intermediate entitlements.
- Idempotency keys for administrative changes are scoped by institution, operation and initiating actor/client. Reuse with a different payload hash is rejected and audited.
- No distributed transaction is required. Local authoritative commits, outbox/inbox, monotonic versions, reconciliation and fail-closed consumers provide convergence.

## 12. Security and privacy

### Authentication security

- The identity-provider ADR must document credential ownership, supported OAuth/OIDC profiles, channel flows, phishing resistance, token lifetime, refresh rotation, logout/revocation, key rotation, tenant model and licensing/operational responsibilities.
- Use approved adaptive password hashing only if password verification is owned locally. Never design a custom password, OTP, biometric or token protocol.
- Configurable password rules cannot weaken approved platform security minima.
- Failed-attempt and suspicious-login policy must resist both brute force and account-lockout denial of service.
- Authentication error responses resist user, tenant and factor enumeration.
- MFA factor changes, recovery and privileged sign-in require appropriate step-up and enhanced evidence.
- Biometric templates remain on the device or approved biometric provider where possible. The backend consumes a verified protocol result, not raw biometric data.
- CSRF protection, secure cookie rules, CORS, redirect URI validation and token storage are defined per channel threat model.

### Authorisation security

- Every protected controller/handler declares a permission, but enforcement also occurs in the application use case with target resource context.
- Never accept client-supplied role, permission, institution, ownership, approval or assurance claims without trusted validation.
- Resource lookup and authorisation order must avoid cross-tenant existence disclosure.
- Conflict and maker-checker checks use stable actor identity, not role name or display name.
- Service clients validate issuer, audience, environment, credential/certificate state and explicit service permission.
- Emergency/break-glass access is unavailable until its policy, approval, monitoring, expiry and post-review process are approved.

### Devices and payload encryption

- Do not use an unstable browser fingerprint as the sole device identifier. Prefer an app-generated device key protected by platform facilities and bind it through challenge proof.
- Store only device data necessary for security and user recognition, with retention and privacy review.
- Public-key endpoints return an allow-listed algorithm, key ID, version, purpose, validity period and encoded public key/certificate only.
- Encrypted envelopes bind ciphertext to key ID/version, institution, endpoint/purpose, audience, issued time and replay value as required by the approved design.
- Decrypted sensitive values are never added to model-state logs, request capture, traces, exception data or analytics.
- Key revocation and rotation define an overlap/decryption transition. A client cannot choose an obsolete or unapproved algorithm.
- TLS is mandatory regardless of payload encryption.

### Threat cases requiring tests

- Credential stuffing, password spraying, enumeration and lockout abuse.
- Stolen refresh token reuse and concurrent refresh.
- Session use after identity, grant or device revocation.
- Cross-tenant grant creation and confused-deputy service calls.
- Role conflict introduced by two concurrent approvals.
- Maker approving through a second role or second active session.
- Forged/stale entitlement version and poisoned cache.
- MFA challenge replay, excessive attempts, expiry and purpose swapping.
- Device-key replay, duplicate registration and revoked-device re-entry.
- Payload downgrade, wrong-purpose ciphertext, stale/replayed envelope and revoked key version.
- Mass assignment on role, grant, policy, device and identity endpoints.

## 13. Audit and observability

### Immutable evidence

Record successful and failed authentication categories, challenge lifecycle, lock/unlock, session issue/refresh/revocation, device registration/revocation, role draft/submit/approval/publication, grant proposal/approval/activation/revocation/expiry, conflict denial, entitlement advance, service-client lifecycle, key-metadata change and sensitive authorisation decisions.

Evidence includes, where applicable:

- Stable actor/service ID and actor type.
- Institution and branch context.
- Role/grant references and required permission.
- Target resource type plus protected reference, not raw sensitive payload.
- Decision, denial category, reason and maker/approver separation.
- Authentication assurance, factor categories and device reference without factor secrets.
- Policy, role, grant, entitlement and key versions.
- Source channel, masked/network classification, timestamp and correlation ID.
- Before/after values for administrative metadata where policy permits.

Ordinary administrators cannot modify or delete evidence. Audit delivery failure is observable; a sensitive authoritative change must retain durable local evidence/outbox before success is returned.

### Logs, metrics and traces

Structured logs use stable event names and exclude passwords, PINs, OTPs, tokens, assertions, session cookies, recovery codes, private keys, decrypted fields, raw device secrets and unrestricted request bodies.

Minimum metrics:

- Authentication successes/failures/challenges by institution, channel and safe category.
- Lockout and suspicious-login counts/rates.
- MFA enrolment/challenge success and recovery activity.
- Active/revoked sessions and session/provider revocation backlog.
- Trusted-device registrations/revocations and bound-session revocation latency.
- Authorisation allow/deny by permission family and safe reason.
- Role/grant approval age, conflicts and expired temporary grants.
- Entitlement publication lag, consumer version gaps, stale-cache denial and resync failures.
- Service-authentication failures and expiring credential/certificate count.
- Key rotation status, deprecated key use and decryption failure category.
- Audit/SIEM delivery failures.

Trace context crosses provider adapters, workflow decisions, outbox events and business-service authorisation checks without recording credential material. Alerts and runbooks cover attack-rate thresholds, privileged-access anomaly, revocation backlog, entitlement propagation breach, provider outage, signing-key/certificate expiry, repeated decryption failures and audit/SIEM delivery failure.

## 14. Failure, retry and recovery

| Failure | Safe outcome | Retry/recovery |
| --- | --- | --- |
| Identity provider unavailable before authentication | No session issued | Bounded retry/circuit breaker; channel-safe error |
| Provider outcome ambiguous | No duplicate local active session assumed | Status/introspection/reconciliation per provider contract |
| Provider revocation unavailable | Local session/device denies immediately | Durable retry and alert until provider converges |
| Entitlement store/cache unavailable | Privileged/financial action denied | Resync/retry; no stale allow beyond policy |
| Entitlement event gap | Consumer marks identity version incomplete | Fetch signed/current snapshot; preserve deny |
| Risk service timeout | Apply approved channel/action fail-safe | Do not invent allow behaviour |
| Workflow approval delayed | Role/grant change remains pending | Resume idempotently from exact approved version |
| KMS/HSM unavailable | Encrypted-field operation fails safely | No plaintext fallback; alert/circuit breaker |
| Key version unknown/revoked | Reject envelope generically | Refresh public-key metadata; never try arbitrary keys |
| Duplicate challenge completion | Return prior safe terminal result or reject replay | Never issue a second independent session unintentionally |
| Concurrent refresh token use | Detect reuse according to provider policy | Revoke token family/session and raise security event |
| Database restore | Restored access may be stale relative to revocations | Reconcile provider state and advance/rebuild versions before reopening traffic |

Backups are encrypted and restored in exercises. Restore verification must prove identities, current role/grant versions, revoked sessions/devices, entitlement monotonicity, audit/outbox continuity and absence of plaintext secrets. Production traffic remains closed until provider and local security state are reconciled.

## 15. Configuration

| Setting | Owner | Secret | Validation | Reload behaviour |
| --- | --- | --- | --- | --- |
| Identity authority/issuer metadata | Platform security | No | HTTPS, issuer/audience allow-list | Controlled refresh |
| Client credential/certificate reference | Platform security | Reference only | KMS/secret-manager existence, environment, expiry | Rotated with overlap |
| Token/session policy | Identity security | No | Platform floor/ceiling plus channel rules | New sessions; revocation policy defined |
| Password/lockout policy | Institution security within platform floor | No | Approved schema and abuse analysis | Effective-dated |
| MFA/step-up policy | Institution security/risk | No | Supported factors, action/channel/assurance mapping | Versioned/effective-dated |
| Entitlement cache and revocation objective | Platform security | No | Cannot exceed approved risk window | Controlled rollout |
| Role/grant approval policy | Institution security | No | Separation/conflict validation | Versioned/effective-dated |
| Device trust duration and registration policy | Institution security/risk | No | Channel/privacy/security constraints | New/renewed trust decisions |
| Public-key metadata | Institution/platform security | Public portion only | Approved algorithm/purpose/version/validity | Rotation overlap |
| Private-key operation reference | Platform security | Secret reference | KMS/HSM purpose and access policy | Controlled rotation |
| Authentication rate limits | Platform/institution security | No | Per channel/actor/network safe dimensions | Dynamic within approved bounds |
| Security-event/SIEM destination | Platform security | Credential ref only | Connectivity, redaction and durability | Controlled |

All security-affecting configuration is versioned, approved, effective-dated and audited. A missing or invalid security configuration fails closed; it does not fall back to permissive defaults.

## 16. Implementation slices

Implement one deployable, reviewable slice at a time:

Implementation progress as of 2026-10-26: Slice 1 is complete as an architecture and security baseline. ADR-0008 and ADR-0009 are accepted, their previously inconsistent status text is reconciled, the Phase 1 entitlement snapshot and invalidation delivery model is explicit, and the [Identity and Access threat model](../../security/identity-access-threat-model.md) records assets, trust boundaries, channel constraints, STRIDE threats, abuse cases, required controls and executable verification targets. Slice 2 promotes the Phase 0 security vocabulary into dependency-free version 1 contracts for explicit actor kinds, trusted tenant context, permission requirements, request/grant scopes, safe authorisation decisions, entitlement snapshot requests/responses and monotonic invalidation facts. Compatibility tests pin camel-case wire names, additive-field tolerance, decimal limits, stable codes, tenant/version fields, data minimisation and contract-assembly isolation. Slice 3 adds explicit customer, staff, agent, corporate-user, administrator and service identity kinds; institution membership and hashed provider-link records; active-institution gating; capability- and step-up-protected registration and status transitions; tenant-scoped retrieval; optimistic lifecycle concurrency; entitlement-version advancement; idempotency; immutable operator evidence; and durable `IdentityRegistered.v1` and `IdentityStatusChanged.v1` outbox facts. Provider subjects are used transiently and never returned or stored in plaintext. The append-only migration refuses to invent actor kinds for legacy Phase 0 identities and requires an approved mapping before upgrade. This does not authorise production enrolment. Keycloak hosting and operational ownership, approved second-factor methods, channel-specific token-storage deployment reviews, service-specific cache settings, initial role/conflict configuration and production SLOs remain named release gates.

1. Approve ADR-0008 identity provider/token/channel model and ADR-0009 authorisation distribution/invalidation model; add a threat model.
2. Promote Phase 0 actor, tenant, permission, scope, decision and entitlement contracts to versioned production contracts with compatibility tests.
3. Implement identity, membership and provider-link lifecycle with institution provisioning events and cross-tenant tests.
4. Implement authentication-policy versions and the approved provider adapter using synthetic/non-production credentials. **Complete:** effective-dated, immutable policy metadata is tenant-scoped and does not invent the still-open password/MFA/lockout floors. The Keycloak adapter refuses direct platform credential grants, while Development registers a separately configured synthetic fixture adapter that returns only a provider subject. No production enrolment, policy administration, session, lockout or MFA flow is enabled by this slice.
5. Implement sessions, refresh/revocation coordination, lockouts and append-only authentication evidence. **In progress:** local session lifecycle state now fails closed on idempotent self-revocation, persists only a provider-session fingerprint, emits durable revocation work, and records append-only authentication evidence. Security locks are tenant-scoped evidence records; policy thresholds and provider-side refresh confirmation remain configuration/provider integration work and are not invented here.
6. Implement MFA enrolment/challenges and step-up evidence for one approved customer channel and one staff/privileged flow.
7. Implement permission catalogue plus role draft, simulation, approval and publication lifecycle.
8. Implement scoped grants, conflict-of-duty evaluation, temporary expiry and entitlement version advancement.
9. Implement entitlement distribution/invalidation and integrate one financial and one non-financial protected business operation.
10. Implement maker identity/approver eligibility contracts with Workflow; prove self-approval denial across roles and sessions.
11. Implement trusted-device registration, listing, step-up, revocation and bound-session termination.
12. Approve the payload-encryption design; implement public-key discovery and one authentication-field envelope flow through KMS/HSM port.
13. Implement service identity authentication, audience restrictions, rotation and one service-to-service integration.
14. Add SIEM/audit adapters, dashboards, alerts, reconciliation jobs and runbooks.
15. Complete security, contract, integration, concurrency, recovery and end-to-end evidence before enabling production access.

Feature flags may hide incomplete administrative UI/API exposure, but an incomplete policy path must never default to allow. Each slice leaves the solution buildable and retains Phase 0 negative tests.

## 17. Test plan

| Test ID | Requirement/story | Level | Scenario | Expected evidence |
| --- | --- | --- | --- | --- |
| IAM-001 | IAM-001 / US-005 | Integration | Create each supported human actor type and membership | Typed identity; tenant isolation; audit |
| IAM-002 | IAM-001, IAM-011 | Architecture | Attempt human role on service identity and inverse | Rejected at application and persistence boundary |
| IAM-003 | IAM-002 / US-005 | Integration | Authenticate with configured password plus MFA | Approved assurance and session evidence |
| IAM-004 | IAM-002, IAM-008 | Security | Exceed failed/suspicious login policy | Challenge/lock without enumeration leakage |
| IAM-005 | IAM-003 | Integration | Refresh valid session and rotate token family | Prior token handling matches policy; no duplicate session |
| IAM-006 | IAM-003 | Concurrency | Concurrent refresh/reuse attempt | Reuse detected; family revoked or approved safe result |
| IAM-007 | IAM-003 | Security | Use revoked/expired session | Denied consistently across services |
| IAM-008 | IAM-004 / US-006 | Integration | Publish role with granular permissions and scopes | Exact approved version becomes active once |
| IAM-009 | IAM-004 / US-006 | Security | Cross-tenant role or grant assignment | Denied without target disclosure |
| IAM-010 | IAM-005 / US-006 | Domain | Assign multiple compatible roles | Effective permissions combine with scopes |
| IAM-011 | IAM-005 / US-006 | Concurrency | Concurrent grants create prohibited combination | One/both denied so conflict never becomes active |
| IAM-012 | IAM-006 / US-007 / BR-008 | Security | Maker approves through another role/session | Denied using stable actor ID and audited |
| IAM-013 | IAM-007 / US-007 | Contract | Sequential/parallel approval eligibility query | Eligible approvers respect amount/branch/product/risk conditions |
| IAM-014 | IAM-009 | Integration | Temporary grant reaches expiry | Access denied and entitlement version advances |
| IAM-015 | IAM-010 | Security | Privileged action without fresh strong authentication | Step-up required; no state mutation |
| IAM-016 | IAM-011 | Security | Service client uses wrong audience/environment | Denied and security event emitted |
| IAM-017 | IAM-011 | Integration | Rotate service credential/certificate | Overlap follows policy; expired version denied |
| IAM-018 | SEC-009 / US-006 | Security | Permission exists but branch/account/amount scope fails | Stable deny; business action not invoked |
| IAM-019 | SEC-009 | Resilience | Entitlement cache unavailable or version stale | Privileged/financial action fails closed |
| IAM-020 | IAM-004 | Messaging | Replay entitlement/grant events | No duplicate grant, version regression or extra audit outcome |
| IAM-021 | US-009 | Integration | Register new trusted device | Fresh step-up bound to device key; audit/event |
| IAM-022 | US-009 | Integration | Revoke device with several sessions | All bound sessions denied/revoked idempotently |
| IAM-023 | US-009 | Security | High-risk action on untrusted device | Challenged or blocked according to exact policy version |
| IAM-024 | SEC-002..SEC-004 / US-008 | Contract | Discover active public key during rotation | Only public material and valid versions returned |
| IAM-025 | SEC-002..SEC-004 / US-008 | Security | Wrong-purpose, replayed, expired or revoked-key envelope | Generic denial; no plaintext/key leakage |
| IAM-026 | SEC-003 / BR-009 | Security | Inspect API/log/event/error/database outputs | No private key, password, OTP, token or decrypted field |
| IAM-027 | AUD-001..AUD-003 | Integration | Role/grant/auth/device privileged changes | Complete immutable evidence; admin cannot alter it |
| IAM-028 | IAM-003..IAM-006 | Recovery | Restore backup containing prior session/grant state | Revocations reconciled; entitlement versions never regress |
| IAM-029 | SEC-011 | Security | Mass-assign role status, institution or approval fields | Unaccepted fields ignored/rejected; no elevation |
| IAM-030 | NFR-OBS-001, SEC-017 | End-to-end | Trace sign-in, step-up, grant change and business denial | Correlated trace plus redacted SIEM/audit evidence |

Performance and abuse tests must use approved production-load and attack-rate profiles. Do not invent thresholds in test code.

## 18. Acceptance criteria

- [ ] IAM-001 through IAM-011 and US-005 through US-009 trace to contracts, implementation and tests.
- [ ] Identity-provider, token/channel and authorisation-distribution ADRs are approved before their internals are implemented.
- [ ] All actor types are explicit; service and human grant models cannot be mixed.
- [ ] Password/MFA/lockout/session policies are configurable within approved security minima.
- [ ] Sessions refresh, expire and revoke safely, including provider/local failure and token reuse.
- [ ] Roles use stable permissions and immutable approved versions rather than code branches on role names.
- [ ] Contextual scopes and conflict-of-duty rules are enforced under concurrency.
- [ ] Maker self-approval is denied across roles, sessions and channels.
- [ ] Entitlement invalidation meets the approved objective and fails closed for privileged/financial operations.
- [ ] Trusted-device registration requires step-up and revocation terminates bound sessions.
- [ ] Payload encryption distributes only public keys, keeps private operations in KMS/HSM and retains mandatory TLS.
- [ ] Cross-tenant, enumeration, replay, mass-assignment and sensitive-data leakage tests pass.
- [ ] Authentication, authorisation and privileged changes produce immutable, redacted evidence.
- [ ] Dashboards, alerts, runbooks, backup/restore and provider reconciliation evidence are complete.

## 19. Definition of Done

- Repository build, formatting, analyzers, architecture tests and affected test suites pass.
- Versioned OpenAPI and event schemas are reviewed for compatibility and data minimisation.
- EF Core migrations apply to an empty database and the supported upgrade path; verification queries pass.
- Approved identity provider is exercised in a non-production integration environment with synthetic actors.
- One customer, one staff, one service and one privileged flow pass end to end.
- One financial and one non-financial business service enforce permission, scope and entitlement freshness independently.
- Threat model, security review and penetration-test findings required for launch are closed or explicitly accepted.
- No secret, token, password, OTP, private key, decrypted payload or prohibited PII appears in repository content or captured evidence.
- Revocation, entitlement-gap, provider-outage, KMS-outage and restore runbooks are demonstrated.
- Traceability records requirement/story, ADR, contract, migration, code, test and operational evidence.

## 20. Open decisions

| Decision | Impact | Owner | Required by | Status |
| --- | --- | --- | --- | --- |
| Identity provider and licensing/hosting model | Credential, MFA, token, HA and operational design | Architecture/Security/Product | Slice 1 | Keycloak selected in ADR-0008; hosting, support and operations remain a production gate |
| Channel-specific OAuth/OIDC flows and token transport | Web/mobile/staff/USSD/chat security | Security/Channel owners | Slice 1 | Protocol boundary approved in ADR-0008 and threat model; each deployed channel still requires a token-storage review |
| Token lifetimes, refresh rotation and revocation mechanism | Session UX and breach containment | Security | Slice 1 | Approved by ADR-0008; environment values remain configuration evidence |
| Service-to-service authentication mechanism | Workload identity, rotation and platform dependency | Security/Platform | Slice 1 | Client credentials and audience-scoped service permissions approved by ADR-0008 |
| Entitlement distribution/read-model strategy | Latency, availability and consistency | Architecture/Security | Slice 1 | Versioned snapshot call, consumer projection/cache and durable invalidation event approved by ADR-0009 |
| Maximum entitlement/revocation propagation window | Residual access risk and cache settings | Security/Risk | Slice 1 | Default 60 seconds, hard maximum five minutes; privileged/financial paths require fresh source resolution |
| Platform security floors for passwords, lockout and MFA | Institution configurability boundary | Security | Slice 4 | Open |
| Approved MFA and recovery methods per actor/channel | Provider dependency and assurance | Security/Product | Slice 6 | Open |
| Initial system permission catalogue and role templates | Launch operations and least privilege | Domain owners/Security | Slice 7 | Open |
| Role/grant approval thresholds and conflicts | Segregation of duties | Security/Operations | Slice 7 | Open |
| Device-key protocol, trust duration and privacy fields | US-009 security/privacy | Security/Mobile/Privacy | Slice 11 | Open |
| Payload-envelope format, algorithms and replay policy | US-008 interoperability/security | Security/Architecture | Slice 12 | Open |
| KMS/HSM product and key-operation boundary | Private-key custody, latency and recovery | Security/Platform | Slice 12 | Open |
| Authentication/security evidence retention | Storage, investigation and regulation | Security/Compliance | Before migration approval | Open |
| Authentication and authorisation SLOs/load profile | Capacity and release gates | Product/Platform | Before performance test | Open |

## 21. Prohibited assumptions

- Do not select or emulate a production identity provider without the approved ADR.
- Do not implement a custom OAuth, OIDC, password, MFA, biometric, token-signing or cryptographic protocol.
- Do not infer that every channel can use the same authentication flow, token transport or session lifetime.
- Do not invent final banking roles, permission grants, conflict rules, approval thresholds or wildcard administration.
- Do not use role names as authorisation conditions in business code.
- Do not trust role, tenant, identity, permission, device, approval or assurance values supplied by a client.
- Do not place the entire permission catalogue or sensitive resource scopes in access tokens.
- Do not allow stale entitlements for privileged or financial actions when freshness cannot be proven.
- Do not equate device fingerprint, phone number, WhatsApp account, USSD session or possession of a bearer token with sufficient authorisation.
- Do not persist raw biometric data, passwords, OTPs, recovery codes, bearer tokens, refresh tokens or private keys in ordinary tables.
- Do not decrypt selected fields at a broad gateway and forward unrestricted plaintext unless the approved threat model explicitly requires it.
- Do not make payload encryption optional when a field/policy requires it, and do not treat it as a replacement for TLS.
- Do not reactivate access automatically after restore, event replay or provider recovery without reconciliation.
- Do not implement Phase 2 access-review campaign orchestration as incidental scope.

While decisions remain open, keep provider-specific implementations behind ports, use synthetic adapters in development, deny unsupported flows, and record the decision dependency explicitly.

## 22. Agent implementation instructions

- Required reading: root `AGENTS.md`, `README.md`, architecture document, Phase 0 identity foundation and evidence, Phase 1 institution epic, this epic, IAM/SEC/AUD requirements, US-005..US-009 and approved ADRs.
- Expected C# boundaries: `CoreBanking.IdentityAccess.Domain`, `.Application`, `.Infrastructure`, `.Api`, `.Contracts` and `.Tests` plus deliberately small domain-neutral authentication/authorisation primitives where already approved.
- Keep provider SDK models inside Infrastructure. Domain/Application must depend on explicit ports and platform contracts.
- Do not reference another service's EF entities, infrastructure project or database.
- Start every slice with traceable failing tests. Include cross-tenant, wrong-scope, stale-entitlement and sensitive-output negative tests for every protected path.
- Use `TimeProvider`, strongly typed IDs, nullable reference types, cancellation tokens for I/O and optimistic concurrency unless a documented invariant requires stronger control.
- Use EF Core migrations owned by Identity and Access and Testcontainers for PostgreSQL/infrastructure integration evidence.
- Use transactional outbox/inbox for authoritative access events. Never publish a grant or revocation fact before commit.
- Stop for a decision if work would lock in an identity provider, token transport, cryptographic algorithm, KMS/HSM, revocation window, MFA method, permission grant, conflict rule or security threshold that is still open.
- Required completion report: requirement/story coverage, ADRs, permissions, API/event versions, migrations, configuration, tests/commands run, security evidence, assumptions, open risks and rollout/rollback notes.

