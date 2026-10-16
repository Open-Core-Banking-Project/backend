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

`Proposed` - decision drafted and ready for owner approval. Effective only when the owners listed below record `Accepted`. Until then no production authentication, token issuance, device enrolment or service credential handling may be implemented; abstractions, ports and tests may be built against a replaceable local implementation.

## Context

- `02-identity-roles-and-permissions.md` requires this ADR together with [ADR-0009](ADR-0009-roles-permissions-contextual-scopes-and-invalidation.md) to be approved before production authentication and authorisation enforcement, requires a threat model alongside them, and defines use cases for authenticating a human actor, refreshing or revoking a session, registering or revoking a trusted device, decrypting a selected authentication payload field, and authenticating a service client.
- `AGENTS.md` (`## Roles and permissions`, `## Security and privacy`) requires deny-by-default enforcement, service identities that never inherit human roles, step-up authentication where policy requires it, approved framework cryptography, and no logging of credentials, tokens or keys.
- `BACKEND_ARCHITECTURE.md` lists identity provider and licensing requirements and the service-to-service authentication mechanism among the decisions awaiting approval.
- `03-identity-and-permission-foundation.md` requires authentication and authorisation abstractions, permission policies, tenant context and negative tests.
- Requirement and story traceability: `IAM-001` to `IAM-011`, `SEC-002` to `SEC-013`, `SEC-016`, `SEC-017`, `US-005` to `US-010`.

## Decision

1. **Protocol.** The platform is an OAuth 2.1 and OpenID Connect resource server. A hosted or self-hosted OIDC-compliant identity provider issues tokens; the platform does not invent its own token format, signing scheme or password store. The provider product and licensing are an owner decision recorded below; a replaceable local implementation is permitted for Phase 0.
2. **Token type and validation.** Access tokens are signed JWTs using asymmetric keys published through the provider's discovery document. Services validate issuer, audience, signature, expiry and not-before on every request, and reject tokens from another environment or audience.
3. **Token lifetime.** Access tokens are short-lived: default 15 minutes, configurable per environment, and never longer than 60 minutes. Refresh tokens rotate on use with reuse detection; a reused refresh token revokes the session family and raises a security event.
4. **Claims.** Required claims: subject (actor identifier), actor type (`human`, `service`, `partner`), institution, session identifier, device identifier where applicable, entitlement version, authentication method and strength, and audience. Claims carry no permission list and no personal data beyond identifiers, in line with [ADR-0009](ADR-0009-roles-permissions-contextual-scopes-and-invalidation.md).
5. **Sessions.** A session is created on successful authentication, is bound to the authenticated device context, carries an absolute and an idle timeout, and can be revoked by the actor or by an administrator. Revocation is effective no later than the entitlement snapshot window defined in ADR-0009. Session identifiers appear in audit evidence.
6. **Devices.** Trusted devices are registered with verification, named, listed and revocable per actor. Device identity participates in step-up decisions and in anomaly detection. A device registration cannot by itself elevate permissions.
7. **Step-up authentication.** Sensitive operations require step-up: high-value or high-risk payments, beneficiary changes, card activation, credential or entitlement changes, privileged administrative actions, and any operation the owning service marks as restricted. Step-up is re-authentication or a second factor, represented by the authentication-method and strength claims with a short validity, and enforced server-side by the owning service.
8. **Service identity.** Service-to-service calls use client-credentials authentication with a per-service client identifier and programmatically constrained credentials. Service tokens carry `actor_type = service`, carry service permissions only, are audience-scoped to the called service, and never inherit human roles. A service token presented as a human actor, or a human token used for a service-only permission, is rejected and audited.
9. **Channel identity.** USSD, WhatsApp and chatbot identities are linking attributes, not credentials. Linking a channel identity to a customer requires a verified binding, and channel-initiated financial or sensitive actions require an allow-listed intent plus server-side permission checks and step-up where policy requires, consistent with [ADR-0012](ADR-0012-channel-security-boundaries.md).
10. **Staff and privileged access.** Staff authenticate through the same provider with mandatory multi-factor authentication for privileged roles, stricter session limits and device controls. Privileged access is subject to review under the Phase 2 privileged-access epic.
11. **Failure behaviour.** Authentication and authorisation fail closed. Error responses do not reveal whether an account, customer or identifier exists. Only the identity service and provider handle credential material; no other service receives passwords, PINs or one-time codes.
12. **Management surface.** Sessions and devices are listed and revoked through Identity and Access endpoints, matching the session, device and service-client use cases in `02-identity-roles-and-permissions.md`.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| Custom token issuance and password storage | Prohibited; unnecessary risk and forensic burden |
| Provider tokens plus a permission list in the token | Large tokens and slow revocation; contradicts the entitlement-version model |
| One token type for humans and services | Breaks the separation between human roles and service permissions |
| Long-lived tokens without rotation | Conflicts with step-up and invalidation requirements |
| Treating a phone number or chat identity as authentication | Spoofable shared identifier; prohibited by policy |
| Self-hosted provider only, with no abstraction | Locks the platform to one product and complicates Phase 0 development |

## Consequences

- **Enables:** standard protocol behaviour, fast revocation through session and entitlement versioning, and testable negative cases for authentication and service identity.
- **Constrains:** token claims become a compatibility surface; device and step-up behaviour propagates into channels, payments, cards and privileged access; provider availability becomes a platform dependency with a documented failure mode.
- **Cost:** provider licensing, multi-factor authentication enrolment, device management and session operations tooling.
- **Out of scope:** the authorisation model itself (ADR-0009), key custody for signing material (ADR-0011), and channel journey design (ADR-0012).

## Compliance and evidence

- Negative tests: expired, forged, revoked, wrong-audience and wrong-environment tokens; cross-institution token use; service token presented as a human actor; step-up bypass attempt; revoked device and revoked session.
- Threat model covering token theft, refresh reuse, device enrolment abuse, step-up bypass and channel identity spoofing.
- Architecture tests failing when a service accepts identity from an untrusted source or validates tokens without audience checking.
- Recorded evidence for approval: claim inventory, token lifetimes, session and device lifecycle, and a worked step-up flow.

## Open questions

- Identity provider product, hosting model and licensing, including whether a hosted provider is acceptable for a regulated deployment.
- Second-factor method: authenticator application, passkey or short message service, and whether customer-facing authentication uses the same provider as staff.
- Whether refresh tokens are stored server-side for introspection or validated statelessly with reuse detection only.
- Whether partner and corporate clients authenticate through the same provider or a separate delegated surface.

## Approval gate

Do not implement production authentication, token issuance, device enrolment or service credential handling until this ADR is `Accepted`. Abstractions, ports and negative tests may be built against a replaceable local implementation.

Approval required from **Architecture**, **Security** and **Identity and Access**. Record acceptance by setting `status: Accepted` and filling `approved_by` and `approved_on`, then updating the [index](README.md).

## Related documents

- [ADR index](README.md)
- [ADR-0009](ADR-0009-roles-permissions-contextual-scopes-and-invalidation.md) - authorisation model consuming these claims
- [ADR-0011](ADR-0011-secret-key-and-certificate-management.md) - signing keys, client credentials and certificate custody
- [ADR-0012](ADR-0012-channel-security-boundaries.md) - channel identity linking and step-up
- [02-identity-roles-and-permissions.md](../implementation/phase-1-banking-foundation/02-identity-roles-and-permissions.md)