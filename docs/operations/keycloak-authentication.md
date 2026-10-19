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

Retain test and deployment evidence without recording access tokens, client secrets, passwords or
private keys.
