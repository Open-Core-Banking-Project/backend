---
decision: P1-E02-D11
title: Device-key protocol, trust duration, privacy fields and device permissions
epic: P1-E02 Identity, Roles and Permissions
owners:
  - Security
  - Mobile
  - Privacy
required_by: Slice 11
status: Approved
approved_on: 2026-10-31
brief_date: 2026-10-31
related: [P1-E02-D08, ADR-0008]
decisions: [P1-E02-D11]
---

# Decision brief: trusted devices, keys, trust duration and privacy

## 1. Decision

| # | Question | Answer |
| --- | --- | --- |
| D11-A | What is a trusted device? | A user-visible registration of one client key for one identity in one institution, with its own lifecycle (`pending_step_up` -> `trusted` -> `suspended` -> `trusted` / `revoked` / `expired`). It is a trust signal and a policy condition, never an authentication factor by itself |
| D11-B | Which key material is accepted? | Only a **public-key thumbprint**: SHA-256 over the exported public key, base64url encoded. No private key, no secret, no attestation claim and no raw device signal is sent or stored. The client keeps its private key |
| D11-C | What must precede trust? | A fresh, unconsumed, session-bound step-up for purpose `identity.device.register`, produced after the registration was started with that thumbprint. Absence is a refusal (`device_step_up_required`), never a warning |
| D11-D | What is the duplicate-key policy? | One trusted registration per institution, identity and thumbprint. Re-registering while trusted is idempotent. After revocation the same key may only return as a **new** device identity with a fresh step-up: a revoked registration never becomes trusted again |
| D11-E | How long does trust last? | Trust is **effective-dated**, never a permanent flag: standard identities 180 days, privileged or administrative identities 30 days. Expiry is evaluated when the device is used or read; an expired device behaves as untrusted and the person registers again. Values are configuration with these approved defaults |
| D11-F | Which privacy fields are stored? | A user-supplied **display name** (untrusted free text, trimmed, at most 64 characters, never used for authentication), a **platform code** from a fixed set and the thumbprint. No OS version, advertising identifier, IP address or location; risk scoring belongs to Risk and Reporting and receives the versioned events only |
| D11-G | What does revocation do? | It requires a reason, is idempotent per device, and revokes **every active session bound to that device in the same transaction**, marking each for provider revocation and publishing the revocation facts. A revoked device cannot be re-trusted or refreshed |
| D11-H | Who may do what? | `identity.device.manage` for a person's own devices (start/completion/list/revoke), `identity.device.view` for support officers reading another identity's devices (audited), `identity.device.revoke` for security operators revoking any device in their institution with a recorded reason |

## 2. Events

| Event | Trigger | Minimal payload |
| --- | --- | --- |
| `TrustedDeviceRegistered.v1` | Trust becomes active | device id, identity id, institution id, platform code |
| `TrustedDeviceRevoked.v1` | A device is revoked | device id, identity id, institution id, reason category |
| `SessionsRevokedForDevice.v1` | Sessions were terminated because their device was revoked | device id, identity id, institution id, revoked session count |

## 3. Constraints

- The display name is untrusted input: it is never parsed, executed or used to select policy, and it cannot contain a secret.
- Signals and fingerprints alone never authenticate and never substitute for a permission or a step-up.
- Expiry is evaluated, not written by a background sweep; a sweep may record evidence but is not required for correctness.
- Revocation is fail-closed: if session termination fails, the device stays revoked and the operation is retried, never partially trusted.
- Nothing here invents a key-attestation, jailbreak-detection or biometric requirement.

## 4. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Attestation (Play Integrity / DeviceCheck) as an additional signal | Security and Mobile | Requires the mobile platform decision and provider accounts |
| Per-institution trust durations and privileged overrides | Security | Approved defaults exist; institution overrides are administration data |
| Risk scoring of device signals | Risk and Reporting | Risk owns the model; Identity publishes events only |
| Device-bound token audience per channel | Security and Channel owners | Channel deployment reviews |

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved public-key thumbprints only, step-up-gated registration, effective-dated trust (180/30 days), untrusted display metadata, transactional revocation with bound sessions, and the three device permissions | 2026-10-31 |
| Security | | Named confirmation required before any client channel enables device trust | |
| Privacy | | Named confirmation required on the display-name field before production enrolment | |

## 6. Implementation notes (2026-10-31)

Two deviations from the epic's route and permission table were required when the API endpoints were wired. Both
keep the decisions above intact.

| # | Deviation | Reason |
| --- | --- | --- |
| 1 | Self management is `identity.device.manage`, not the epic's `identity.device.self.manage` | `PermissionIdentifier` accepts exactly `domain.resource.action`, so the four-segment identifier cannot be catalogued. The permission catalogue, the seed migration and every route agree on the three-segment form |
| 2 | Two routes were added: `GET /api/v1/identity/devices/identities/{identityId}` (`identity.device.view`) and `DELETE /api/v1/identity/devices/{deviceId}/administrative` (`identity.device.revoke`) | D11-H grants officers a read of another identity's devices and operators a revocation in their institution; the epic's four self-service routes cannot express either, and `identity.device.view` would otherwise be catalogued without a use |
