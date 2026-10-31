---
decision: P1-E02-D12
title: Payload-envelope format, algorithms and replay policy
epic: P1-E02 Identity, Roles and Permissions
owners:
  - Security
  - Architecture
  - Mobile
required_by: Slice 12
status: Approved
approved_on: 2026-10-31
brief_date: 2026-10-31
related: [P1-E02-D13, ADR-0011, ADR-0012, ADR-0006]
decisions: [P1-E02-D12]
---

# Decision brief: protected payload envelopes

## 1. Decision

| # | Question | Answer |
| --- | --- | --- |
| D12-A | What is the envelope? | One JSON object, `ProtectedPayloadEnvelopeV1`, with exactly these members: `schemaVersion`, `keyId`, `keyVersion`, `algorithm`, `purpose`, `audience`, `field`, `issuedAt`, `replayId`, `wrappedKey`, `nonce`, `ciphertext`, `tag`. Hybrid encryption: the client generates a fresh content key, encrypts the one field with it and wraps the content key with the institution's published wrapping public key |
| D12-B | Which algorithms are approved? | Exactly one suite: `RSA-OAEP-256+A256GCM` — RSA-OAEP with SHA-256 for the content-key wrap, AES-256-GCM with a 96-bit nonce and 128-bit tag for the field. AES-256-GCM is the primitive already approved by [ADR-0011](../../../adr/ADR-0011-secret-key-and-certificate-management.md) for field protection. Any other `algorithm` value is refused generically |
| D12-C | How is ciphertext bound to its context? | Authenticated additional data is the canonical ASCII string `v1|purpose|audience|field|keyId|keyVersion|issuedAt`. A ciphertext produced for another purpose, audience, field, key version or issue time fails authenticated decryption instead of being silently accepted |
| D12-D | How is replay prevented? | `replayId` is a client CSPRNG value of at least 128 bits, base64url encoded. The first successful structural validation claims `(institution, purpose, replayId)` in `security.envelope_replay_records`; a second use inside the window is refused with the generic code. Records live for 15 minutes and are purged opportunistically on write, so no sweep is required for correctness |
| D12-E | What is the time policy? | `issuedAt` must be within 120 seconds of server time in either direction. There is no long validity window: an envelope is single-use, so a wider window would only help an attacker |
| D12-F | What size limits apply? | `wrappedKey` at most 512 bytes, `nonce` exactly 12 bytes, `tag` exactly 16 bytes, `ciphertext` at most 4096 bytes. The limits keep an authentication field bounded and stop the endpoint becoming a general-purpose decryption oracle |
| D12-G | Which failure codes are returned? | Only the four the epic names: `payload_encryption_invalid` (shape, algorithm, binding, time, replay or authenticated-decryption failure, deliberately indistinguishable), `payload_key_expired`, `payload_key_revoked` and `payload_decryption_unavailable`. No cryptographic detail, ciphertext, key material or plaintext ever appears in a response, log or event |
| D12-H | Which fields are protected first? | One flow in Phase 1: the multi-factor factor response on `POST /api/v1/mfa/step-up` (`purpose` `identity.step_up.response`, `audience` `identity-access`, `field` `response`). The allow-list of protected fields is data on the operation, not a client choice, and additional fields need their own decision |

## 2. Envelope example

```json
{
  "schemaVersion": 1,
  "keyId": "pe-7f31d2",
  "keyVersion": 2,
  "algorithm": "RSA-OAEP-256+A256GCM",
  "purpose": "identity.step_up.response",
  "audience": "identity-access",
  "field": "response",
  "issuedAt": "2026-10-31T18:04:11Z",
  "replayId": "Yk3s0Qm9t7vX2pLr8ZcW1A",
  "wrappedKey": "base64url…",
  "nonce": "base64url-12-bytes",
  "ciphertext": "base64url…",
  "tag": "base64url-16-bytes"
}
```

## 3. Constraints

- Payload encryption is additional to TLS, authentication, authorisation and input validation. It never replaces any of them.
- Decryption happens on the server only for an allow-listed `purpose`, `audience` and `field`; a caller cannot nominate its own.
- Plaintext lives for the duration of one operation, is never persisted, logged, echoed in an error or written to an event, and the operation clears the buffer where the runtime allows it.
- The unwrapped content key is used once and is never cached.
- Public-key discovery returns only public parameters and active key versions.

## 4. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Protecting further fields (for example card or identity capture) | Security and Product | Each field needs its own threat review and payload-size budget |
| Client-side key pinning and public-key caching duration | Security and Mobile | Channel deployment reviews |
| Post-quantum or hybrid signature suite | Architecture | Requires a platform-wide cryptography decision |

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved one hybrid envelope suite (`RSA-OAEP-256+A256GCM`), authenticated context binding, a 15-minute replay window with a 120-second issue-time skew, bounded sizes, the four named failure codes, and one protected field (the step-up factor response) in Phase 1 | 2026-10-31 |
| Security | | Named confirmation required before any client channel sends production envelopes | |
| Mobile | | Named confirmation required on the client crypto implementation review | |
