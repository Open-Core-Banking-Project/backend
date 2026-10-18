# Core Ledger institution creation messaging migration

Migration: `20261018134139_AddInstitutionCreationMessaging`

Scope: Phase 1 Institution and Tenancy slice 5 (`CFG-001`, `CFG-002`, `ARC-004`, `ARC-006`, `ARC-007`, `DAT-005`, `DAT-007`, `DAT-008`, `US-001`) and ADR-0007.

## Data impact

- Creates the Core Ledger-owned `integration` schema and its outbox, inbox, idempotency and dead-letter tables.
- Adds no seed data and does not modify existing institution or provisioning rows.
- Enforces tenant-scoped primary keys and unique outbox event identity per institution.
- Stores only event identifiers, canonical envelope JSON and operational evidence; institution provisioning payloads contain no secret material.

## Rollout

1. Apply `20261018132602_InitialInstitutionProvisioning` first.
2. Back up `core_ledger_db` according to the environment's approved recovery procedure.
3. Apply this migration with the Core Ledger migration principal.
4. Verify integration tables, indexes and application-principal grants.
5. Deploy the API with the relational store configured, while leaving the institution HTTP route disabled until platform authentication and permission enforcement are available.

No backfill or long-running lock is expected because this migration only creates new objects.

## Verification

- `dotnet ef migrations has-pending-model-changes` reports no pending changes.
- A forward SQL script includes all four Core Ledger-owned integration tables and their indexes.
- Model tests confirm every integration record has a fail-closed institution query filter.
- Database integration evidence must prove source institution state and outbox commit or roll back together, same-key replay returns the first identifiers, changed-request reuse is rejected, and duplicate institution codes produce `institution_code_conflict`.

## Rollback and forward fix

Rollback is safe only before an institution-creation command writes an idempotency or outbox record. Once evidence exists, do not drop these tables; use an append-only forward-fix migration. Never remove unpublished outbox or unresolved dead-letter records to force a rollback.
