# Risk and Reporting institution boundary migration

Migration: `20261019104154_InitialInstitutionBoundary`

Scope: Phase 1 Institution and Tenancy slice 6 and ADR-0007.

## Data impact

- Creates the Risk and Reporting-owned `platform.institution_boundaries` table.
- Creates the service-owned integration outbox, inbox, idempotency and dead-letter tables.
- Adds no seed data and does not modify another service's database.
- Enforces fail-closed institution filtering and unique source-event identity.

## Rollout

1. Back up `risk_reporting_db` according to the approved recovery procedure.
2. Apply the migration with the Risk and Reporting migration principal.
3. Verify the platform and integration tables, constraints and indexes.
4. Deploy the worker with `ConnectionStrings__RiskReporting` and `Messaging__Kafka__BootstrapServers` configured.
5. Confirm its consumer group reads the Core Ledger institution topic and publishes acknowledgements from its local outbox.

## Verification

- `dotnet ef migrations has-pending-model-changes` reports no pending changes.
- The initial forward SQL creates only service-owned schemas and tables.
- Unit model tests verify fail-closed filters and unique source-event identity.
- Environment evidence must prove boundary, inbox outcome and acknowledgement outbox record commit atomically.

## Rollback and forward fix

Rollback is safe only before a boundary or integration record exists. Once evidence is written, retain it and use an append-only forward-fix migration.
