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

`Proposed` - not approved. `02-identity-roles-and-permissions.md` requires this ADR together with [ADR-0008](ADR-0008-authentication-provider-token-model-and-service-identity.md) before production authorisation enforcement, and `03-identity-and-permission-foundation.md` lists it as a dependency.

## Context

- `AGENTS.md` (`## Roles and permissions`, `## Security and privacy`) requires every protected endpoint, command, query, workflow action, channel operation and CMS operation to declare a stable capability permission such as `payment.transfer.initiate` or `cms.content.publish`; forbids hard-coding behaviour to role names; requires configurable role bundles owned by Identity and Access; requires contextual scopes for institution, branch, channel, customer or account, corporate mandate, amount, currency, transaction limit, device and step-up; requires Identity and Access to own grants and entitlement versions while each business service enforces access to its own resources; requires deny-by-default when identity, permission, scope or entitlement freshness cannot be proven; requires service identities to use separate service permissions; and requires a maker not to approve their own restricted action with conflict-of-duty rules applying even when a user holds multiple roles.
- `BACKEND_ARCHITECTURE.md` (`## Roles and permissions model`) defines ownership and enforcement, permission naming, contextual scopes, segregation of duties, token and entitlement behaviour and authorisation evidence.
- `03-identity-and-permission-foundation.md` defines the access-control model, foundation permissions, contextual scopes, persistence foundation, required contracts, a reference protected flow, API requirements and a test matrix.
- `02-identity-roles-and-permissions.md` defines use cases for role version publication, role assignment with contextual scope and authorisation requirement evaluation, and a role version and grant lifecycle, together with entitlement invalidation.
- Requirement and story traceability: `IAM-001` to `IAM-011`, `SEC-002` to `SEC-013`, `SEC-016`, `SEC-017`, `WFL-001` to `WFL-008` (maker-checker interaction), `AUD-001` to `AUD-003`, `US-005` to `US-010`, `US-048` to `US-050`.

## Decision

Not yet decided. This ADR must settle:

- Permission naming standard (`domain.resource.action`), the namespace owners and the review gate for new permissions.
- Role model: whether roles are global, institution-scoped or versioned per institution; the role lifecycle (draft, published, effective-dated, retired); and how a role version is published and audited.
- Compilation of grants: whether authorisation decisions are computed from Identity and Access data at request time or compiled into a token or a cached entitlement set, and the resulting staleness window.
- Entitlement invalidation: how a revoked permission, role, scope or institution assignment takes effect, the maximum invalidation delay, and the fail-closed behaviour when freshness cannot be proven.
- Distribution of the authorisation decision: which checks are local to the owning service and which require the Identity and Access contract.
- Segregation-of-duties and conflict-of-role rules, including the multi-role maker scenario and privileged or temporary access approval.
- Authorisation evidence: what is recorded for a sensitive decision and how the permission and scope are captured without logging secrets or unnecessary personal data.
- The foundation permission catalogue for Phase 0 and the process for extending it in later phases.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| Role-name checks inside business code | Prohibited; makes behaviour unconfigurable and unauditable |
| Central synchronous authorisation call for every operation | Introduces a single point of failure and latency on financial paths without an approved availability model |
| Long-lived tokens carrying the full grant set with no invalidation | Conflicts with the entitlement invalidation and deny-by-default requirements |
| One global permission catalogue with no owners | Creates naming collisions and unclear review authority across services |

## Consequences

- Every service inherits a permission catalogue, enforcement middleware or policy layer, and a scope-resolution contract.
- Role versioning and effective dating add governance cost but are required for audit evidence and privileged access review.
- Invalidation behaviour determines whether revocation, suspension or downgrade takes effect immediately or within a stated window, which matters for fraud and offboarding.

## Compliance and evidence

- Test matrix from `03-identity-and-permission-foundation.md`, plus negative tests for missing permission, wrong institution, expired grant, revoked role, scope escalation and self-approval.
- Audit evidence assertions for sensitive decisions.
- Architecture tests that fail when an endpoint lacks a declared permission or hard-codes a role name.

## Open questions

- Whether the platform ships default role bundles per institution type or requires per-institution configuration.
- Whether entitlements are cached per request, per session or per token lifetime.
- How privileged access review campaigns in Phase 2 consume entitlement versions.

## Approval gate

Do not implement production authorisation enforcement, role storage, grant assignment or invalidation until this ADR is `Accepted`. Policies, ports and negative tests may be built against a replaceable local implementation.

## Related documents

- [ADR index](README.md)
- [ADR-0008](ADR-0008-authentication-provider-token-model-and-service-identity.md) - token claims that carry actor, session and scope context
- [ADR-0010](ADR-0010-observability-audit-evidence-redaction-and-retention.md) - authorisation audit evidence and retention
- [02-identity-roles-and-permissions.md](../implementation/phase-1-banking-foundation/02-identity-roles-and-permissions.md)
- [03-identity-and-permission-foundation.md](../implementation/phase-0-engineering-foundation/03-identity-and-permission-foundation.md)
