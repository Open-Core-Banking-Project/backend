# Core Ledger initial institution migration

Migration: `20261018132602_InitialInstitutionProvisioning`

Scope: Phase 1 Institution and Tenancy, implementation slices 3-4 (`CFG-001`, `CFG-002`, `ARC-004`, `DAT-005`, `DAT-007`, `DAT-008`, `US-001`).

## Data impact

- Creates the `institution` schema in `core_ledger_db`.
- Creates empty `institutions`, `provisioning_runs` and `provisioning_steps` tables.
- Adds no seed data and does not read, update or delete an existing row.
- Enforces a globally unique institution code, one active provisioning run per institution, tenant-scoped primary/foreign keys, lifecycle checks and failure-code consistency.

## Rollout

1. Back up `core_ledger_db` according to the environment's approved recovery procedure.
2. Run the migration with the Core Ledger migration principal; never use the application principal.
3. Verify the schema, tables, constraints and indexes before enabling institution command handlers.
4. Deploy the API with `ConnectionStrings__CoreLedger` set to the application-principal connection string.
5. Confirm `/health/ready` reports healthy before routing traffic.

No backfill or long-running lock is expected because this migration only creates new objects.

## Verification

- `dotnet ef migrations has-pending-model-changes` reports no pending changes.
- `dotnet ef migrations script 0 20261018132602_InitialInstitutionProvisioning` generates the expected forward SQL.
- PostgreSQL catalogue checks confirm the three tables, the unique institution-code index, the filtered active-run index, and tenant-scoped foreign keys.
- The Core Ledger persistence-model tests confirm fail-closed query filters and institution-scoped keys.

## Rollback and forward fix

Rollback is safe only before any institution or provisioning row has been created. After data exists, do not run `Down`; use an append-only forward-fix migration so institution and provisioning evidence is preserved. This migration does not authorise institution deletion or activation.
