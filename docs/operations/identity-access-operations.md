# Identity and Access operations

Runbook for the Identity and Access service. Every step assumes the operator holds the stated
capability with a completed step-up, and that `identityaccess_app` is the only principal the
service uses at runtime.

## 1. Service clients

Service clients are the only identities that may call this service as a workload. Only an opaque
provider reference is stored; Keycloak holds the credential.

### Register

1. Create the client credential in the provider for the exact audience, for example `corebanking.core-ledger`.
2. `POST /api/v1/service-clients` with the client name, audience, credential reference and expiry.
3. Record the returned `serviceClientId`; the reference itself is never returned again.

### Rotate

1. Create the new credential in the provider and keep the old one valid for a bounded overlap.
2. `POST /api/v1/service-clients/{id}/rotate` with the new reference, its expiry and `previousValidUntil`.
3. After the overlap ends, disable the old credential at the provider.

### Revoke

1. `POST /api/v1/service-clients/{id}/revoke` with a reason.
2. Confirm the client now returns `403 auth.service_identity_not_valid` on any protected call.
3. Remove the credential at the provider.

## 2. Entitlement reconciliation

`IdentityReconciliationWorker` runs hourly; `POST /api/v1/reconciliation/entitlements` runs one pass
on demand. Findings are counts only — no actor, tenant or credential values.

| Finding | Meaning | Action |
| --- | --- | --- |
| `grantHoldersWithoutVersion` | An actor holds a role or temporary grant but has no positive entitlement version | Treat as a broken grant: revoke and re-grant through the API so the version advances; investigate how the row was written |
| `expiredGrantsNotClosed` | A temporary grant passed its end without being closed | The expiry worker clears these within its interval; if the count persists, check the worker is running |
| `expiredServiceClientsActive` | A client is still active with an expired credential reference | Rotate or revoke immediately; this is a stale credential path |
| `staleStepUpEvidence` | Evidence that can no longer authorise anything | Informational; retention cleanup removes it. Growth without use indicates failed step-up attempts |

A non-zero count raises the corresponding `identity.reconciliation.*` counter. Alert on any increase
outside a known change window.

## 3. Step-up and MFA

- Step-up evidence is bound to actor, session, device and purpose, lasts five minutes and is consumed once.
- A user reporting "step-up keeps failing" should be checked for: a paused session, a device change, or a purpose mismatch between the challenge and the operation.
- Enrolment is per factor family; the first verified challenge activates it. A second enrolment for the same family is refused until the first is revoked.
- Recovery remains subject to the approved process in decision `P1-E02-D08`; there is no support-desk override for a privileged factor.
- The factor response may arrive inside a protected payload envelope; section 5 covers its codes and rotation.

## 4. Grants and conflicts

- A grant that creates a prohibited pair is refused with `identity.conflict_of_duty`; nothing is written.
- Existing conflicting grants are reported, never silently revoked. Resolve through an access review with an explicit revocation.
- Every successful grant, revocation and expiry advances the actor's entitlement version and writes an invalidation event. A consumer that keeps allowing with an older version is a defect, not a cache setting.

## 5. Protected payload envelopes

- Clients fetch the wrapping public key from `GET /api/v1/security/public-keys/payload.wrapping` with the
  institution header. The response carries active versions only and is cacheable for a minute; a client that
  caches longer must refresh when a request is refused with `payload_key_expired` or `payload_key_revoked`.
- The one protected field in Phase 1 is the step-up factor response (`purpose` `identity.step_up.response`,
  `audience` `identity-access`, `field` `response`). Other purposes are refused before any work happens.
- Failure codes and what they mean:

| Code | Meaning | First action |
| --- | --- | --- |
| `payload_encryption_invalid` | Malformed, wrongly bound, stale, replayed or unauthentic envelope. Deliberately one answer | Check the client clock, the derived key version and whether the client retried a request verbatim |
| `payload_key_expired` | Key version outside its validity window, or retired beyond the 24-hour overlap | Refresh discovery and re-encrypt |
| `payload_key_revoked` | Key version revoked | Refresh discovery; if the only advertised version is revoked, the institution key must be replaced |
| `payload_decryption_unavailable` | The KMS/HSM boundary is unconfigured or failing | Treat as an outage: no plaintext fallback exists and the client must retry later |

- Rotation order is Active -> Retired -> Revoked. Retiring stops advertising the version immediately while in-flight
  envelopes may still unwrap for 24 hours; revoking refuses the version at once and is the only response to
  suspected compromise.
- Every attempt, accepted or refused, appends a `payload_envelope_accepted` or `payload_envelope_rejected` row in
  `security.authentication_events` with the key identifier, version, outcome and correlation id. Neither ciphertext
  nor plaintext is recorded, so an investigation starts from the correlation id.
- Local development only: `CB_SECRET_IDENTITY_PAYLOAD_WRAPPING_DEVELOPMENT` holds a synthetic PKCS#8 wrapping key
  and `PayloadEncryption:DevelopmentInstitution` names the institution whose key metadata is bootstrapped at
  start-up. Neither value may exist in a deployed environment.

## 6. Alerts

| Signal | Condition | First action |
| --- | --- | --- |
| `identity.reconciliation.grant_holders_without_version` increase | Any increase | Reconcile, then re-grant affected actors |
| `identity.reconciliation.expired_grants_not_closed` | Sustained increase | Verify the expiry worker and its interval |
| `identity.reconciliation.expired_service_clients_active` | Any increase | Rotate or revoke the client |
| Authorisation denials for `auth.service_identity_not_valid` | Sudden increase | Check for an unregistered or expired client, or a wrong audience |
| Step-up denials for `auth.step_up_required` | Sudden increase | Check session/device binding and whether evidence was already consumed |
| Envelope refusals with `payload_decryption_unavailable` | Any sustained rate | Verify the KMS/HSM boundary and its adapter configuration; there is no plaintext fallback |
| Envelope refusals with `payload_encryption_invalid` | Sudden increase | Check client clock skew and whether a client is replaying requests |

## 7. Break-glass

Break-glass for a privileged administrator is a documented out-of-band process: the provider
credential is issued under dual approval, the session is time-boxed, and every action is
reconciled afterwards. It is deliberately not an application endpoint.

## 8. What this runbook does not cover

- Keycloak realm hosting, backup and upgrade: see the provider runbook once the deployment choice is approved.
- SIEM forwarding and retention: depends on the approved platform adapter.
