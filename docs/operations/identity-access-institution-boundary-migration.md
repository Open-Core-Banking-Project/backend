# Identity and Access institution boundary migration

Migration: `20261018141020_AddInstitutionBoundaryReference`

Scope: Phase 1 Institution and Tenancy slice 6 (`CFG-001`, `CFG-002`, `ARC-004`, `ARC-006`, `ARC-007`, `DAT-005`, `DAT-007`, `US-001`) and ADR-0007.

## Data impact

- Creates `platform.institution_boundaries` in `identity_access_db`.
- Adds no seed data and does not modify identities, roles, permissions, sessions or audit evidence.
- Enforces one boundary per institution, a positive provisioning-plan version, a controlled lifecycle status and a unique source-event reference.
- The boundary begins in `provisioning`; consuming this event never activates an institution or grants a permission.

## Rollout

1. Back up `identity_access_db` according to the approved environment procedure.
2. Apply the migration with `identityaccess_migrator`.
3. Verify the application principal inherited CRUD privileges from the existing `platform` schema default privileges.
4. Deploy the Identity and Access worker with its database and Kafka settings.
5. Confirm both the outbox relay and `corebanking.core-ledger.institution.v1` consumer are healthy before producing provisioning events.

No backfill is required. Existing environments have no Phase 1 institution boundaries until their provisioning events are deliberately replayed.

## Verification

- `dotnet ef migrations has-pending-model-changes` reports no pending changes.
- Forward migration SQL creates the table, lifecycle checks and unique source-event index.
- Unit/model/contract tests confirm tenant filtering, source-event uniqueness and compatible version-1 payloads.
- Database and broker integration evidence must prove first delivery creates one boundary and one acknowledgement, duplicate delivery creates neither again, a controlled plan conflict emits failure evidence, and replay remains idempotent.

## Rollback and forward fix

Rollback is safe only before a boundary or acknowledgement exists. Once provisioning evidence exists, preserve the table and use an append-only forward-fix migration. Do not delete inbox, outbox or boundary evidence to repeat provisioning.
