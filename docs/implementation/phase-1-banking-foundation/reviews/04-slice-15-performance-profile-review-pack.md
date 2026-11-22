# P1-E04-S15 performance and recovery evidence-gates review pack

## Review request

Review the bounded P1-E04-S15 increments that turn the approved `P1-E04-D16` targets into versioned configuration and
separate fail-closed performance and recovery evidence gates. The increments do not generate load, run against
production data, configure recovery or claim that any latency, capacity, RTO or RPO objective has been met.

## Traceability

- `P1-E04-D16`: 500,000 active accounts per institution; concurrency 1/8/32; cold and warm modes; three repetitions;
  approved read, write and account-opening p95/p99 targets; 0.1% technical-error ceiling.
- ACC-033: recovery objectives and their claim prerequisites remain explicit without treating the local restore drill
  as RTO/RPO evidence.
- Phase 1 acceptance: every result identifies its profile, environment, dataset, samples and percentiles.

## Delivered increment

- `tests/performance/profiles/p1-e04-initial-launch.v1.json` is the single versioned target source.
- `scripts/performance-profile-gate.ps1` validates the profile and gates a complete captured measurement matrix.
- `scripts/recovery-evidence-gate.ps1` gates a fresh-cluster recovery result from detection through verified service
  restoration, including point-in-time evidence, zero committed-transaction loss and all nine service databases.
- The gate rejects missing, duplicate or unexpected observations, workstation evidence, dataset mismatch, invalid
  percentile ordering, p95/p99 breaches and technical-error breaches.
- `scripts/phase1-evidence.ps1` runs both gate self-tests while continuing to label real performance and recovery
  evidence as outstanding. A bounded VSTest connection failure now produces a failed, complete report instead of
  aborting before the remaining evidence sections run.
- `docs/operations/performance-evidence-gate.md` documents the evidence boundary and invocation.
- `docs/operations/recovery-evidence-gate.md` documents the recovery result contract and honesty boundary.

## Verification performed

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/performance-profile-gate.ps1 -SelfTest`
  - passed one complete synthetic measurement matrix;
  - passed four negative cases: missing observation, latency breach, workstation result and dataset mismatch.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/performance-profile-gate.ps1`
  - profile validation passed and explicitly reported that no measurement result was evaluated.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/recovery-evidence-gate.ps1 -SelfTest`
  - passed one complete synthetic recovery result;
  - passed five negative cases: RTO breach, committed-transaction loss, missing off-site copy, workstation recovery and
    incomplete database evidence.
- PowerShell parsed both changed scripts; the profile parsed as JSON; `git diff --check` passed for the bounded files;
  the slice CSV retained its final document/status columns; the implementation router still selects `P1-E04-S15`.
- `scripts/phase1-evidence.ps1 -Configuration Release -SkipGate` was attempted with the default 90-second VSTest
  connection window and twice with 180 seconds, including the 2026-11-22 recovery-gate pass. All stopped before the
  harness could finish because VSTest could not connect to a testhost process. One direct Audit Reconciliation run
  passed 7 tests before a subsequent exact-suite retry hit the same startup timeout. No assertion failure was observed,
  but the affected harness is not recorded as passed and must be rerun in a healthy test-host session.
- After the resilience fix, `scripts/phase1-evidence.ps1 -Configuration Release -SkipGate
  -TestHostConnectionTimeoutSeconds 30 -ReportDirectory artifacts/automation` exited 1 after the same Audit
  Reconciliation testhost timeout, wrote a complete report, skipped the eight remaining service projects, passed the
  59-capability grammar scan and both evidence-gate self-tests, and recorded supply-chain status as unknown. PowerShell
  syntax and `git diff --check` passed; the router still selects `P1-E04-S15` as `in progress`.

## Compatibility and operational impact

- No production code, API, contract, migration or database data changes.
- The profile is a release-test input only and the gate is not part of ordinary deterministic unit tests.
- A future acceptance runner must emit the documented result shape; changing targets requires a new reviewed profile
  version rather than editing application code.

## Remaining risks and gates

- No dedicated acceptance environment or load generator exists yet, so no performance or capacity result is claimed.
- Zero committed-transaction loss needs durable recovery of every acknowledged commit; WAL archiving and a periodic
  dump alone cannot satisfy it.
- Fresh-cluster restore, off-site copy, service start and verification still need objective-measured evidence.
- `P1-E04-D14` still blocks the existing-account migration portion of the end-to-end Phase 1 journey.

## Rollback and forward fix

Removing the profile, gates and evidence-harness changes returns the repository to its previous evidence posture and
does not affect runtime behaviour. Prefer a new profile version for approved target changes; fix gate defects in place
before any result is accepted.
