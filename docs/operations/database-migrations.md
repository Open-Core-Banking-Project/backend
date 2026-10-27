# Database migration operations

Runbook for inspecting, verifying, generating, reviewing and deploying EF Core migrations across the
nine service databases. Decisions: ADR-0004 (one database per service) and ADR-0013 (deployment,
migration, backup, restore, RTO and RPO).

Each service owns one database, one `DbContext`, one migrations assembly and two principals:

| Service | Database | Context | Migration project | Application principal | Migration principal |
| --- | --- | --- | --- | --- | --- |
| Core Ledger | `core_ledger_db` | `CoreLedgerDbContext` | `src/Services/CoreLedger/src/CoreBanking.CoreLedger.Infrastructure` | `coreledger_app` | `coreledger_migrator` |
| Customer Onboarding | `customer_onboarding_db` | `CustomerOnboardingDbContext` | `src/Services/CustomerOnboarding/src/CoreBanking.CustomerOnboarding.Infrastructure` | `customeronboarding_app` | `customeronboarding_migrator` |
| Identity and Access | `identity_access_db` | `IdentityAccessDbContext` | `src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure` | `identityaccess_app` | `identityaccess_migrator` |
| Payments and VTU | `payments_vtu_db` | `PaymentsVtuDbContext` | `src/Services/PaymentsVtu/src/CoreBanking.PaymentsVtu.Infrastructure` | `paymentsvtu_app` | `paymentsvtu_migrator` |
| Channels and Engagement | `channels_engagement_db` | `ChannelsEngagementDbContext` | `src/Services/ChannelsEngagement/src/CoreBanking.ChannelsEngagement.Infrastructure` | `channelsengagement_app` | `channelsengagement_migrator` |
| Operations and Workflow | `operations_workflow_db` | `OperationsWorkflowDbContext` | `src/Services/OperationsWorkflow/src/CoreBanking.OperationsWorkflow.Infrastructure` | `operationsworkflow_app` | `operationsworkflow_migrator` |
| Risk and Reporting | `risk_reporting_db` | `RiskReportingDbContext` | `src/Services/RiskReporting/src/CoreBanking.RiskReporting.Infrastructure` | `riskreporting_app` | `riskreporting_migrator` |
| Integration Hub | `integration_hub_db` | `IntegrationHubDbContext` | `src/Services/IntegrationHub/src/CoreBanking.IntegrationHub.Infrastructure` | `integrationhub_app` | `integrationhub_migrator` |
| Audit and Reconciliation | `audit_reconciliation_db` | `AuditReconciliationDbContext` | `src/Services/AuditReconciliation/src/CoreBanking.AuditReconciliation.Infrastructure` | `auditreconciliation_app` | `auditreconciliation_migrator` |

The migration project is also the startup project: each `CoreBanking.<Context>.Infrastructure`
project contains its own `IDesignTimeDbContextFactory`, which builds the model without configuration
and never opens a connection. The design-time fallback connection string is not a credential.

## Inspect migration status

List the committed migrations and, when a database is reachable, which are applied:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations list `
  --project src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure `
  --startup-project src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure `
  --context IdentityAccessDbContext --configuration Release
```

Add `--no-connect` to list the committed migrations without touching a database. Add `--json` when a
script must parse the result. Replace the project, context and connection string for another service:

```powershell
$env:ConnectionStrings__CoreLedger = 'Host=127.0.0.1;Port=5432;Database=core_ledger_db;Username=coreledger_migrator;Password=<from the secret store>'
```

The environment variable name is always `ConnectionStrings__<Service>` and matches
`<Context>DbContextFactory.ConnectionStringVariableName`.

## Run migration-drift verification

`scripts/migrations-verify.ps1` verifies every context and is part of `scripts/verify.ps1` and CI:

```powershell
pwsh scripts/migrations-verify.ps1                 # restore, build, verify all nine contexts
pwsh scripts/migrations-verify.ps1 -NoBuild        # after `dotnet build`
pwsh scripts/migrations-verify.ps1 -ContextName IdentityAccess
```

The gate is read-only. It clears inherited `ConnectionStrings__*` variables, uses
`migrations list --no-connect`, and runs `migrations has-pending-model-changes` for each context, so
it cannot reach a database, generate a migration or apply one. Exit codes:

| Exit | Meaning | Action |
| --- | --- | --- |
| 0 | Every context matches its committed migrations | None |
| 1 | Migration drift: the model changed and no migration records it | Add the migration described below |
| 2 | Tooling, SDK, project or design-time factory failure | Fix the reported failure; the drift result is unknown |
| 3 | Drift and a tooling failure were both observed | Fix the tooling failure first, then re-run |

A migration that was created but left empty is drift too: the snapshot records the model, the
migration records nothing, and `has-pending-model-changes` reports a mismatch only after the next
model change. Do not commit a generated migration until its `Up` and `Down` are written.

## Generate a migration safely

1. Confirm the tree is clean enough to review: `git status --short`.
2. Run the drift gate first. It must report the drift you are about to record.
3. Generate an empty migration, then write the operations:

```powershell
dotnet tool run dotnet-ef migrations add AddSessionLifecycleAndSecurityLocks `
  --project src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure `
  --startup-project src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure `
  --context IdentityAccessDbContext --configuration Release
```

4. Review the generated snapshot changes before editing `Up`/`Down`. A migration that changes the
   snapshot but is left empty must not be committed.
5. Add nullable columns first, backfill deterministically, validate the backfill in a `DO` block
   that raises an exception with counts, and only then apply `NOT NULL`, checks, unique indexes and
   foreign keys. Existing examples: `20261027150150_AddSessionLifecycleAndSecurityLocks`.
6. Implement `Down` for what can be reversed and state what cannot. A backfilled synthetic value
   cannot be turned back into an absent column; say so in the migration remarks.
7. Re-run the drift gate. It must report every context as consistent.

Never edit a migration that may have been applied anywhere. Add a new migration instead: migration
history is the only record of what a deployed database has already executed.

## Review migration SQL before deployment

Read the SQL the migration will actually run rather than only the C# operations:

```powershell
dotnet tool run dotnet-ef migrations script 20261027103324_AddSessionRevocationEventReference 20261027150150_AddSessionLifecycleAndSecurityLocks `
  --project src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure `
  --startup-project src/Services/IdentityAccess/src/CoreBanking.IdentityAccess.Infrastructure `
  --context IdentityAccessDbContext --configuration Release --output artifacts/migrations/identity-access-session-lifecycle.sql
```

Review the script for: unexpected `DROP`, destructive `ALTER`, a `NOT NULL` without a backfill, a
unique index that can fail on existing duplicates, grants to `PUBLIC`, and statements that assume a
role exists. `--idempotent` produces a script that can be re-run; it does not make destructive
operations reversible.

## Back up and restore

Local infrastructure owns its own backup path:

```powershell
powershell -File scripts/infra.ps1 -Action backup                     # every database
powershell -File scripts/infra.ps1 -Action backup -Database identity_access_db
powershell -File scripts/infra.ps1 -Action restore -Database identity_access_db
```

`-Action backup` writes `pg_dump --format=custom` files to `artifacts/backups/<timestamp>/` inside
the `corebanking-postgres` container and copies them out. `-Action restore` proves a dump is usable:
it restores the newest (or `-BackupPath`) dump into a scratch database named
`<database>_restore_check`, counts its tables and drops the scratch database. It does not overwrite
the live database.

Recovering a live database is an operator action performed under the approved environment procedure
for the deployed database, not with these local-device scripts. Take a fresh backup immediately
before any migration that changes data, verify it with `-Action restore`, and keep it until the
deployment is accepted.

## Deploy schema changes in the correct order

1. Back up the target database and verify the backup.
2. Deploy the migration with the **migration principal** only, for example
   `identityaccess_migrator` for `identity_access_db`. The application principal never runs DDL.
3. Run `migrations list` against the target to confirm every migration is applied and none is
   pending.
4. Deploy the service build that expects the new schema.
5. Confirm the application principal can read and write the new objects (see below).

Order across services: Identity and Access first when a schema change is a prerequisite for another
service's contract, then the producing service, then consumers. A service never migrates another
service's database. Never let two principals migrate the same database at the same time: the EF Core
migration lock serialises attempts, and a waiting migration should be investigated rather than
retried blindly.

## Verify session lifecycle fields and security locks after deployment

After applying `20261027150150_AddSessionLifecycleAndSecurityLocks` to `identity_access_db`:

```sql
-- Columns and constraints exist.
SELECT column_name, is_nullable, data_type
FROM information_schema.columns
WHERE table_schema = 'identity' AND table_name = 'sessions'
ORDER BY ordinal_position;

SELECT conname FROM pg_constraint
WHERE conrelid = 'identity.sessions'::regclass AND contype = 'c';

-- Every legacy row was classified and given a distinguishable legacy marker.
SELECT status, count(*) FROM identity.sessions GROUP BY status ORDER BY status;
SELECT count(*) FROM identity.sessions WHERE status IS NULL OR last_activity_at IS NULL;
SELECT count(*) FROM identity.sessions
WHERE provider_session_fingerprint LIKE 'legacy:%' AND length(provider_session_fingerprint) <> 39;
SELECT count(*) FROM identity.sessions WHERE provider_session_fingerprint = '';

-- The tenant-unique fingerprint index and the lock table are present.
SELECT indexname FROM pg_indexes
WHERE schemaname = 'identity' AND tablename = 'sessions';
SELECT count(*) FROM security.locks;

-- The application principal can use the new objects but still cannot change the schema.
SET ROLE identityaccess_app;
SELECT has_table_privilege(current_user, 'identity.sessions', 'SELECT'),
       has_table_privilege(current_user, 'security.locks', 'INSERT'),
       has_schema_privilege(current_user, 'identity', 'CREATE');
RESET ROLE;
```

A legacy marker is `legacy:` plus 32 hexadecimal characters. Real provider fingerprints are
64-character hexadecimal SHA-256 digests, so the two can never collide. A legacy session was never
bound to a provider session; when a refresh presents a provider reference the existing reuse check
refuses it and revokes the session. Expect legacy sessions to require re-authentication.

## Diagnose a failed migration and recover safely

Start with the migration lock and the history table:

```sql
SELECT * FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND query ILIKE '%__EFMigrationsHistory%';
SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";
```

A failing `DO` block raises `Session lifecycle backfill failed: missing_values=..., invalid_status=...`.
That message means identity data violates a rule the migration will not guess about:

`P0001: Identity kind mapping is required before applying AddIdentityMembershipAndProviderLinks.`
is a different, older guard: the database holds Phase 0 identities that have no `identity_type` or
institution membership. Follow `docs/operations/identity-access-legacy-identity-kind-mapping.md`;
do not delete the guard or invent a kind.

A session lifecycle failure means:

- `missing_values` — a row could not be classified. Inspect it, then correct the data. Do not add a
  default to make the migration pass.
- `invalid_status` — a stored status is outside the lifecycle enumeration. Determine the intended
  state from the authentication evidence before changing it.
- `invalid_fingerprint` — a stored or backfilled fingerprint is empty or longer than the column.
- `duplicate_fingerprints` — two sessions in one institution claim the same provider session.
  Resolve which row is genuine; the unique index will not be relaxed.
- `lifecycle_mismatch` — status contradicts `revoked_at`/`expires_at`.

Recovery rules:

- EF Core applies each migration in a transaction unless the provider cannot. If the migration
  failed, the transaction rolled back: fix the cause, then run `database update` again.
- If the process died between the DDL and the history insert, compare the schema with
  `migrations script` output before retrying; re-running is safe only for statements that are
  naturally idempotent.
- Prefer a forward-fix migration over `database update <previous>` once anything has been written.
  `Down` drops columns and therefore discards backfilled state.
- Restore from the verified backup only when the database is unusable. Document the restore point
  and the events replayed afterwards.

## Verify the whole workflow

```powershell
powershell -File scripts/infra.ps1 -Action up          # local PostgreSQL, broker, cache, collector
powershell -File scripts/verify.ps1                    # restore, build, drift gate, tests, format
powershell -File scripts/db-verify.ps1                 # migrate all nine, database tests, privilege tests
```

`scripts/db-verify.ps1` applies every service's migrations with its migration principal, lists the
applied migrations, then runs each service's `Category=Database` and `Category=Integration` tests as
its application principal, and finally runs `tests/CoreBanking.SecurityTests` against all
nine principals. Never point it at a shared or production database: it applies migrations and
deletes test rows.
