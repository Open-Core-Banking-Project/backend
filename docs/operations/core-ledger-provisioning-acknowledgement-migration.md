# Core Ledger provisioning acknowledgement migration

Migration: `20261018141828_AddProvisioningAcknowledgementEvidence`

Scope: Phase 1 Institution and Tenancy slice 6 (`CFG-001`, `CFG-002`, `ARC-004`, `ARC-006`, `ARC-007`, `DAT-005`, `DAT-007`, `US-001`) and ADR-0007.

## Data impact

- Adds nullable `acknowledgement_event_id` and `acknowledged_at` columns to `institution.provisioning_steps`.
- Existing steps remain valid and require no backfill.
- The columns preserve the service acknowledgement that caused a step to become initialised or failed.
- No institution is activated by this migration or by the acknowledgement consumer; a fully acknowledged run stops at `pending_activation`.

## Rollout

1. Apply the two earlier Core Ledger institution migrations first.
2. Back up `core_ledger_db` according to the approved recovery procedure.
3. Apply this migration with the Core Ledger migration principal.
4. Deploy the Core Ledger messaging worker with `ConnectionStrings__CoreLedger` and `Messaging__Kafka__BootstrapServers` configured.
5. Start the worker only after the Identity and Access boundary migration and worker are deployed.

Both columns are nullable additions, so no table rewrite or data backfill is expected.

## Verification

- `dotnet ef migrations has-pending-model-changes` reports no pending changes.
- A forward SQL script contains the two nullable column additions.
- Model tests confirm both acknowledgement evidence fields are mapped.
- End-to-end environment evidence must show that a successful Identity and Access acknowledgement updates only its configured step and does not activate the institution.

## Rollback and forward fix

Rollback is safe only before acknowledgement evidence is written. Once evidence exists, retain the columns and use an append-only forward-fix migration. Never clear acknowledgement evidence or inbox records to force a replay.
