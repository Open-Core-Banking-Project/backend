---
decision: P1-E02-D07
title: Platform security floors for passwords, lockout and MFA
epic: P1-E02 Identity, Roles and Permissions
owners:
  - Security
required_by: P1-E02-S4, P1-E02-S5
status: Approved
approved_on: 2026-11-06
brief_date: 2026-11-06
related: [P1-E02-D08, ADR-0008]
decisions: [P1-E02-D07]
---

# Decision brief: platform security floors

The floor is the weakest authentication control the platform accepts. An institution may strengthen it; no
institution, channel or client may weaken it. Keycloak remains the owner of password verification, failed-attempt
handling and factor execution under ADR-0008, so these values are provider policy plus platform constants, not
application data.

## 1. Decision

### Password floor

| # | Setting | Approved floor | Configurable by an institution |
| --- | --- | --- | --- |
| D07-A1 | Minimum length | 12 characters for customer, corporate-user and partner identities; 15 for staff, agent, administrator and approver identities | Raise only |
| D07-A2 | Maximum accepted length | 128 characters, no truncation, Unicode permitted | Not configurable |
| D07-A3 | Breached/common-password screening | Required on every password set or reset through an approved k-anonymity range service. When screening is unavailable a new password is refused; existing sign-in is unaffected | Not configurable |
| D07-A4 | History and reuse | The previous 5 passwords cannot be reused | Raise only |
| D07-A5 | Maximum password age | None. Passwords never expire on a schedule; a change is forced only by compromise evidence or an administrative reset | Not configurable |
| D07-A6 | Reset and temporary password | Expires after 24 hours, single use, and must be changed at first successful use | Shorten only |
| D07-A7 | Passwordless-only access | Permitted only for customer identities whose primary factor is an approved passkey. Never for staff, agent, administrator or approver identities | Not configurable |

### Failed-attempt and lockout floor

| # | Setting | Approved floor | Configurable by an institution |
| --- | --- | --- | --- |
| D07-B1 | Observation window | 15 minutes, rolling | Not configurable |
| D07-B2 | Threshold | 5 consecutive failed password attempts for one identity and credential inside the window | Lower only |
| D07-B3 | Progressive delay | The wait before a retry starts at 1 second, doubles per failure and never exceeds 30 seconds | Not configurable |
| D07-B4 | Lock | At the fifth failure the password credential is blocked for 15 minutes and unblocks automatically when the window ends | Lengthen only |
| D07-B5 | Unlock authority | Automatic expiry, or a security operator holding the lock-release permission with a recorded reason. There is no self-service unlock | Not configurable |
| D07-B6 | Evaluation dimensions | Identity plus credential, institution, and source network. Network limit: 20 attempts per minute and 100 per hour per source | Lower only |
| D07-B7 | Enumeration and lockout-abuse safety | Unknown usernames are never locked and receive the same response and timing as known ones. The lock binds the password credential only, so a registered passkey or approved second factor still authenticates and a targeted lockout cannot deny an enrolled actor | Not configurable |
| D07-B8 | Security events and alerts | Lock, release and use of a locked credential append authentication evidence. Alert on any privileged-identity lock, more than three locks for one identity in 24 hours, or 10 distinct identities failing from one source in five minutes | Not configurable; destinations are ADR-0010 gates |

### MFA floor

| # | Setting | Approved floor | Configurable by an institution |
| --- | --- | --- | --- |
| D07-C1 | Actor types that must enrol MFA before activation | Staff, administrator, agent, approver and corporate-administrator identities. Customers may activate on one factor and enrol a passkey second factor progressively | Add actor types only |
| D07-C2 | Operations that always require fresh step-up | Every operation already declared with `RequiresStepUp` stays a floor: identity create and update, role and permission administration, another identity's access review, MFA enrolment/change/removal, trusted-device registration and revocation, credential and API-client administration, security-lock release, approval decisions, institution lifecycle, business-date advancement and product update or publication | Add operations only |
| D07-C3 | Step-up freshness and reuse | Five minutes, single use, purpose-bound; a missing, failed or expired step-up denies and never warns (P1-E02-D08) | Shorten only |
| D07-C4 | Prohibited methods and combinations | SMS OTP for staff, agent, administrator or approver identities; email OTP as the sole factor; knowledge-based questions; voice OTP; password-only access for privileged actors. Realm and client roles never satisfy a factor or a permission | Not configurable |
| D07-C5 | Recovery assurance | Factor replacement waits 24 hours for privileged identities with a second approver, and one hour for customers. Any recovery invalidates existing sessions, unused step-up evidence and trusted-device trust for that identity, and appends authentication evidence | Lengthen only |
| D07-C6 | Institution strengthening | An institution may require MFA for more actor types and operations and may shorten the step-up window. It may never remove a required factor, weaken a method or lengthen the step-up window | Strengthen only |

### Configuration and rollout boundary

| # | Question | Answer |
| --- | --- | --- |
| D07-D1 | What is a platform constant? | Every value marked not configurable above, including the 5-minute single-use step-up, the ban on periodic rotation, the prohibited methods and the MFA-required actor types. These are code and configuration review subjects, never institution data |
| D07-D2 | What lives in provider configuration? | The Keycloak realm password policy, brute-force detection, required actions and authentication flows carry the numeric values. Token, refresh and session lifetimes remain ADR-0008 configuration evidence |
| D07-D3 | What may institution policy do? | When institution authentication policy is implemented it is effective-dated, versioned and audited, and each value is validated against the floor. A weakening value is rejected, never silently clamped to the floor |
| D07-D4 | How are existing identities remediated? | At their next authentication. A privileged identity that has not enrolled MFA within 14 days keeps restricted access — no approval decisions and no administration — until enrolment completes |
| D07-D5 | Who owns and how is it rolled back? | Identity security owns the values and evidence. Rollback is a forward fix through a new audited policy version. An emergency weakening needs Security approval and an incident record; it is never a silent revert |

## 2. Constraints

- This decision does not authorise production enrolment. Keycloak hosting and operations (P1-E02-D01), the reviewed realm export, breached-password screening integration and real provider refresh/revocation remain production gates.
- No threshold is inferred from framework defaults, development fixtures or provider sample data.
- No password, OTP, recovery code, token or private key enters ordinary storage, telemetry, evidence or errors.
- Every refusal fails closed; a missing or invalid security configuration never falls back to a permissive default.
- Authentication responses do not disclose whether an identity, tenant or factor exists, or whether a credential is locked.
- Nothing here sets monetary approval thresholds (P1-E02-D10), evidence retention (P1-E02-D14) or service objectives (P1-E02-D15).

## 3. Where each value is enforced

| Value | Enforcement point | State at approval |
| --- | --- | --- |
| Password length, history, rotation, reset expiry | Keycloak realm password policy | Not applied; the realm export is a production gate |
| Breached-password screening | Approved screening integration on password set/reset | Provider integration gate |
| Failure threshold, progressive delay and lock | Keycloak brute-force detection, with the edge gateway or an approved extension covering any outcome the deployed provider cannot express | Provider integration gate; any deviation must be recorded |
| Network attempt limits | Gateway or provider rate limiting | Deployment gate |
| Lock record, release with reason and authentication evidence | `SessionSecurityService` records tenant-scoped locks, one unreleased lock per identity and scope, and append-only authentication events | Implemented in slice 5 |
| Step-up freshness, purpose binding and single use | `AuthorisationPolicy` plus the step-up evidence store | Implemented in slices 6 and 12 |
| MFA-required actor types and strengthen-only institution policy | Institution authentication policy schema and validation | Later slice; until then privileged enrolment is an operational gate |

The platform enforces the mechanics today; the numeric floors above are provider policy. This decision deliberately
adds no application constant that would duplicate a provider setting the platform cannot yet read back.

## 4. Deferred for named approval

| Item | Owner | Why it is not decided here |
| --- | --- | --- |
| Reviewed Keycloak realm/client policy export, hosting and support | Security / Platform | P1-E02-D01 production gate; no credential material may be committed |
| Breached-password screening product and outage behaviour | Security / Platform | Provider selection follows the screening requirement, not the reverse |
| Real provider refresh and revocation integration | Security / Platform | Slice 5 production gate; the local lifecycle and revocation work already fail closed |
| SIEM routing, dashboard product and alert thresholds | Security / Platform | ADR-0010 production gates; D07 fixes the events and the alert conditions, not the destinations |
| Evidence retention and authentication SLOs | Security / Compliance / Product | P1-E02-D14 and P1-E02-D15 remain open |

## 5. Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Project owner | Recorded in the implementation session | Approved the recommended floors: 12/15 minimum lengths with breached-password screening, history 5 and no periodic rotation, 24-hour single-use reset; 5 failures in 15 minutes with 1-second doubling delay to a 30-second cap and a 15-minute lock; MFA mandatory for privileged actor types with 5-minute single-use step-up and 24-hour/1-hour recovery delays; platform constants with strengthen-only institution policy and a 14-day enrolment grace | 2026-11-06 |
| Security | | Named confirmation required before production enrolment or any realm policy is applied | |

## 6. Implementation notes (2026-11-06)

- This decision was the only open blocker on `P1-E02-S5`. With it approved, slice 5 is recorded as implemented
  pending review, and real Keycloak refresh/revocation integration remains the named production gate.
- It also clears the password floor that `P1-E02-S4` deferred, without inventing a policy schema the epic has not
  scheduled.
- Production evidence still required: the reviewed realm export with no credential material; synthetic negative
  tests for spraying, stuffing, enumeration, lockout abuse, expired reset, missing MFA and step-up bypass; refresh
  and revocation integration evidence; and dashboard and alert evidence with a runbook response path.