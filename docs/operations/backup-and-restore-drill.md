# Backup and restore drill

`scripts/restore-drill.ps1` proves that each service database can be dumped and restored into a fresh
database without losing rows. It is the recovery evidence the repository can produce without an
approved RTO or RPO; it does not measure or claim either.

## What the drill does

1. Checks that `corebanking-postgres` is running and that PostgreSQL is reachable on the port in
   `deploy/compose/.env`.
2. Writes the administrator password to `/root/.pgpass` inside the container with mode `0600`, so no
   credential appears on a command line or in this repository.
3. Dumps the service database with `pg_dump -Fc` into a scratch directory inside the container.
4. Creates `<database>_drill` from `template0`, restores the dump into it, and compares the row count
   of every non-system table between the source and the restored database.
5. Removes the scratch databases, the dump directory and the password file, unless
   `-KeepScratchDatabase` is passed.

The drill reads only the source database and never writes to it. It needs no application principal,
so a recovery does not depend on the service credentials.

If a run is killed before the cleanup step, drop `<database>_drill` yourself, delete
`/tmp/cb-restore-drill-*` inside the container and remove `/root/.pgpass`. The password file lives only
inside the development container and is recreated by the next run.

## Running it

```powershell
# One service (the default is Core Ledger).
powershell -File scripts/restore-drill.ps1

# Every service. Use a comma-separated list from a session or -Command: -File passes one string.
& scripts/restore-drill.ps1 -Service CoreLedger,CustomerOnboarding,IdentityAccess,PaymentsVtu,ChannelsEngagement,OperationsWorkflow,RiskReporting,IntegrationHub,AuditReconciliation

# Keep the scratch database behind for inspection.
& scripts/restore-drill.ps1 -Service CoreLedger -KeepScratchDatabase
```

The report is written to `artifacts/restore-drill-<timestamp>.md` (git-ignored) and the console shows
one line per service. The exit code is 0 only when every selected service restored with a matching row
count.

## Interpreting the result

| Result | Meaning |
| --- | --- |
| `pass` | The dump restored and every table holds the same number of rows as the source |
| Row-count mismatch | The restore lost or duplicated rows; treat as a defect before any recovery is trusted |
| `pg_dump` or `pg_restore` error | The dump or the restore is incomplete; the row counts in the report are not evidence |
| Scratch database left behind | The cleanup step did not finish; drop `<database>_drill` manually |

A row-count match is necessary and not sufficient. It does not prove that constraint definitions,
indexes, grants or sequences match, and it does not prove the restored database serves the
application.

## What the drill does not cover

- **No RTO.** The restore duration covers `pg_restore` only. Detection, decision, environment
  provisioning, service start-up and verification are excluded, and no target exists yet: `P1-E04-D16`
  owns the recovery objectives.
- **No RPO.** The source is a live primary, not a retained backup, so nothing here says how much
  committed work a real recovery would lose.
- **No point-in-time recovery.** WAL archiving, a retained base backup and a restore to an arbitrary
  timestamp are not configured on the local stack.
- **No off-site copy, encryption at rest or key custody.** The dump stays inside the development
  container and is deleted afterwards.
- **No failover or replica.** The compose stack is a single node.
- **No production volume.** The measured row counts are development and test residue.

## Before a recovery claim is published

1. `P1-E04-D16` is approved with an RPO expressed in committed financial transactions and an RTO that
   names every stage.
2. Continuous WAL archiving and a retained base backup exist, with the retention window agreed.
3. A restore into a fresh cluster serves the smoke journey, with the elapsed time recorded per stage.
4. The restored database reconciles against the audit and outbox records, including exactly one
   idempotency outcome per financial request and no duplicate consumer effect.
