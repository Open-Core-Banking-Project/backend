---
id: ADR-0008
title: "Authentication provider, token model and service identity"
status: Proposed
date: 2026-10-16
owners: [Architecture, Security, Identity and Access]
required_by: "Phase 0 - Identity and Permission Foundation; Phase 1 - Identity, Roles and Permissions"
architecture_item: 8
referenced_by:
  - docs/implementation/phase-0-engineering-foundation/03-identity-and-permission-foundation.md
  - docs/implementation/phase-1-banking-foundation/02-identity-roles-and-permissions.md
  - docs/implementation/phase-1-banking-foundation/01-institution-and-tenancy.md
supersedes: []
approved_by: []
approved_on: null
---

# ADR-0008: Authentication provider, token model and service identity

## Status

`Proposed` - not approved. `02-identity-roles-and-permissions.md` requires this ADR, together with [ADR-0009](ADR-0009-roles-permissions-contextual-scopes-and-invalidation.md), to be approved before production authentication and authorisation enforcement. `03-identity-and-permission-foundation.md` lists it as a dependency.

## Context

- `02-identity-roles-and-permissions.md` requires the approval of "ADR-0008 identity provider/token/channel model" and "ADR-0009 authorisation distribution/invalidation model", and requires a threat model alongside them. Its use cases include authenticating a human actor, refreshing or revoking a session, registering or revoking a trusted device, decrypting a selected authentication payload field, and authenticating a service client.
- `AGENTS.md` (`## Roles and permissions`, `## Security and privacy`) requires deny-by-default enforcement in every business service, service identities that use separate service permissions and never inherit human roles, step-up authentication where policy requires, no custom cryptography, and no logging of passwords, PINs, OTPs, tokens or keys.
- `BACKEND_ARCHITECTURE.md` lists this decision as required ADR 8 and lists "Identity provider and licensing requirements" and "Service-to-service authentication mechanism" as awaiting approval.
- `03-identity-and-permission-foundation.md` requires authentication and authorisation abstractions, permission policies, tenant context and negative tests.
- Requirement and story traceability: `IAM-001` to `IAM-011`, `SEC-002` to `SEC-013`, `SEC-016`, `SEC-017`, `US-005` to `US-009`, `US-010`.

## Decision

Not yet decided. This ADR must settle:

- Identity provider choice (hosted, self-hosted or built-in ASP.NET Core Identity) and its licensing implications.
- Token model: access and refresh token format, lifetime, rotation, revocation, and the claims required to carry institution, actor, session and device context without bloating the token.
- Session and device model: session lifecycle, trusted-device enrolment and revocation, and the step-up authentication mechanism for sensitive operations.
- Service identity and service-to-service authentication: client-credential or certificate-based identity, token audience and scope model, and how service permissions stay separate from human roles.
- Channel authentication boundaries: how USSD, WhatsApp and chatbot identities link to a customer record, and the rule that a phone number or chat identity is not sufficient authentication on its own (coordination point with [ADR-0012](ADR-0012-channel-security-boundaries.md)).
- Secret, key and certificate custody for authentication material (coordination point with [ADR-0011](ADR-0011-secret-key-and-certificate-management.md)).
- Portal or admin authentication for staff, including privileged access and review requirements.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| Custom token issuance and password storage | Prohibited; custom cryptography is not permitted |
| A single shared token for human and service callers | Breaks the separation between human roles and service permissions |
| Long-lived tokens without rotation or revocation | Conflicts with entitlement invalidation and step-up requirements |
| Channel identity accepted as authentication | Contradicts the channel-security rules in `AGENTS.md` |

## Consequences

- Token claims determine what every service can authorise locally without a synchronous call, so claim design constrains all later epics.
- Identity provider licensing, availability and outage behaviour become platform dependencies.
- Device and step-up decisions affect channels, payments, cards and privileged access epics.

## Compliance and evidence

- Negative tests for missing, expired, revoked and forged tokens; cross-institution token use; service token used as a human identity.
- Threat model covering token theft, replay, device enrolment abuse and step-up bypass.
- Architecture tests that fail when a business service accepts identity from an untrusted source.

## Open questions

- Whether multi-factor authentication is mandatory for all staff roles or only privileged ones.
- Whether tokens carry institution context or the institution is resolved server-side from the session.
- Whether an external identity provider is acceptable for Phase 1 or only for production.

## Approval gate

Do not implement production authentication, token issuance, device enrolment or service-to-service credential handling until this ADR is `Accepted`. Abstractions, ports and tests may be built against a replaceable local implementation.

## Related documents

- [ADR index](README.md)
- [ADR-0009](ADR-0009-roles-permissions-contextual-scopes-and-invalidation.md) - authorisation model that consumes the token claims
- [ADR-0011](ADR-0011-secret-key-and-certificate-management.md) - key and certificate custody
- [ADR-0012](ADR-0012-channel-security-boundaries.md) - channel identity linking and step-up
- [02-identity-roles-and-permissions.md](../implementation/phase-1-banking-foundation/02-identity-roles-and-permissions.md)
