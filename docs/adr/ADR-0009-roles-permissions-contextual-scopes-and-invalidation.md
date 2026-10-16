---
id: ADR-0009
title: "Roles, permissions, contextual scopes and entitlement invalidation"
status: Proposed
date: 2026-10-16
owners: [Architecture, Security, Identity and Access]
required_by: "Phase 0 - Identity and Permission Foundation; Phase 1 - Identity, Roles and Permissions"
architecture_item: 9
referenced_by:
  - docs/implementation/phase-0-engineering-foundation/02-local-infrastructure.md
  - docs/implementation/phase-0-engineering-foundation/03-identity-and-permission-foundation.md
  - docs/implementation/phase-1-banking-foundation/01-institution-and-tenancy.md
  - docs/implementation/phase-1-banking-foundation/02-identity-roles-and-permissions.md
supersedes: []
approved_by: []
approved_on: null
---

# ADR-0009: Roles, permissions, contextual scopes and entitlement invalidation

## Status

`Proposed` - decision drafted and ready for owner approval. Effective only when the owners listed below record `Accepted`. Until then no production authorisation enforcement, role storage or grant assignment may be implemented; policies, ports and negative tests may be built against a replaceable local implementation.

## Context

- `AGENTS.md` (`## Roles and permissions`) requires every protected operation to declare a stable capability permission, forbids hard-coded role names, requires roles to be configurable bundles owned by Identity and Access, requires contextual scopes, requires deny-by-default when freshness cannot be proven, requires service identities to use separate service permissions, and requires a maker not to approve their own restricted action.
- `BACKEND_ARCHITECTURE.md` (`## Roles and permissions model`) defines ownership and enforcement, permission naming, contextual scopes, segregation of duties, token and entitlement behaviour and authorisation evidence.
- `03-identity-and-permission-foundation.md` defines the access-control model, foundation permissions, contextual scopes, the required contracts and a negative-test matrix.
- `02-identity-roles-and-permissions.md` defines session, role-version, grant, device and authorisation-evaluation use cases, including entitlement invalidation.
- Requirement and story traceability: `IAM-001` to `IAM-011`, `SEC-002` to `SEC-013`, `WFL-001` to `WFL-008`, `AUD-001` to `AUD-003`, `US-005` to `US-010`, `US-048` to `US-050`.

## Decision

1. **Permission naming.** `<domain>.<resource>.<action>` in lower case, for example `payment.transfer.initiate` or `ledger.manual_journal.create`. The domain segment identifies the owning bounded context, the resource segment the concept, and the action segment is drawn from an approved verb list (`view`, `create`, `update`, `delete`, `approve`, `reject`, `execute`, `manage`, `override`, `initiate`). Permission identifiers are permanent and are never renamed; a replacement permission is added and the old one retired.
2. **Ownership.** The service that owns a resource defines and enforces its permissions. Identity and Access owns the catalogue, role composition and grant records. A permission granted by no role is inert. Adding a permission requires review by the owning service and Identity and Access; removing one requires a check that no published role grants it.
3. **Role model.** Roles are institution-scoped, versioned and effective-dated. A role version is an immutable set of permission grants; changing a role publishes a new version rather than editing a published one. Publishing a role version requires maker-checker approval and audit evidence. Retiring a version requires a check that no active assignment references it.
4. **Assignments.** An assignment binds an actor to a role version with a contextual scope and a validity window, and records the assigning actor. Temporary or privileged assignments require an expiry and are subject to access review.
5. **Contextual scopes.** Supported scope dimensions: institution (always required), branch, channel, customer or account, corporate mandate, amount and currency limit, transaction-count limit, device, and authentication strength. Scopes are evaluated server-side in the owning service. A scope that cannot be resolved is a denial, not a wildcard.
6. **Token contents.** Access tokens carry actor identity, actor type, institution, session, device, entitlement version and authentication strength. They do not carry the permission set, which keeps tokens small and revocation fast.
7. **Entitlement resolution.** Each service resolves permissions locally from an entitlement snapshot contract owned by Identity and Access, keyed by actor and institution and versioned by entitlement version. Snapshots are cached with a bounded time to live (default 60 seconds, configured per environment and never longer than five minutes). A snapshot that is missing, stale beyond the window, or version-mismatched with the token results in denial.
8. **Invalidation.** Revoking a session, device, grant, role version or institution assignment increments the entitlement version for the affected actor or institution. Services detect the change on the next snapshot refresh or immediately when the incoming token carries a newer version. Step-up and privileged operations force a fresh snapshot rather than using the cache. The maximum invalidation window is the configured cache lifetime and is documented per service.
9. **Segregation of duties.** A maker cannot approve their own restricted action: the check compares actor identity, not session. Conflict-of-role rules are configuration data, not code branches, and apply even when an actor holds multiple roles. Overrides require a stated reason and produce audit evidence.
10. **Service identities.** Service-to-service callers use distinct service permissions in their own namespace and never inherit human roles. A service token presented as a human identity, or a human token presented for a service-only permission, is rejected.
11. **Enforcement point.** Each service enforces permissions in a single shared policy component used by its API and its background workers, so a message handler cannot bypass a check that an endpoint performs. Endpoints declare their required permission; an endpoint without a declared permission fails the architecture tests.
12. **Evidence.** Sensitive authorisation decisions record actor, permission, scope, decision, reason, entitlement version and correlation identifier, using the audit envelope in [ADR-0010](ADR-0010-observability-audit-evidence-redaction-and-retention.md). Secrets and unnecessary personal data are excluded.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| Role-name checks in business code | Prohibited; unconfigurable and unauditable |
| Permissions embedded in the token | Large tokens, slow revocation, and stale grants on every request |
| A synchronous authorisation call per operation | Adds a hard dependency and latency to financial paths with no approved availability model |
| Long-lived tokens with no version comparison | Conflicts with the invalidation and deny-by-default requirements |
| One global permission catalogue with no per-service ownership | Naming collisions and unclear review authority |

## Consequences

- **Enables:** a stable permission vocabulary, fast revocation through versioning, and testable authorisation with negative cases.
- **Constrains:** every service must implement the policy component, the snapshot cache and the version comparison; every protected operation must declare a permission; role changes become versioned, approved, audited acts.
- **Cost:** role versioning and access review add governance work; the entitlement snapshot is a service dependency that must degrade safely.
- **Out of scope:** the authentication mechanism and token issuance, which belong to [ADR-0008](ADR-0008-authentication-provider-token-model-and-service-identity.md).

## Compliance and evidence

- The negative-test matrix from `03-identity-and-permission-foundation.md`: missing permission, wrong institution, expired grant, revoked role, scope escalation, self-approval and stale entitlement version.
- Architecture tests failing when an endpoint lacks a declared permission, when business code contains a role-name comparison, or when a worker bypasses the policy component.
- Snapshot freshness tests: denial when the snapshot is stale beyond the window, and immediate effect after an entitlement version bump.
- Recorded evidence for approval: the permission-naming convention, the role and grant model, and one worked invalidation test.

## Open questions

- How the entitlement snapshot is delivered: projection table, token extension, or a versioned contract call.
- The default cache lifetime per service class, and whether privileged services use zero caching.
- Whether the platform ships default role bundles per institution type or requires per-institution configuration.
- Whether conflict-of-role rules are configured per institution or platform-wide.

## Approval gate

Do not implement production authorisation enforcement, role storage, grant assignment or invalidation until this ADR is `Accepted`. Policies, ports and negative tests may be built against a replaceable local implementation.

Approval required from **Architecture**, **Security** and **Identity and Access**. Record acceptance by setting `status: Accepted` and filling `approved_by` and `approved_on`, then updating the [index](README.md).

## Related documents

- [ADR index](README.md)
- [ADR-0008](ADR-0008-authentication-provider-token-model-and-service-identity.md) - token claims that carry actor, session, device and entitlement version
- [ADR-0010](ADR-0010-observability-audit-evidence-redaction-and-retention.md) - authorisation audit evidence and retention
- [03-identity-and-permission-foundation.md](../implementation/phase-0-engineering-foundation/03-identity-and-permission-foundation.md)
- [02-identity-roles-and-permissions.md](../implementation/phase-1-banking-foundation/02-identity-roles-and-permissions.md)