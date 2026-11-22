# Recovery evidence gate

`P1-E04-D16` sets a four-hour RTO and zero loss of committed journals, idempotency outcomes and audit records. Those
are acceptance targets, not claims about the local compose stack.

Validate the versioned recovery contract and its fail-closed cases:

```powershell
powershell -File scripts/recovery-evidence-gate.ps1 -SelfTest
```

This command does not create a backup, alter PostgreSQL or measure recovery. The dedicated acceptance environment must
first perform a point-in-time restore into a fresh cluster and emit a result containing:

- stable identities for the environment, retained base backup, WAL archive and off-site copy;
- proof that every acknowledged commit is durably recoverable;
- ordered timestamps from detection through service verification;
- acknowledged, recovered and lost committed-transaction counts;
- verification of journals, idempotency outcomes and audit records;
- restored-and-verified evidence for all nine service databases.

Gate the captured result separately:

```powershell
powershell -File scripts/recovery-evidence-gate.ps1 `
  -ResultsPath artifacts/p1-e04-recovery-result.json
```

The gate rejects workstation or non-fresh-cluster results, missing prerequisites or evidence identities, non-monotonic
timelines, a duration over four hours, any committed-transaction loss, unverified protected evidence and an incomplete
database set.

The existing `scripts/restore-drill.ps1` remains useful row-count evidence. It cannot produce this result because it
does not restore from a retained off-site backup, replay to a timestamp, recover every acknowledged commit or start and
verify the services.
