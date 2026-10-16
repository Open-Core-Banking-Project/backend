---
phase: "Phase 0 - Engineering Foundation"
epic: "Identity and Permission Foundation"
owning_service: "Identity and Access"
modules: [Authentication Contracts, Tenant Context, Roles, Permissions, Policy Enforcement]
status: Approved
requirements: [IAM-001, IAM-003, IAM-004, IAM-005, IAM-006, IAM-007, IAM-010, IAM-011, SEC-009, SEC-010]
stories: [US-005, US-006, US-007]
dependencies: [Repository and Build Foundation, Local Infrastructure]
adrs: [ADR-0008, ADR-0009]
last_updated: 2026-10-16
---

# Epic: Identity and Permission Foundation

Approved 2026-10-16 on the instruction of the repository owner, after ADR-0008 and ADR-0009 were accepted. Ready to implement.

## Objective

Establish the platform-wide authentication, trusted tenant context, role, permission and authorisation model that every later backend capability must enforce.

Phase 0 implements the contracts, policies, persistence model and a reference protected operation. It does not complete customer enrolment, password recovery, biometrics, production MFA or institution-specific roles.

## Access-control model

- Roles are configurable bundles of stable capability permissions.
- Application code checks permissions, not role names.
- Identity and Access owns grants, scopes, conflicts, temporary permissions and entitlement versions.
- Every business service enforces permissions for its own resources.
- Permission checks deny by default.
- Human and service identities use separate grant models.
- Tenant identity comes from a trusted authentication context.
- A maker cannot approve their own restricted action.

## Foundation permissions

The first implementation may use synthetic administrative permissions to prove the model:

```text
platform.foundation.execute
institution.tenant.create
identity.role.create
identity.permission.assign
identity.access.review
audit.event.view
```

Do not treat these examples as the complete production permission catalogue.

## Contextual scopes

The authorisation contract must support, without requiring every scope in Phase 0:

- Institution and legal entity.
- Region and branch.
- Channel.
- Customer and account.
- Corporate mandate.
- Currency, amount and transaction limit.
- Device, network, risk and step-up context.
- Temporary or delegated access.

## Persistence foundation

Target database: `identity_access_db`.

Initial tables should cover:

```text
identity.identities
identity.service_clients
identity.sessions
access.roles
access.permissions
access.role_permissions
access.identity_roles
access.resource_scopes
access.permission_conditions
access.conflict_rules
access.temporary_grants
access.entitlement_versions
security.authentication_events
integration.outbox_messages
integration.inbox_messages
```

The actual identity provider may own credential and protocol-specific storage. Do not duplicate password or token storage without an approved reason.

## Required contracts

Define domain-neutral contracts for:

- Authenticated actor identity and type.
- Institution/tenant context.
- Permission requirement.
- Contextual resource and limit scope.
- Authorisation decision with stable denial reason.
- Entitlement version.
- Step-up requirement.
- Service identity and audience.

Do not expose a full permission catalogue in every token. Define an entitlement-version or equivalent invalidation mechanism so permission removal takes effect within an approved window.

## Reference protected flow

```text
Authenticated synthetic administrator
  -> trusted institution context resolved
  -> required permission declared by endpoint/use case
  -> entitlement version validated
  -> permission and contextual scope evaluated
  -> authorised command proceeds or stable denial is returned
  -> sensitive decision is audited
```

## API requirements

- Protected endpoints declare permissions through a consistent policy mechanism.
- HTTP 401 represents missing/invalid authentication.
- HTTP 403 represents authenticated but unauthorised access.
- Problem details use stable error codes without revealing hidden resource existence.
- Client-supplied role, tenant or approval claims are ignored unless validated from the trusted context.

## Test matrix

- Valid identity, tenant, permission and scope succeeds.
- Missing authentication fails.
- Wrong institution fails.
- Missing permission fails.
- Permission with wrong branch/resource scope fails.
- Stale entitlement version fails or refreshes according to policy.
- Service identity cannot use a human role.
- Multiple roles combine only permitted grants and still enforce conflicts.
- Maker cannot approve their own restricted request.
- Negative decisions create appropriate evidence without leaking sensitive data.

## Implementation slices

1. Approve identity-provider and authorisation ADRs.
2. Define actor, tenant, permission and authorisation contracts.
3. Create Identity and Access domain/application foundation.
4. Create EF Core mappings and migrations for roles, permissions, grants and scopes.
5. Implement reference policy evaluator and entitlement invalidation.
6. Integrate one protected reference API.
7. Add audit events, telemetry, negative tests and architecture tests.

## Acceptance criteria

- [ ] Authentication and tenant context are distinct from authorisation.
- [ ] Every reference operation declares a permission.
- [ ] Roles remain configurable bundles rather than code branches.
- [ ] Contextual scopes are enforced server-side.
- [ ] Cross-tenant and missing-permission tests pass.
- [ ] Service and human identities remain separate.
- [ ] Entitlement changes have a defined invalidation path.
- [ ] Sensitive decisions are auditable.

## Open decisions

- Identity provider and licensing.
- Token type, lifetime, refresh and revocation.
- Permission distribution/read-model strategy.
- Entitlement invalidation window.
- Step-up authentication protocol.
- Initial system-defined roles and custom-role policy.

## Prohibited assumptions

- Do not invent final banking roles or grant them broad wildcard permissions.
- Do not use role names as substitutes for capability checks.
- Do not trust tenant or permission fields supplied by clients.
- Do not implement production credential storage before the identity-provider decision.

## Agent instructions

- Stop if the requested change would lock in an unapproved identity provider or licensing model.
- Include negative authorisation and cross-tenant tests with every protected reference flow.
- Do not weaken policies to make integration tests pass.

