---
id: ADR-0011
title: "Secret, key and certificate management and cryptographic boundaries"
status: Proposed
date: 2026-10-16
owners: [Security, Platform Engineering]
required_by: "Phase 0 - Local Infrastructure; Phase 0 - Observability and CI; Phase 1 - Integration Hub and Providers"
architecture_item: 11
referenced_by:
  - docs/implementation/phase-0-engineering-foundation/02-local-infrastructure.md
  - docs/implementation/phase-0-engineering-foundation/05-observability-and-ci.md
supersedes: []
approved_by: []
approved_on: null
---

# ADR-0011: Secret, key and certificate management and cryptographic boundaries

## Status

`Proposed` - not approved. `02-local-infrastructure.md` and `05-observability-and-ci.md` list this ADR. Development-secret substitutes may be used locally, but no production secret or key mechanism may be committed until it is `Accepted`.

## Context

- `AGENTS.md` (`## Security and privacy`) requires approved framework cryptography and central key and secret management, states that custom cryptographic algorithms must never be designed, forbids committing or logging passwords, PINs, OTPs, tokens, private keys, provider secrets, complete identity numbers or full account and card numbers, requires masking of sensitive fields, and requires reporting and rotation when a secret is exposed.
- `AGENTS.md` (`## External providers`) requires signature verification for webhooks and secure storage of raw provider evidence, redacted from ordinary logs.
- `02-local-infrastructure.md` requires a development-secret substitute with explicit safety rules, and `05-observability-and-ci.md` requires dependency and container-image scanning under the selected CI policy.
- `08-integration-hub-and-providers.md` requires a decision on "Secret manager, key management and certificate lifecycle".
- `BACKEND_ARCHITECTURE.md` lists required ADR 11 and places "Development secret storage and production secret-management boundary" among the decisions awaiting approval; `README.md` (`## Secrets and key management`) captures the current expectation.
- Requirement and story traceability: `SEC-007`, `SEC-016`, `SEC-017`, `SEC-018`, `ARC-008`, `NFR-RES-001`, `NFR-DR-001`, `US-072`, `US-079`, `US-080`.

## Decision

Not yet decided. This ADR must settle:

- Secret management for production and non-production: store choice, hierarchy and naming, access model, rotation policy and audit of secret reads.
- Development secret substitute: the local mechanism, its explicit development-only marking, and the guarantee that local defaults cannot reach production systems.
- Key management and cryptographic boundaries: which keys are platform-managed, which are per-institution, where key material lives, and the approved algorithms and libraries per use case (data at rest, data in transit, field-level encryption, tokenisation, webhook signature verification).
- Certificate lifecycle: issuance, custody, expiry monitoring and renewal for provider, correspondent and mutual-TLS integrations.
- Provider credential and certificate storage for the Integration Hub, including reference-only persistence and the prohibition on storing secrets in service databases.
- CI and build secrets, and the scanning rules for dependencies, container images and committed content.
- Incident response for exposed secrets, including rotation expectations and evidence.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| Secrets in service configuration files or environment files committed to the repository | Prohibited and auditable only by review |
| A custom encryption or key-derivation scheme | Prohibited; framework and platform cryptography only |
| One shared key for all institutions and providers | Blast radius and separation of duties are unacceptable for banking |
| Secrets stored in ordinary application tables | Conflicts with the reference-only rule in the Integration Hub epic |

## Consequences

- Every service inherits a secret-access abstraction, configuration contract and startup failure behaviour when a secret is unavailable.
- Rotation, expiry and certificate renewal become operational obligations with alerting and runbooks.
- Data-at-rest, field-level encryption and tokenisation choices constrain database design and key custody in later epics, including card tokenisation.

## Compliance and evidence

- Automated scan evidence showing no secrets in source, images or logs.
- Tests proving local configuration cannot target production endpoints and that a missing secret fails closed.
- Rotation rehearsal and certificate-expiry alert evidence.
- Review checklist for new cryptographic usage.

## Open questions

- Whether a managed secret store, an in-cluster store or a hybrid is used, and the licensing implications.
- How per-institution data-encryption keys are separated if physical isolation is not adopted.
- Which party owns provider certificates and how they are installed during certification.

## Approval gate

Do not commit production secret paths, key material, certificate handling, cryptographic algorithm choices or CI secret wiring until this ADR is `Accepted`. Clearly marked development-only substitutes may continue under `02-local-infrastructure.md`.

## Related documents

- [ADR index](README.md)
- [ADR-0008](ADR-0008-authentication-provider-token-model-and-service-identity.md) - signing keys and token material
- [ADR-0010](ADR-0010-observability-audit-evidence-redaction-and-retention.md) - prohibition on emitting secrets in telemetry
- [ADR-0013](ADR-0013-deployment-migration-backup-restore-rto-and-rpo.md) - secret availability during deployment and recovery
- [08-integration-hub-and-providers.md](../implementation/phase-1-banking-foundation/08-integration-hub-and-providers.md)
