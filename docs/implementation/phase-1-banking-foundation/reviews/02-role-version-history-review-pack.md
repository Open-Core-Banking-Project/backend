---
artifact: Slice review pack
epic: P1-E02 Identity, Roles and Permissions
slice: 7
increment: Immutable role-version history and grant binding
snapshot: 2026-11-01
reviewer: project owner (recorded in implementation session)
approved: 2026-11-01
related:
  - ../02-identity-roles-and-permissions.md
  - ../decisions/P1-E02-D09-permission-catalogue-and-role-templates.md
  - ../slice-ledger.md
---

# Review pack: P1-E02 slice 7 immutable role versions

## Claim under review

Published role state is now immutable and grants no longer follow mutable role permissions:

- approval atomically creates a `role_versions` snapshot and ordered `role_version_permissions` rows;
- the snapshot retains content hash, request, Workflow decision, publisher and publication time;
- each identity-role assignment stores the exact published role version granted;
- grant conflict evaluation and entitlement distribution join through the assigned version;
- publishing a later version does not alter the permissions of an existing assignment;
- migration `20261101134052` backfills every active role and assignment before adding the version foreign key;
- tenant-aware composite keys and restrictive foreign keys prevent cross-tenant or missing-version grants.

## Evidence

| Check | Result |
| --- | --- |
| Affected build | Passed; 0 warnings, 0 errors |
| Identity non-database tests | 177 passed, 0 failed |
| Complete Identity database suite | 97 passed, 0 failed |
| Identity integration journeys | 4 passed, 0 failed |
| Targeted publication/grant/entitlement tests | 26 passed, 0 failed |
| Historical stability | Assignment to version 1 retained its original permission after version 2 was published |
| Migration | `20261101134052_AddImmutableRoleVersions` applied with active-role and assignment backfill |
| EF model drift | No pending model changes |
| Formatting/analyzers | `dotnet format --verify-no-changes`; passed |

## Files to review

- `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure/Persistence/Access/AccessRecords.cs`
- `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure/Persistence/Configurations/AccessRecordConfiguration.cs`
- `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure/Access/RoleApprovalDecidedConsumer.cs`
- `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure/Access/GrantAdministrationService.cs`
- `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure/Persistence/RelationalEntitlementStore.cs`
- `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure/Persistence/Migrations/20261101134052_AddImmutableRoleVersions.cs`

## Remaining gates

- Submit/update contention needs an explicit optimistic-concurrency boundary and database contention test.
- The access-review query/API surface remains open.
- Named Security and Operations production confirmations remain open.

## Decision

- [x] Approve immutable role-version history and continue with contention protection and access review.
- [ ] Request changes: record instructions below.

Reviewer: Project owner (approval recorded in implementation session)  Date: 2026-11-01

Change request:

> Approved to continue with contention protection and access review. Slice 7 remains pending until its remaining
> gates are implemented or explicitly accepted.
