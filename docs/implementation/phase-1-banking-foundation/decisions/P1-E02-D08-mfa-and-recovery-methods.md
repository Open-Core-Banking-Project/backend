---
decision: P1-E02-D08
title: Approved MFA and recovery methods per actor and channel
epic: P1-E02 Identity, Roles and Permissions
owners:
  - Security
  - Product
required_by: Slice 6
status: Approved
approved_on: 2026-10-28
brief_date: 2026-10-28
related: [ADR-0008, ADR-0012]
---

# Decision brief: approved MFA and recovery methods per actor and channel

This brief prepares decision `P1-E02-D08` for signature. It does not make the decision: until Security
and Product sign it, the flows that need the factor stay unavailable and no production enrolment is
claimed.

## 1. The decision requested

| # | Question | What the answer fixes |
| --- | --- | --- |
| D08-A | Which second factors are approved for each actor kind and channel, and what assurance does each yield? | The factor → assurance mapping the platform enforces |
| D08-B | Which recovery paths are approved, and what must be re-proven before access resumes? | Who can regain access, after what evidence, and what is revoked |
| D08-C | Which parts of the policy may an institution configure, inside which approved floors? | Configuration surface versus platform minima (pairs with `P1-E02-D07`) |

## 2. Why the answer is needed now

- It is the only open blocker on `P1-E02` slice 6 (MFA enrolment, challenges and step-up evidence), and slices 7, 9, 10, 11 and 13 depend on assurance meaning something stable.
- Step-up is already enforced in code, not merely declared: `AuthorisationPolicy` denies when a requirement sets `RequiresStepUp` and either step-up is unsatisfied or the actor strength is below `AuthenticationStrength.StepUp`. Many Phase 1 operations already require it — institution create/activate/suspend, branch lifecycle, calendar publication, business-date advancement, provisioning repair, product update/publish, approval decisions, credential and API-client administration, identity create/update.
- The only thing production lacks is the approved meaning of a factor. The Keycloak actor resolver reads an `authentication_strength` claim and rejects `None`; Development substitutes a synthetic adapter. Without D08 there is no approved way to earn `MultiFactor` or `StepUp`.
- ADR-0008's approval gate names "the second-factor method" as still outstanding, and the [identity threat model](../../../security/identity-access-threat-model.md) gate requires "Approved factors, recovery methods and assurance mapping", with the safe state "flow requiring the factor remains unavailable".

## 3. Current implementation state

| Capability | State | Evidence |
| --- | --- | --- |
| Provider | Keycloak selected (ADR-0008, 2026-10-19); realm hosting and operations still a production gate | ADR-0008 |
| Actor assurance | `authentication_strength` claim required; `None` rejected; enum `SingleFactor` < `MultiFactor` < `StepUp` | `KeycloakActorContextResolver`, `AuthenticatedActor` |
| Step-up enforcement | Server-side policy check on `RequiresStepUp` and strength | `AuthorisationPolicy` |
| Session lifecycle | Local session state, idempotent revocation, provider-session fingerprint, append-only evidence | slice 5 |
| Lockout evidence | Tenant-scoped security lock records with one active lock per scope | slice 5 |
| Enrolment and challenge | Implemented for TOTP (2026-10-28): pending enrolment, provider-verified challenge, five-minute purpose-bound evidence | slice 6 |
| Recovery | **Not implemented** | later slice; the rules in section 7 stand |
| Factor → assurance mapping | Approved by this decision | this decision |

## 4. Constraints already binding

- No custom OAuth, OIDC, token-signing, MFA or cryptographic protocol; use the approved provider and approved framework cryptography (ADR-0008).
- A channel identity (phone number, WhatsApp account, USSD session) is an attribute, never a credential, and cannot satisfy a factor (ADR-0008, ADR-0012, threat model section 7).
- USSD and WhatsApp ingress carry no platform bearer or refresh token, so neither channel can deliver a factor. A step-up on those channels must be completed on a registered factor out of band.
- No OTP, recovery code, password, token or private key in logs, traces, events, errors or ordinary tables.
- Missing or stale assurance denies; it never defaults to allow.
- Thresholds, lockout counts and cache intervals are policy values that need their own approval; this brief must not invent them.

## 5. Options

| Option | Factors | Phishing resistance | Dependencies | Recovery burden | Assessment |
| --- | --- | --- | --- | --- | --- |
| 1. Passkeys first | WebAuthn/passkeys for staff and customers; TOTP fallback; SMS OTP limited to low-risk customer actions | High for passkeys | Keycloak WebAuthn; device-bound credential lifecycle (slice 11) | Medium: passkey loss needs assisted re-proof | Best assurance per unit of friction; needs device and channel work |
| 2. TOTP universal | Authenticator app for every actor; recovery codes | Medium | Keycloak OTP only | Medium | Simple and uniform; weaker than passkeys and awkward on some customer segments |
| 3. SMS OTP universal | SMS for everyone | Low (SIM swap, interception) | SMS aggregator through Integration Hub | High fraud exposure | Rejected for privileged or financial actions; acceptable only as a narrow low-risk assist |
| 4. Push approval | Provider push app | Medium-high | Vendor app and push infrastructure | Medium-high | Attractive for staff and corporate approvers; adds a vendor and enrolment surface |

## 6. Recommendation

Adopt **Option 1 with a push-approval variant for staff and corporate approvers**, and record SMS OTP
as a low-risk customer assist only. Concretely:

| Actor kind | Sign-in factor | Step-up method | Notes |
| --- | --- | --- | --- |
| Customer | Password or federated sign-in plus passkey (preferred) or TOTP | Passkey or TOTP challenge bound to session, action and device | SMS OTP permitted only for non-monetary, low-risk actions until Product approves more |
| Staff operations | Password plus passkey; TOTP only as a temporary fallback with an expiry | Passkey, TOTP or push, fresh per action | Restricted approvals additionally require maker-checker, not a second factor alone |
| Security administrator | Password plus passkey | Passkey with fresh device check | Break-glass path defined in runbook, not in application code |
| Corporate/partner user | Password plus passkey or push | Same factor family, bound to mandate | Delegated topology remains a separate open gate |
| Service identity | Client credentials or certificate | Not applicable | Never a human factor; never inherits human grants |

Proposed assurance semantics for the existing enum. Security owns the final values:

| Strength | Proposed meaning | Typical trigger |
| --- | --- | --- |
| `SingleFactor` | One verified factor at sign-in | Read-only self-service |
| `MultiFactor` | Two distinct factor families at sign-in | Standard authenticated operations |
| `StepUp` | A factor completed within a short window, bound to session, purpose and device | Money movement above threshold, beneficiary change, entitlement change, approval decision |

The step-up freshness window is a Security value. This brief proposes five minutes and single use per
action, and marks it **proposed, not approved**.

## 7. Recovery requirements

Any approved recovery path must satisfy all of these; the specific process still needs Security and
Operations sign-off:

1. Recovery is never an administrator silently resetting a factor; it produces immutable evidence with actor, reason, method and correlation.
2. Self-service recovery uses single-use recovery codes stored only as hashes, plus a factor the user still holds.
3. Assisted recovery requires identity re-proof through the customer KYC path or an in-person branch check, with cooling-off before financial actions resume.
4. Recovery revokes active sessions and trusted devices, advances the entitlement version where applicable, and leaves restored state unusable until reconciliation.
5. No security questions, knowledge-based answers, or support-desk override of a privileged factor.
6. Recovery attempts are rate-limited and alertable, and failures do not disclose whether an account exists.

## 8. What stays disabled until this is signed

- MFA enrolment and challenge endpoints, and any production issuance of `MultiFactor` or `StepUp` assurance.
- Step-up-dependent operations for real actors: they remain denied, not weakened, exactly as today.
- Device registration and trusted-device trust duration (slice 11).
- Institution-level configuration of factors and floors; only platform defaults exist.

## 9. Evidence required to close D08

| Evidence | Owner |
| --- | --- |
| Signed factor matrix and assurance mapping, recorded here and in ADR-0008 | Security, Product |
| Keycloak realm/client configuration for the approved factors, with exact redirect and audience inventory | Security, Platform |
| Executable coverage for IAM-003, IAM-004, IAM-015, IAM-021, IAM-022 and IAM-023 including wrong-purpose, replayed, expired and revoked-factor cases | Identity and Access |
| Recovery runbook with assisted re-proof, cooling-off and revocation steps | Operations, Security |
| Threat-model update for the selected factors and recovery abuse cases | Security |
| Retention decision for authentication evidence (`P1-E02-D14`) | Security, Compliance |

## 10. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved Option 1: passkeys first, TOTP available immediately, SMS OTP only for low-risk customer actions, push for approvers; five-minute, single-use, purpose-bound step-up evidence | 2026-10-28 |
| Security | | Named confirmation required before production enablement | |
| Product | | Named confirmation required before the first customer journey is enabled | |
| Platform (Keycloak delivery) | | Required before realm configuration and production enrolment | |

The factor policy, not the production deployment, is approved by this record. The first implementation
slice delivers TOTP with provider-verified challenges, enrolment state and step-up evidence for the
Identity and Access privileged operations. Passkeys, push and the customer channel journeys remain
sequenced behind their own delivery work, and no production enrolment may be claimed until the named
sign-offs, realm configuration and operational controls are complete.

Until the production gates are closed, the register records `P1-E02-D08` as approved with a production
gate, and the ledger keeps `P1-E02` slice 6 `in progress`.
