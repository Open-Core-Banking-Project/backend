# Identity, roles and permissions acceptance evidence

Result of the Phase 1 Identity, Roles and Permissions epic evidence pass (slice 15). Every row is produced by a
test that runs in `scripts/db-verify.ps1`, so the evidence is re-run on each verification instead of being a dated
screenshot. Decisions: [P1-E02-D12](../implementation/phase-1-banking-foundation/decisions/P1-E02-D12-payload-envelope-format-algorithms-and-replay-policy.md),
[P1-E02-D13](../implementation/phase-1-banking-foundation/decisions/P1-E02-D13-kms-hsm-product-and-key-operation-boundary.md),
[P1-E02-D11](../implementation/phase-1-banking-foundation/decisions/P1-E02-D11-trusted-device-protocol-and-trust-duration.md).

## What is evidenced

| Requirement | Evidence (test) | Suite |
| --- | --- | --- |
| Explicit actor kinds, membership and provider links | `IdentityLifecycleRulesTests`, `IdentityAccessEndToEndTests` | Unit, Integration |
| Registration and status transitions with concurrency and idempotency | `IdentityAccessEndToEndTests.A_staff_identity_is_registered_activated_trusted_and_revoked_with_its_session` | Integration |
| Session lifecycle, lockouts and provider binding | `SessionSecurityDatabaseTests` | Database |
| MFA enrolment, purpose-bound step-up and single-use evidence | `MfaDatabaseTests`, `MfaPolicyTests` | Database, Unit |
| Trusted devices: step-up-gated trust and bound-session termination | `TrustedDeviceRulesTests`, `TrustedDeviceDatabaseTests`, `IdentityAccessEndToEndTests` | Unit, Database, Integration |
| Payload envelopes: approved suite, binding, replay and key custody | `PayloadEnvelopeRulesTests`, `PayloadEncryptionDatabaseTests`, `IdentityAccessEndToEndTests` | Unit, Database, Integration |
| Public-key discovery of active versions only | `PayloadEncryptionDatabaseTests.Discovery_advertises_only_active_versions_of_the_requested_institution` | Database |
| Role catalogue, versioned drafts and publication | `RoleAdministrationDatabaseTests`, `RoleDraftRulesTests` | Database, Unit |
| Scoped grants, temporary grants and conflict-of-duty | `GrantAdministrationDatabaseTests`, `ConflictEvaluatorTests` | Database, Unit |
| Entitlement invalidation fails closed on a stale version | `IdentityAccessEndToEndTests.A_stale_entitlement_version_and_a_missing_grant_both_fail_closed`, `RelationalEntitlementStoreTests`, `EntitlementDistributionTests` | Integration, Database, Unit |
| Deny by default and safe problem details | `AuthorisationServiceTests`, `AuthorisationPolicyTests`, `AuthorisationProblemTests` | Unit |
| Maker self-approval denial and approver eligibility | `ApprovalEligibilityTests` | Unit |
| Service identity validation before authorisation | `ServiceClientRulesTests`, `ServiceClientDatabaseTests` | Unit, Database |
| Reconciliation counters and operator endpoint | `EntitlementReconciliationDatabaseTests` | Database |
| Immutable, redacted evidence | `PersistentAuthorisationAuditWriterTests`, `OperatorActionAuditRecordTests`, `SensitiveTelemetryRedactionProcessorTests` | Database, Unit |
| Cross-tenant refusal | `IdentityAccessEndToEndTests`, `TrustedDeviceDatabaseTests`, `PayloadEncryptionDatabaseTests` | Integration, Database |
| Wire-contract stability | `AuthorisationContractCompatibilityTests`, `EventContractTests` | Unit |
| Database privileges for every principal | privilege suite (`tests/CoreBanking.SecurityTests`) | Security |

Run them with:

```powershell
powershell -File scripts/db-verify.ps1
```

## End-to-end journey

`A_staff_identity_is_registered_activated_trusted_and_revoked_with_its_session` exercises one continuous flow
against `identity_access_db`: an active institution boundary admits a staff registration (`pending`), an explicit
activation publishes `identity_status_changed` and advances the entitlement version, another tenant cannot read the
identity, a provider session is registered, a TOTP factor is enrolled and a purpose-bound step-up verifies, a
trusted device completes registration by consuming that evidence, a second factor response arrives inside a
protected payload envelope and still verifies, replaying that envelope is refused, a session bound to the trusted
device is revoked with it while the unbound session stays active, and both `trusted_device_revoked` and
`sessions_revoked_for_device` are published.

## Provider and boundary posture

| Boundary | Bound implementation | What it does not decide |
| --- | --- | --- |
| Actor/token resolution | `KeycloakActorContextResolver` in deployment, `DevelopmentActorContextResolver` only in Development | Realm hosting, claim mapping ownership |
| Factor verification | `SyntheticMfaFactorVerifier` (Development) and `UnconfiguredMfaFactorVerifier` everywhere else | Approved provider per factor family |
| Key custody | `DevelopmentKeyOperationPort` behind `ISecretProvider`, `UnconfiguredKeyOperationPort` elsewhere | KMS/HSM product, region and alerting |
| Secret material | `ISecretProvider` with an environment/configuration adapter | The production secret store |

Every unconfigured boundary refuses the operation: no factor, no token and no key means no access and no plaintext
fallback.

## Gaps, recorded as gaps

| Gap | Owner | Note |
| --- | --- | --- |
| Keycloak realm, token transport and channel storage reviews | Security/Platform | Named release gate from ADR-0008 |
| Approved MFA provider per factor family and passkey/attestation providers | Security/Product | D08 sign-off outstanding |
| KMS/HSM product and production adapter; platform-side key registration and rotation | Security/Platform | D13 keeps the boundary, not the product |
| SIEM forwarding, dashboards and alert routing | Platform Engineering | Reconciliation counters and metric names exist; the platform adapter is not selected |
| Authentication and authorisation SLOs and load profile | Product/Platform | Performance evidence belongs to the release gates |
| Evidence retention period | Security/Compliance | Retention decision still open in the epic |
| Sensitive-read auditing on the officer device route | Security | Route is permission-gated; the dedicated audit row is not written yet |
| Production access enablement | Project owner | Nothing here authorises production traffic |

## Points to remember

- A stale entitlement version is a refusal, never a cached allow: the integration test proves the same actor is
  allowed at the current version and denied after the version advances.
- Payload envelopes never replace TLS, authentication, authorisation or input validation; they add field
  confidentiality for an allow-listed purpose only.
- Every refusal is one of the documented stable codes, and no response, event or log carries ciphertext, plaintext,
  token or key material.
