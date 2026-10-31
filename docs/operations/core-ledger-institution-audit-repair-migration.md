# Core Ledger institution audit and repair migration

Migration: `20261026084852_AddInstitutionAuditAndProvisioningRepairEvidence`

The migration adds `audit.institution_audit_events`, keyed by institution and audit event ID, with
bounded actor, permission, action, resource, outcome, reason, correlation, before/after references
and occurred time. It contains no payload or secret column. The migration grants `coreledger_app`
select/insert only and explicitly revokes update/delete/truncate. Runtime code also rejects update
or delete tracking for this evidence and exposes no mutation endpoint.

## Rollout and verification

1. Back up `core_ledger_db` using the approved environment procedure.
2. Apply the migration with the Core Ledger migration principal.
3. Deploy API and worker versions together; verify the `CoreBanking.Institutions` meter is exported.
4. Run `dotnet ef migrations has-pending-model-changes` and the Core Ledger test suite.
5. In a synthetic institution, request one eligible provisioning replay and verify the outbox reset,
   repaired dead-letter marker and audit insertion commit together.

Do not roll back after audit evidence exists. Preserve the table and use an append-only forward-fix
migration. Production deployment must verify the runtime principal retains select/insert only.
