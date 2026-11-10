# Keycloak authentication rollout

This runbook starts the Keycloak integration selected in ADR-0008. It covers the first protected
resource server, `CoreBanking.IdentityAccess.Api`. Repeat the same validation boundary in every
business service before exposing that service outside a development environment.

## Trust boundary

Keycloak authenticates actors and issues signed access tokens. Identity and Access remains the
authority for institution membership, roles, permissions, contextual scopes and entitlement
versions. Keycloak realm roles and client roles are not effective banking permissions and the API
does not project them into `AuthenticatedActor`.

The API downloads discovery metadata and signing keys from Keycloak, then validates signature,
issuer, audience, expiry and not-before using ASP.NET Core JWT bearer authentication. It creates an
actor only when all required claims are present and unambiguous.

## Realm and client

Create one realm per environment; do not allow a non-production service to trust a production
realm. The examples below use:

- realm: `core-banking-dev`
- resource-server client ID and audience: `corebanking-identity-api`
- reusable client scope: `corebanking-actor-context`

Configure the resource-server client so its client ID appears in the access token `aud` claim.
Keycloak's Audience protocol mapper can add that audience. Attach a dedicated client scope with
protocol mappers for the application-owned attributes below.

| Access-token claim | Source | Required behavior |
| --- | --- | --- |
| `sub` | Keycloak subject | Stable provider link for the actor |
| `actor_type` | Managed attribute | `human`, `service` or `partner` |
| `institution_id` | Entitlement projection | Institution identifier owned by Identity and Access |
| `sid` | Keycloak session | Session identifier; required |
| `device_id` | Authenticated session/device context | Optional until device binding is implemented |
| `entitlement_version` | Entitlement projection | Non-negative integer owned by Identity and Access |
| `authentication_strength` | Authentication flow projection | `SingleFactor`, `MultiFactor` or `StepUp` |
| `aud` | Audience mapper | Must contain `corebanking-identity-api` |

Add custom claims to access tokens, not only ID tokens or UserInfo responses. Do not add role or
permission lists to satisfy application authorisation. Before using a mapper in production, verify
that its source cannot be self-edited by the end user.

The first entitlement projection may update protected Keycloak attributes through a narrowly
scoped administrative service account after the corresponding Identity and Access transaction
commits. That adapter, retry/reconciliation behavior and credential custody are a separate rollout
step; manually maintained attributes are development-only.

## Approved platform security floors (P1-E02-D07)

[P1-E02-D07](../implementation/phase-1-banking-foundation/decisions/P1-E02-D07-platform-security-floors.md)
fixes the weakest authentication controls the platform accepts. The realm must produce these outcomes; an
institution may strengthen them, never weaken them. The reviewed realm export is the evidence and must contain no
credential material.

| Area | Approved outcome the realm must deliver |
| --- | --- |
| Password length | Minimum 12 for customer, corporate-user and partner identities; 15 for staff, agent, administrator and approver identities; 128 characters accepted without truncation |
| Password composition | No composition-class rules. Breached and common passwords are refused through the approved screening integration |
| Password history | The previous 5 passwords cannot be reused |
| Password lifetime | No periodic expiry. Reset and temporary passwords expire after 24 hours, are single use and must be changed at first use |
| Failed attempts | 5 consecutive failures in a rolling 15-minute window lock the password credential for 15 minutes |
| Retry delay | The wait before a retry starts at 1 second, doubles per failure and never exceeds 30 seconds |
| Unlock | Automatic at the end of the window, or by a security operator holding the lock-release permission with a recorded reason. No self-service unlock |
| Enumeration and lockout abuse | Unknown usernames are never locked and receive the same response and timing as known ones. The lock binds the password credential only, so a registered passkey or approved second factor still authenticates |
| Network limits | 20 attempts per minute and 100 per hour per source |
| MFA | Required before activation for staff, administrator, agent, approver and corporate-administrator identities. Prohibited: SMS OTP for those identities, email OTP as sole factor, knowledge-based questions, voice OTP and password-only privileged access |
| Step-up | Five minutes, single use, purpose-bound. Every operation already declared with `RequiresStepUp` remains a floor |
| Recovery | Factor replacement waits 24 hours for privileged identities with a second approver and one hour for customers; recovery invalidates sessions, unused step-up evidence and device trust |

Configure these through Keycloak's password policy, brute-force detection, required actions and authentication
flows. The realm-policy review must demonstrate each outcome against the deployed provider version and record any
gap where the provider cannot express it, with the compensating gateway or extension control. Do not copy
development or provider-sample defaults into the realm: a missing or invalid setting fails closed, it never falls
back to a permissive default.

Production evidence for this section: the reviewed realm/client export, synthetic negative tests for spraying,
stuffing, enumeration, lockout abuse, expired reset, missing MFA and step-up bypass, and dashboard and alert
evidence with the runbook response path.

## API configuration

Use environment variables or an approved secret/configuration provider:

```text
Authentication__Keycloak__Authority=https://identity.example.com/realms/core-banking
Authentication__Keycloak__Audience=corebanking-identity-api
Authentication__Keycloak__RequireHttpsMetadata=true
```

For a loopback-only local Keycloak development server, HTTP metadata can be enabled explicitly:

```text
Authentication__Keycloak__Authority=http://127.0.0.1:8080/realms/core-banking-dev
Authentication__Keycloak__Audience=corebanking-identity-api
Authentication__Keycloak__RequireHttpsMetadata=false
```

HTTP metadata is rejected outside the ASP.NET Core `Development` environment. Authority and
audience must be configured together. A non-development API refuses to start when either is
missing, so deployment cannot silently fall back to the synthetic development identity header.

`ConnectionStrings__IdentityAccess` is still mandatory outside Development. Authentication
configuration does not apply database migrations or make the entitlement store optional.

## Outbound workload identity

Core Ledger and Operations Workflow call Identity and Access with OAuth client credentials. Register
one confidential Keycloak client per calling service, configure its audience mapper to emit exactly
`corebanking-identity-api`, and register the same client identifier and audience through the Identity
service-client administration API. Grant only the service capabilities the caller needs.

Configure the caller with metadata and an opaque secret reference:

```text
IdentityAccess__BaseUrl=https://identity-api.example.com
IdentityAccess__WorkloadIdentity__TokenEndpoint=https://identity.example.com/realms/core-banking/protocol/openid-connect/token
IdentityAccess__WorkloadIdentity__ClientId=core-ledger
IdentityAccess__WorkloadIdentity__ClientSecretReference=identity.core-ledger.client-secret
```

The referenced secret is resolved through `ISecretProvider`; it must not appear in ordinary
configuration, logs or evidence. Partial configuration, a non-HTTPS token endpoint, a missing secret,
a rejected token request or a token lifetime outside 31 to 3600 seconds fails closed. Access tokens
are cached only until 30 seconds before expiry. No token is persisted or logged.

## Customer eligibility facts (service-to-service)

Core Ledger reads authoritative customer facts from Customer and Onboarding over one internal route.
Decision: P1-E04-D17.

| Item | Value |
| --- | --- |
| Route | `GET /internal/v1/customers/{customerId}/eligibility-facts?asOf=<iso8601>` |
| Audience | `corebanking-customer-api` |
| Capability (token scope) | `service.customer.eligibility_facts_read` |
| Allow-listed caller | `CustomerFacts:AllowedServiceClientIds` on Customer and Onboarding |

Register one confidential Keycloak client for each calling service (for example `core-ledger`) with an audience mapper
that emits exactly `corebanking-customer-api`, and assign a client scope whose name includes
`service.customer.eligibility_facts_read` so the token `scope` claim carries it. The service token also needs the
trusted `institution_id` claim the platform uses for every actor; the caller sends `X-Institution-Id` for traceability
but the service never trusts it over the claim.

Configure Customer and Onboarding:

```text
Authentication__Keycloak__Authority=https://identity.example.com/realms/core-banking
Authentication__Keycloak__Audience=corebanking-customer-api
CustomerFacts__AllowedServiceClientIds__0=core-ledger
Privacy__Protection__EncryptionKey=<secret reference resolved outside configuration>
Privacy__Protection__LookupKey=<distinct secret reference>
ConnectionStrings__CustomerOnboarding=<application principal connection string>
```

Configure the caller:

```text
CustomerFacts__BaseUrl=https://customer-onboarding.example.com
IdentityAccess__WorkloadIdentity__TokenEndpoint=https://identity.example.com/realms/core-banking/protocol/openid-connect/token
IdentityAccess__WorkloadIdentity__ClientId=core-ledger
IdentityAccess__WorkloadIdentity__ClientSecretReference=identity.core-ledger.client-secret
```

The route fails closed: an unconfigured authority answers `503 service_authentication_unconfigured`; a missing,
non-service, unlisted, wrong-audience, capability-less or institution-less caller answers `401`/`403` with a bounded
code; missing facts, no effective classification, an inactive branch or an unsupported currency deny the eligibility
decision. An empty allow-list denies every caller, so omitting the setting can never widen access.

## Verification gate

Before enabling traffic, prove all of the following with synthetic identities:

1. A valid token with the correct issuer, audience and complete claim set reaches authentication,
   then succeeds or fails according to the locally stored entitlement.
2. Missing, expired, not-yet-valid, unsigned, forged, wrong-issuer and wrong-audience tokens return
   HTTP 401.
3. Missing, conflicting or malformed actor, institution, session, entitlement-version or strength
   claims return HTTP 401.
4. A valid token for another institution cannot read or change the target institution's data.
5. Keycloak realm/client roles and injected permission claims do not grant an application
   permission.
6. Incrementing the entitlement version makes the old token fail authorisation within the approved
   invalidation window.
7. Key rotation succeeds while an old compromised key is rejected after the approved overlap.
8. A service token whose validated `aud` differs from its registered service-client audience is
   rejected before the protected handler runs.
9. Core Ledger or Operations Workflow obtains a short-lived client-credentials token and completes
   one synthetic call to the appropriate Identity internal route; missing credentials deny the call.

Retain test and deployment evidence without recording access tokens, client secrets, passwords or
private keys.
