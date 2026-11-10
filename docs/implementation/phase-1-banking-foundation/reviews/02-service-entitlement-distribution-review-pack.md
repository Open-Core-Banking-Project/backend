# P1-E02-S13 service identity final review pack

## Review request

Review the completed service-identity implementation: explicit service-only capabilities, exact audience binding, credential-reference lifecycle, provider token acquisition and authenticated Identity calls. Realm configuration and a synthetic deployed-provider run remain production evidence gates.

## Requirement and story coverage

- IAM-011 and IAM-016: an authenticated service actor must map to an active, unexpired, same-institution service client.
- IAM-004/IAM-005 separation: service grants are stored on the service client and never sourced from human roles or role assignments.
- SEC-009: the internal route is protected by `service.identity.entitlements_read`, is institution scoped, and fails closed for human callers, cross-tenant requests, missing snapshots and regressed versions.
- ADR-0008 and ADR-0009: workload identity validation remains provider/audience aware at the token boundary; entitlement resolution is explicit, versioned and deny-by-default.

## Delivered increment

- `ServiceClientRegistrationRequestV1` accepts a bounded list of explicit service permissions.
- Permission validation accepts only three lower-case/digit/underscore segments in `service.<domain>.<action>` form, with duplicate removal and a maximum of 32 entries.
- Migration `20261101142013_AddServiceClientPermissions` adds the non-null PostgreSQL `text[]` permission column.
- Service entitlement lookup is separate from human entitlement versions and human role assignments. An active, unexpired service record has version 1; a revoked, expired, missing or cross-tenant record has no current version or snapshot.
- `POST /internal/v1/entitlements/snapshot` requires a service actor with `service.identity.entitlements_read` and resolves only a human target in the caller's institution.
- The validated JWT audience is retained in trusted actor context and must exactly match the registered service-client audience.
- Core Ledger and Operations Workflow acquire short-lived client-credentials tokens through an opaque secret reference and attach them to Identity calls. Missing or invalid configuration fails closed.
- Consumer-owned version 1 wire projections replace prohibited compile-time references to another service's contract project; the HTTP boundary remains the only dependency.
- Published permission identifiers use the platform grammar (`entitlements_read`, not a hyphenated segment).

## Verification performed

- Affected Identity, Core Ledger and Operations Workflow builds: passed.
- Workload-token and Keycloak actor-context tests: 24 passed.
- Focused `ServiceClientDatabaseTests`: 6 passed against local PostgreSQL, including wrong-audience denial.
- Identity non-database suite: 197 passed.
- Core Ledger non-database suite: 102 passed.
- Operations Workflow non-database suite: 26 passed.
- Repository architecture suite: 15 passed after removing the cross-service project references exposed by the first run.
- Database security suite: 54 passed with local application-principal connections. An initial run without those connection strings failed closed before database access.
- Formatting verification for the three affected projects: passed.

The first focused database run exposed and prevented a human/service model leak: service registration attempted to create a human entitlement-version row and violated its identity foreign key. The corrected implementation derives the service version only from an active service-client record. A second run exposed an invalid hyphenated permission identifier; validation and the published identifier now conform to the shared grammar.

## Remaining gates and risk

- The token issuer must emit entitlement version `1` and the exact registered Identity audience. The realm/client mapper inventory and a synthetic deployed-provider call still require human-controlled environment evidence.
- Service permissions are immutable after registration in this increment. A future permission-change operation must advance a service-specific version instead of silently editing the array.
- Approval remains pending; this pack does not self-approve the slice.

## Rollout and forward fix

Apply the existing additive migration before enabling service permission registration. Register the target audience and least-privilege capabilities, place the secret only in the configured provider, then configure outbound token metadata. Existing clients with empty permissions gain no access. Removing workload configuration immediately makes outbound calls fail closed; after use, prefer a forward fix that preserves service-client evidence.

## Human action

Review the audience binding, secret-reference boundary, token lifetime constraints and fail-closed outbound integration.
