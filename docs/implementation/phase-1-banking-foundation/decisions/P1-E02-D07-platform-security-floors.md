---
id: P1-E02-D07
title: Platform security floors for passwords, lockout and MFA
status: Open
owners: [Security]
required_by: P1-E02-S4, P1-E02-S5
---

# P1-E02-D07 platform security floors

## Decision request

Security must approve the minimum authentication controls that no institution policy may weaken. The platform must not infer these values from framework defaults or development fixtures.

## Already fixed by approved decisions

- Keycloak owns password verification and token issuance under ADR-0008. The banking platform does not store or verify passwords.
- P1-E02-D08 approves passkeys first, TOTP availability, SMS OTP only for low-risk customer actions, push for approvers and five-minute single-use step-up evidence.
- Raw passwords, OTPs, recovery codes, bearer/refresh tokens and private keys never enter ordinary platform storage, telemetry or evidence.
- Authentication and recovery fail closed; errors do not disclose whether an identity exists.

## Inputs Security must resolve

### Password floor in Keycloak

Approve the minimum provider policy and which settings an institution may strengthen:

- minimum length and permitted maximum;
- breached/common-password screening requirement;
- password history/reuse rule;
- maximum password age, or an explicit decision not to force periodic rotation;
- reset and temporary-password expiry;
- whether any actor/channel may use passwordless-only authentication.

### Failed-attempt and lockout floor

Approve values and scope for:

- failed-attempt observation window;
- threshold before challenge, delay or lock;
- delay progression and maximum delay;
- lock duration and unlock authority;
- actor, credential, device, network and institution dimensions used by the provider;
- safeguards against account-lockout denial of service and enumeration;
- security-event and operator-alert conditions.

### MFA floor

Within P1-E02-D08's approved methods, approve:

- actor types that must enrol MFA before activation;
- operations that always require fresh step-up;
- whether an institution may require stronger methods but never weaken the platform floor;
- recovery assurance, factor replacement delay and privileged recovery approval;
- any prohibited actor/channel/method combinations.

### Configuration and rollout boundary

Approve which fields are platform constants, provider realm/client configuration, or institution-effective-dated policy. Confirm the rollout order, existing-identity remediation, evidence owner and rollback/forward-fix posture.

## Evidence required before production

- Reviewed Keycloak realm/client policy export with no credential material.
- Synthetic negative tests for spraying, stuffing, enumeration, lockout abuse, expired reset, missing MFA and step-up bypass.
- Provider/local session refresh and revocation integration evidence.
- Dashboard and alert evidence using bounded labels and the runbook response path.

## Consequence of no decision

P1-E02-S5 remains in progress. Local session, reuse-detection, revocation and evidence paths stay fail closed, but production password, lockout, recovery and real provider refresh/revocation enablement remain prohibited.
