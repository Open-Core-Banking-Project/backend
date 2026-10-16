---
id: ADR-0011
title: "Secret, key and certificate management and cryptographic boundaries"
status: Accepted
date: 2026-10-16
owners: [Security, Platform Engineering]
required_by: "Phase 0 - Local Infrastructure; Phase 0 - Observability and CI; Phase 1 - Integration Hub and Providers"
architecture_item: 11
referenced_by:
  - docs/implementation/phase-0-engineering-foundation/02-local-infrastructure.md
  - docs/implementation/phase-0-engineering-foundation/05-observability-and-ci.md
supersedes: []
approved_by: [Security, Platform Engineering]
approved_on: 2026-10-16
---

# ADR-0011: Secret, key and certificate management and cryptographic boundaries

## Status

`Proposed` - decision drafted and ready for owner approval. Effective only when the owners listed below record `Accepted`. The local development substitute may proceed under `02-local-infrastructure.md`; production secret, key and certificate mechanisms may not be committed.

## Context

- `AGENTS.md` (`## Security and privacy`) requires approved framework cryptography and central key and secret management, prohibits designing custom cryptographic algorithms, forbids committing or logging passwords, PINs, OTPs, tokens, private keys, provider secrets, complete identity numbers or full account and card numbers, requires masking of sensitive fields, and requires reporting and rotation when a secret is exposed.
- `AGENTS.md` (`## External providers`) requires signature verification and secure storage of raw provider evidence, redacted from ordinary logs.
- `02-local-infrastructure.md` requires a safe development secret mechanism and states that no production credential or copied production configuration is permitted locally.
- `05-observability-and-ci.md` requires dependency and secret checks in the pipeline and states that telemetry must never record secrets. `08-integration-hub-and-providers.md` requires a decision on secret manager, key management and certificate lifecycle.
- Requirement and story traceability: `SEC-007`, `SEC-016`, `SEC-017`, `SEC-018`, `ARC-008`, `NFR-RES-001`, `NFR-DR-001`, `US-072`, `US-079`, `US-080`.

## Decision

1. **No secrets in the repository.** Secrets never appear in source, configuration files, test fixtures, container images, logs or workflow files. Only a committed `.env.example` with placeholder values is permitted. Repository scanning runs in CI once the tooling is selected under [ADR-0010](ADR-0010-observability-audit-evidence-redaction-and-retention.md); until then review and the dependency audit are the gates.
2. **Access through one port.** All secret access goes through an `ISecretProvider` abstraction in the shared infrastructure building block. Services reference secrets by logical name and never read raw environment variables or files directly for secret material. Implementations are a development provider (local user secrets or environment variables, explicitly marked development-only) and a production provider selected by the stakeholders named in `BACKEND_ARCHITECTURE.md`. Swapping the provider must not change service code.
3. **Fail closed.** A missing, expired or unreadable secret fails service startup or the affected operation. There is no silent fallback to a default credential, an empty value or a development value in a production environment.
4. **Approved cryptography.** Only platform cryptography is used: `System.Security.Cryptography` and framework-integrated providers. Custom algorithms, custom key derivation and hand-rolled padding schemes are prohibited. Approved primitives are AES-256-GCM for confidentiality and integrity of stored fields, RSA-2048 or higher, or ECDSA P-256 or higher for signatures, SHA-256 or stronger for digests, and CSPRNG for all random values. Password or PIN verification is delegated to the identity provider under [ADR-0008](ADR-0008-authentication-provider-token-model-and-service-identity.md); if any password material is ever stored by the platform, it uses a memory-hard, salted, versioned scheme.
5. **Key management.** Keys are versioned with a stable key identifier, rotated on a defined cycle and immediately on suspected compromise. Ciphertext records carry the key identifier needed for rotation. Keys are separated by purpose, with distinct material for field-level data encryption, token signing, webhook signature verification and provider credentials. Key material never leaves the secret store or hardware boundary, and is never logged, exported to a non-production environment or copied into test fixtures.
6. **Certificates.** Every certificate and client credential has a named owner, an inventory entry and monitored expiry with alerts at 30, 14 and 7 days. Renewal is a documented runbook step with evidence. Private keys live in the secret store or hardware module; mutual TLS is used for correspondent and provider links that require it.
7. **Provider credentials.** The Integration Hub stores credential and certificate references and metadata only, never secret values in its own tables. Values are resolved at runtime and used only inside the adapter. Raw provider payloads are redacted before logging.
8. **Field-level protection.** Classified identifiers are stored encrypted or tokenised per the classification defined in `AGENTS.md` and the security documentation, with key identifier and algorithm recorded alongside the ciphertext. Card data follows the tokenisation rule in the card epic and never stores CVV or PIN.
9. **Pipeline secrets.** CI uses repository or organisation secrets with least privilege, never echoes them, and prefers short-lived federated credentials where the platform supports it. No production secret is available to pull-request builds from forks.
10. **Rotation and disclosure.** Any secret disclosed outside its boundary is rotated, not merely removed from view, and the rotation is recorded. Deleting the visible string alone is not remediation.
11. **Development.** Development credentials are synthetic and non-functional against production systems. Local defaults cannot reach production endpoints, and the development provider is documented as replaceable.

## Alternatives considered

| Option | Why not chosen |
| --- | --- |
| Secrets in committed configuration or environment files | Prohibited by policy and auditable only by human review |
| Reading secrets directly from environment variables in each service | No central rotation, no origin control, and inconsistent failure behaviour |
| A custom encryption or key-derivation scheme | Prohibited; unauditable and easy to get subtly wrong |
| One shared key per environment | Unacceptable blast radius and no separation of duties |
| Storing provider credentials in service tables | Conflicts with the reference-only rule and widens the exposure surface |
| Deferring key versioning until rotation is needed | Rotation without key identifiers requires re-encrypting everything at once |

## Consequences

- **Enables:** one secret access path, rotation and expiry discipline, and cryptographic choices that a reviewer can verify rather than interpret.
- **Constrains:** every service must resolve secrets through the provider and handle absence as a startup failure; ciphertext must carry key identifiers; certificate expiry becomes an operational obligation with alerts and runbooks.
- **Cost:** secret store integration, key rotation and certificate monitoring are ongoing work; field-level encryption complicates queries on protected columns.
- **Out of scope:** the production secret store product, hardware module selection, and the specific PII subset that requires field-level encryption.

## Compliance and evidence

- Automated test that prohibited values never appear in logs, traces, error responses or test snapshots for a sensitive flow.
- Failure test proving a missing secret stops startup rather than falling back.
- Key rotation rehearsal with evidence, and certificate expiry alerting verified in the environment.
- Review checklist for any new cryptographic usage, and a CI check that no secret-shaped literal is committed.

## Open questions

- Production secret store product and whether a hardware security module is required for PIN or card operations.
- Whether per-institution data-encryption keys are required, and how they are separated during rotation.
- Which personal-data attributes require field-level encryption rather than platform storage encryption.
- Which secret-scanning tool is adopted, pending the tooling decision in ADR-0010.

## Approval gate

Satisfied 2026-10-16 for the secret access path, cryptography, key versioning, certificate lifecycle and provider credential handling. Still outstanding: the production secret store product, any hardware module requirement, per-institution key separation, the subset of personal data that requires field-level encryption, and the secret-scanning tool choice shared with [ADR-0010](ADR-0010-observability-audit-evidence-redaction-and-retention.md).

## Related documents

- [ADR index](README.md)
- [ADR-0004](ADR-0004-database-per-service-and-tenant-isolation.md) - credential separation and field-level protection scope
- [ADR-0008](ADR-0008-authentication-provider-token-model-and-service-identity.md) - signing keys and token material
- [ADR-0010](ADR-0010-observability-audit-evidence-redaction-and-retention.md) - prohibition on emitting secrets in telemetry
- [ADR-0013](ADR-0013-deployment-migration-backup-restore-rto-and-rpo.md) - secret availability during deployment and recovery