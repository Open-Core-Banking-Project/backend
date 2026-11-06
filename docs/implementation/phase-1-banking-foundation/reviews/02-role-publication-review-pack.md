---
artifact: Slice review pack
epic: P1-E02 Identity, Roles and Permissions
slice: 7
increment: Workflow decision application and role publication
snapshot: 2026-11-01
reviewer: project owner (recorded in implementation session)
approved: 2026-11-01
related:
  - ../02-identity-roles-and-permissions.md
  - ../decisions/P1-E02-D09-permission-catalogue-and-role-templates.md
  - ../decisions/P1-E02-D10-role-and-grant-approval-thresholds-and-conflicts.md
  - ../slice-ledger.md
---

# Review pack: P1-E02 slice 7 Workflow decision and role publication

## Claim under review

The frozen role-submission path now completes a real cross-service maker-checker round trip:

- Operations Workflow explicitly recognizes the pinned `identity.role.publish.v1` profile and subscribes to the
  Identity approval topic;
- Identity routes `approval_decided` independently from institution lifecycle events;
- the consumer validates producer, institution, request, version, content hash, workflow definition, permission and
  decision-time entitlement validity;
- maker self-approval is independently denied even if an inbound event claims approval;
- approval activates only the exact frozen role and records decision/publisher evidence;
- activation, append-only audit evidence, `role_version_published`, `approval_application_result` and inbox outcome
  commit in one local unit of work;
- duplicate delivery produces no second publication or acknowledgement;
- rejection returns the role to editable draft state while retaining the last decision evidence.

## Evidence

| Check | Result |
| --- | --- |
| Identity affected builds | Passed; 0 warnings, 0 errors |
| Identity non-database tests | 177 passed, 0 failed |
| Identity focused role database tests | 13 passed, 0 failed against local PostgreSQL |
| Workflow non-database tests | 22 passed, 0 failed |
| Workflow database tests | 4 passed, 0 failed against local PostgreSQL |
| Maker separation | Claimed maker self-approval remained pending and produced no publication |
| Replay | Duplicate decision returned `Duplicate`; one publication and one application acknowledgement persisted |
| Migration | `20261101133109_AddRolePublicationDecision` applied successfully |
| EF model drift | No pending model changes |
| Formatting/analyzers | Identity tests/worker and Workflow tests passed `dotnet format --verify-no-changes` |

## Files to review

- `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure/Access/RoleApprovalDecidedConsumer.cs`
- `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure/IdentityAccessIntegrationEventConsumer.cs`
- `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Workers/Program.cs`
- `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Contracts/RoleAdministrationV1.cs`
- `src/Services/OperationsWorkflow/src/CoreBanking.OperationsWorkflow.Domain/Approvals/ApprovalProfiles.cs`
- `src/Services/IdentityAccess/tests/CoreBanking.IdentityAccess.UnitTests/Persistence/RoleAdministrationDatabaseTests.cs`

## Remaining gates

- Published role versions do not yet have a separate immutable multi-version history table.
- Grants still bind the role identifier rather than an immutable role-version identifier.
- Submit/update concurrency needs an explicit optimistic-concurrency token and contention test.
- The access-review surface and named production confirmations remain open.

## Decision

- [x] Approve this Workflow decision/publication increment and continue with immutable role-version history.
- [ ] Request changes: record instructions below.

Reviewer: Project owner (approval recorded in implementation session)  Date: 2026-11-01

Change request:

> Approved to continue with immutable role-version history and grant version binding. Slice 7 remains pending until
> its remaining gates are implemented or explicitly accepted.
