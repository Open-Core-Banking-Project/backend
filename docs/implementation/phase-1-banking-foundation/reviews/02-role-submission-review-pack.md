---
artifact: Slice review pack
epic: P1-E02 Identity, Roles and Permissions
slice: 7
increment: Frozen role submission to Workflow
snapshot: 2026-11-01
reviewer: project owner (recorded in implementation session)
approved: 2026-11-01
related:
  - ../02-identity-roles-and-permissions.md
  - ../decisions/P1-E02-D09-permission-catalogue-and-role-templates.md
  - ../decisions/P1-E02-D10-role-and-grant-approval-thresholds-and-conflicts.md
  - ../slice-ledger.md
---

# Review pack: P1-E02 slice 7 frozen role submission

## Claim under review

A role draft can now be submitted to the approved Workflow boundary without becoming active. Submission:

- revalidates the current catalogue and conflict rules and refuses an empty role;
- computes a deterministic SHA-256 hash over role code, name, version and ordered permissions;
- stores the request, hash, maker and submission time as one all-or-none database state;
- queues one `approval_requested` outbox event using a frozen, maker-excluded profile;
- records append-only operator evidence without copying the permission set;
- returns the original result on replay and emits no duplicate event or audit row;
- prevents edits while Workflow is deciding.

The role remains inert because grant administration accepts only `Active` roles. This increment does not treat a
Workflow request as approval and does not publish the role.

## Evidence

| Check | Result |
| --- | --- |
| Affected test-project build | Passed; 0 warnings, 0 errors |
| Non-database Identity/Access tests | 177 passed, 0 failed |
| Focused role database tests | 10 passed, 0 failed against local PostgreSQL |
| Submission replay boundary | Repeated sequential submission returns the original request; one outbox event and one audit row persist |
| Edit freeze | Update is refused after submission |
| Migration | `20261101131858_AddRoleApprovalSubmission` applied successfully to local PostgreSQL |
| EF model drift | No pending model changes |
| Formatting/analyzers | `dotnet format --verify-no-changes`; passed |

## Files to review

- `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Contracts/RoleAdministrationV1.cs`
- `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure/Access/RoleAdministrationService.cs`
- `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure/Persistence/Access/AccessRecords.cs`
- `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure/Persistence/Migrations/20261101131858_AddRoleApprovalSubmission.cs`
- `src/Services/IdentityAccess/tests/CoreBanking.IdentityAccess.UnitTests/Persistence/RoleAdministrationDatabaseTests.cs`

## Remaining gates

- The Identity worker does not yet consume and validate `approval_decided` for role publication.
- Submit/update race handling still needs an explicit optimistic-concurrency token before publication is enabled.
- Approved content is not yet copied into immutable role-version history.
- Publication outbox and entitlement-impact work are not yet emitted.
- The access-review surface and named production confirmations remain open.

## Decision

- [x] Approve this frozen-submission increment and continue with decision application/publication.
- [ ] Request changes: record instructions below.

Reviewer: Project owner (approval recorded in implementation session)  Date: 2026-11-01

Change request:

> Approved to continue with Workflow decision application and role publication. Slice 7 remains pending until its
> remaining gates are implemented or explicitly accepted.
