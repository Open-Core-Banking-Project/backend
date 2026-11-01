---
decision: P1-E02-D13
title: KMS/HSM product and key-operation boundary
epic: P1-E02 Identity, Roles and Permissions
owners:
  - Security
  - Platform Engineering
  - Architecture
required_by: Slice 12
status: Approved
approved_on: 2026-10-31
brief_date: 2026-10-31
related: [P1-E02-D12, ADR-0011]
decisions: [P1-E02-D13]
---

# Decision brief: key-operation boundary

## 1. Decision

| # | Question | Answer |
| --- | --- | --- |
| D13-A | Which KMS/HSM product is chosen? | None is chosen by this decision, and nothing is locked in. The epic's requirement is met by approving the **boundary**: one port, two operations, fail-closed semantics and a replaceable adapter. Product selection stays with Platform Security, which the epic also names as the owner of key custody |
| D13-B | What does the port expose? | `IKeyOperationPort` with `DescribeAsync(keyReference)` returning public parameters only (algorithm, public key, creation time) and `UnwrapAsync(keyReference, algorithm, wrappedKey)` returning the content key. The boundary deliberately has no "decrypt my payload" operation: the HSM only unwraps a bounded content key, so field plaintext never enters the hardware boundary |
| D13-C | Where do key references live? | `security.key_metadata` stores an institution, purpose, key identifier, version, algorithm, public parameters, validity window, state and an **opaque** KMS reference. No private key, no wrapping key and no secret is stored in a service table or in repository content |
| D13-D | What happens when the KMS is unavailable? | The operation fails closed with `payload_decryption_unavailable`. Per-call timeout is 2 seconds, a single non-idempotent operation is never retried automatically, and there is no plaintext fallback and no "try another key" behaviour. Alerting and a circuit breaker are required of the production adapter |
| D13-E | How do development and production differ? | Development uses a synthetic local adapter that performs the same RSA-OAEP unwrap with a non-production wrapping key read through the shared secret provider, so the flow is executable and testable. Every operation of the unconfigured adapter throws, so a production deployment without a provider refuses envelopes instead of degrading silently |
| D13-F | How are keys rotated? | Metadata versions move `Active` -> `Retired` -> `Revoked`. Discovery advertises `Active` versions only; a `Retired` version still unwraps for a bounded overlap (default 24 hours) so in-flight envelopes survive a rotation; a `Revoked` version refuses immediately with `payload_key_revoked`. An expired validity window refuses with `payload_key_expired` |
| D13-G | What is audited? | Every unwrap attempt appends an authentication event with the institution, identity, session, key identifier, key version, purpose, outcome and correlation id. Neither ciphertext, wrapped key, plaintext nor algorithm internals are recorded |
| D13-H | Who owns institution keys? | Institution key creation, custody and rotation remain owned by institution configuration and platform operations, as the epic states. Identity consumes metadata and performs the unwrap call; it never creates, exports or destroys key material |

## 2. Why this shape

| Option | Why not chosen |
| --- | --- |
| Let the KMS decrypt the whole payload | Expands the hardware boundary to arbitrary payloads, gives the HSM an unbounded input, and makes latency a function of field size |
| Share a symmetric wrapping key with every service | Destroys purpose separation and the blast radius that ADR-0011 forbids |
| Choose a product now and code against its SDK | Locks in a vendor before the platform security decision, and the epic names the boundary, not the product |
| Fall back to plaintext when the KMS is down | Removes the control exactly when the system is under stress |

## 3. Constraints

- One identity per key purpose; wrapping keys are never reused for token signing or field storage.
- The adapter receives an opaque reference and an already-wrapped key; it never receives plaintext, the institution, the identity or the purpose.
- Adapters must be swappable without touching domain or application code; the only seam is `IKeyOperationPort`.
- Local development keys are synthetic, documented as non-production and refused by the production registration path.

## 4. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| The KMS/HSM product, region and tenancy model | Platform Security | Provider procurement and data-residency review |
| Hardware module requirement for PIN or card operations | Security | Card epic decision, outside Identity |
| Production circuit-breaker thresholds and alert routing | Platform Engineering | Depends on the selected provider's error taxonomy |

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved the two-operation port (`DescribeAsync`, `UnwrapAsync`), opaque KMS references with metadata-only storage, fail-closed `payload_decryption_unavailable`, a development adapter behind the shared secret provider, and the Active/Retired/Revoked rotation contract with a 24-hour retirement overlap | 2026-10-31 |
| Security | | Named confirmation required on the production adapter before envelopes are enabled | |
| Platform Engineering | | Named confirmation required on provider, region and alerting | |
