# Audit Reconciliation institution boundary migration

Migration: `20261018143048_AddInstitutionBoundaryReference`

Scope: Phase 1 Institution and Tenancy slice 6 and ADR-0007.

## Data impact

- Creates `platform.institution_boundaries` in the Audit Reconciliation database.
- Adds no seed rows and does not modify append-only audit evidence.
- Stores the institution identifier, provisioning-plan version, non-active lifecycle status, source-event identity and initialisation time.
- Enforces unique source-event identity and a fail-closed institution query filter.

## Rollout

1. Apply the existing Audit Reconciliation migrations first.
2. Back up the service-owned database.
3. Apply this migration with the migration principal.
4. Deploy the updated worker with its database connection and Kafka bootstrap configuration.
5. Confirm the worker subscribes to both the foundation and Core Ledger institution topics and can publish its acknowledgement through the outbox.

## Verification

- `dotnet ef migrations has-pending-model-changes` reports no pending changes.
- The forward SQL creates only the new service-owned boundary table and indexes.
- Unit model tests confirm the boundary is tenant filtered and source-event identity is unique.
- Environment evidence must prove boundary, inbox outcome and acknowledgement outbox record commit atomically.

## Rollback and forward fix

Rollback is safe only before a boundary or acknowledgement is written. Once evidence exists, preserve it and use an append-only forward-fix migration.
