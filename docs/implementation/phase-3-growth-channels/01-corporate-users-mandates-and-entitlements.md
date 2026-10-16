---
phase: "Phase 3 - Growth Channels"
epic: "Corporate Users, Mandates and Entitlements"
owning_service: "Identity and Access with Customer and Onboarding"
modules: [Corporate Identities, Delegated Administration, Account Entitlements, Signing Mandates]
status: Draft
requirements: [CHN-004, IAM-001, IAM-004..IAM-010, SEC-009, SEC-010, AUD-001]
stories: [US-059]
dependencies: [Phase 2 Corporate Party Onboarding, Phase 2 Privileged Access Review]
adrs: [Corporate identity and mandate ownership, Delegated administration]
last_updated: 2026-10-16
---

# Epic 01: Corporate Users, Mandates and Entitlements

## 1. Objective

Let authorised corporate administrators create users and assign account entitlements, limits and single/joint/multi-level signing rules without exceeding bank-approved authority.

## 2. Success measures

- Bank ceilings bound every delegated grant and mandate change.
- Mandate versions reproduce signer groups, counts/order, accounts, limits and effective dates.
- Corporate and bank approval applies where configured; revocation/expiry invalidates current access.
- Every sensitive change and use is traceable to identity, corporate party and mandate version.

## 3. Scope

Included: corporate identity invitation/linking, delegated admin, account/resource grants, limits, signer groups, mandates, approval, expiry and review. Excluded: payment submission (Epics 2/3), company onboarding (Phase 2) and treating a signatory as an identity automatically.

## 4. Actors, roles and permissions

| Actor | Permission | Scope | Step-up | Approval |
| --- | --- | --- | --- | --- |
| Bank administrator | `corporate.entitlement.ceiling.manage` | Institution/corporate/accounts | Required | Maker-checker |
| Corporate administrator | `corporate.user.manage` | Within bank ceiling | Required | Corporate/bank policy |
| Mandate manager | `corporate.mandate.manage` | Corporate/account | Required | Maker-checker |
| Auditor | `corporate.access.audit` | Institution/corporate/period | Export step-up | None |

## 5. Domain model

Concepts: `CorporateIdentityLink`, `BankEntitlementCeiling`, `DelegatedAdminGrant`, `CorporateAccountEntitlement`, `CorporateLimit`, `SignerGroup`, `MandateVersion`, `SigningRule`. States: invitation `Pending -> Accepted -> Active -> Suspended/Revoked`; mandate `Draft -> PendingApproval -> Active -> Superseded/Revoked`.

Invariants: user grants are subsets of current bank ceiling; signatory-party reference does not authenticate a user; versions are immutable after activation; maker cannot self-approve; account/currency/amount/channel limits are server enforced; revocation wins over cached entitlement.

## 6. Use cases

Create/invite corporate user; link verified party/identity; grant bounded accounts/actions/limits; define single/joint/multi-level mandate; obtain corporate/bank approval; suspend/revoke/expire access; review current access.

## 7. API contracts

Routes: `POST /corporates/{id}/users`, `/users/{id}/entitlements`, `/corporates/{id}/mandates`, `/mandates/{id}/decisions`, `/users/{id}/suspend`, and bounded queries. Commands use concurrency/version tokens and idempotency keys. Errors distinguish ceiling exceeded, stale mandate, conflict-of-duty, identity mismatch and approval required.

## 8. Event contracts

Produce `CorporateUserInvited/Activated/Suspended`, `CorporateEntitlementChanged`, `CorporateMandateActivated/Superseded`. Consume corporate-party/signatory changes and bank ceiling decisions. Outbox/inbox, minimal PII and ordering by corporate user/mandate are mandatory.

## 9. Database design

`identity_access_db`, schemas `identity`, `access`, `integration`: corporate identity links, ceiling versions, delegated admins, account entitlements, limits, signer groups, mandate references/versions and approval refs. Tenant/corporate scope, unique identity link, effective dates, optimistic concurrency and append-only version history required. Customer party details remain referenced.

## 10. Consistency, concurrency and idempotency

Local grant/mandate state and outbox commit atomically. Revalidate bank ceiling/current signatory/user status on change and use. Concurrent versions use optimistic concurrency. Repeated invitation/change returns prior result. No distributed transaction; pending cross-service validation remains explicit.

## 11. Security and privacy

Strong auth/step-up, least privilege, anti-enumeration, corporate/account scope and sensitive-access audit. Test delegated escalation, cross-corporate/tenant access, stale cache, self-approval, identity-link takeover and mass assignment.

## 12. Audit and observability

Audit ceiling, invitations, links, grants, limits, mandate versions, approvals, revocations and sensitive views. Metrics: active/pending users, stale/expired grants, approval age and denied ceiling/mandate attempts. No identity secrets in telemetry.

## 13. Failure, retry and recovery

Unavailable party/IAM validation denies activation. Failed invalidation leaves change pending and alerts. Replay cannot restore revoked access or duplicate users/versions. Restore verifies active version uniqueness and audit links.

## 14. Configuration

Approve user types, delegated actions, ceilings, limit dimensions, signing algorithms, approval matrix, invitation expiry, session invalidation and review cadence.

## 15. Implementation slices

1. Ownership ADR/contracts/permissions. 2. Corporate identity linking. 3. Bank ceilings/delegated grants. 4. Mandate versions/approval. 5. Invalidation/audit/operations. 6. End-to-end mandate evidence.

## 16. Test plan

| ID | Scenario | Expected |
| --- | --- | --- |
| CME-01 | Grant within ceiling | Active after required approval |
| CME-02 | Corporate admin exceeds ceiling | Denied/audited |
| CME-03 | Single/joint/multi-level mandate | Exact signer policy enforced |
| CME-04 | Stale/revoked signer | Approval denied |
| CME-05 | Maker self-approval | Denied |
| CME-06 | Concurrent mandate changes | One active successor |
| CME-07 | Cross-corporate access | Non-disclosing denial |

## 17. Acceptance criteria

- [ ] Corporate users receive bounded account entitlements, limits and signing rules.
- [ ] Administrators cannot exceed bank-approved entitlements.
- [ ] Single, joint and multi-level mandates work under approved policy.
- [ ] Configured corporate/bank approvals, expiry, revocation and audit are enforced.

## 18. Definition of Done

Repository DoD plus approved mandate ADR, migrations, permission matrix, invalidation/concurrency/security tests, audit dashboard and access runbook.

## 19. Open decisions

Identity linking, bank ceiling, delegated admin, signer groups/order, corporate/bank approvals, limit aggregation, expiry and mandate conflicts require approval.

## 20. Prohibited assumptions

Do not equate party/signatory with login identity, let corporate admins grant bank permissions, infer mandate meaning, or allow stale/revoked authority.

## 21. Agent implementation instructions

Read Phase 2 corporate/IAM epics and approved mandate ADR. Keep party data in Customer and credentials/grants in IAM. Run build, migration, contract, concurrency, invalidation, tenant and security tests. Stop for unresolved mandate/ceiling policy.

